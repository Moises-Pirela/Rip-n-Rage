using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.Culling;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.Particles
{
    /// <summary>
    /// Draws every live particle of <see cref="ParticleWorld"/> in the world pass: emitters are sorted back to
    /// front (then by <c>sortOrder</c>), consecutive emitters sharing texture / blend / lighting are batched into
    /// one dynamic vertex buffer upload and draw call. Quads are built on the CPU per render mode
    /// (billboard, velocity-stretched, horizontal, surface-aligned). Depth tested, never depth written.
    /// </summary>
    public sealed unsafe class ParticleRenderer : IDisposable
    {
        private const int MaxQuads = 8192;
        private const int FloatsPerVertex = 9; // pos3 uv2 rgba4
        private readonly float[] _verts = new float[MaxQuads * 4 * FloatsPerVertex];
        private uint _vao, _vbo, _ebo;
        private int _quadCount;
        private readonly Shader _shader;
        private readonly List<Item> _items = new List<Item>();

        public int DrawCalls { get; private set; }
        public int QuadsDrawn { get; private set; }

        private struct Item
        {
            public ParticleEffectInstance inst;
            public EmitterState st;
            public float depth;
        }

        public ParticleRenderer()
        {
            _shader = ShaderLibrary.Get("particle");
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _verts.Length * sizeof(float), null, GL.DYNAMIC_DRAW);
            // static index pattern: two triangles per quad
            var idx = new uint[MaxQuads * 6];
            for (int q = 0; q < MaxQuads; q++)
            {
                uint b = (uint)(q * 4);
                idx[q * 6] = b; idx[q * 6 + 1] = b + 1; idx[q * 6 + 2] = b + 2;
                idx[q * 6 + 3] = b; idx[q * 6 + 4] = b + 2; idx[q * 6 + 5] = b + 3;
            }
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            fixed (uint* p = idx) GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL.STATIC_DRAW);
            int stride = FloatsPerVertex * sizeof(float);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, GL.FLOAT, false, stride, 5 * sizeof(float));
            GL.BindVertexArray(0);
        }

        /// <summary>Builds and draws all particle batches. <paramref name="prepare"/> uploads the per-frame uniforms (view-projection, lighting) for a shader.</summary>
        public void Render(Camera cam, in Matrix4x4 view, in Matrix4x4 viewProj, bool frustumCull, Action<Shader> prepare)
        {
            DrawCalls = 0; QuadsDrawn = 0;
            var instances = ParticleWorld.Instances;
            if (instances.Count == 0) return;

            Frustum frustum = frustumCull ? Frustum.FromViewProj(viewProj) : default;
            _items.Clear();
            foreach (var inst in instances)
            {
                bool visible = !frustumCull || frustum.IntersectsSphere(inst.position, inst.def.cullRadius * inst.scale);
                inst.Culled = !visible;
                if (!visible) continue;
                float depth = view.MultiplyPoint3x4(inst.position).z;
                foreach (var st in inst.emitters)
                {
                    if (st.count == 0 || st.texture == null) continue;
                    _items.Add(new Item { inst = inst, st = st, depth = depth });
                }
            }
            if (_items.Count == 0) return;
            // far first; within the same effect the emitter's sortOrder decides
            _items.Sort((a, b) =>
            {
                if (a.inst == b.inst) return a.st.def.sortOrder.CompareTo(b.st.def.sortOrder);
                return b.depth.CompareTo(a.depth);
            });

            prepare(_shader);
            _shader.Use();
            GL.Enable(GL.DEPTH_TEST);
            GL.DepthMask(false);
            GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);

            Vector3 camPos = cam.transform.position;
            Vector3 camRight = cam.transform.right, camUp = cam.transform.up, camFwd = cam.transform.forward;

            Texture2D batchTex = null; ParticleBlend batchBlend = ParticleBlend.Alpha; bool batchLit = false;
            _quadCount = 0;
            foreach (var item in _items)
            {
                var e = item.st.def;
                if (item.st.texture != batchTex || e.blend != batchBlend || e.lit != batchLit)
                {
                    Flush(batchTex, batchBlend, batchLit);
                    batchTex = item.st.texture; batchBlend = e.blend; batchLit = e.lit;
                }
                AppendEmitter(item.inst, item.st, camPos, camRight, camUp, camFwd, batchTex, batchBlend, batchLit);
            }
            Flush(batchTex, batchBlend, batchLit);

            GL.DepthMask(true);
            GL.BindVertexArray(0);
        }

        private void AppendEmitter(ParticleEffectInstance inst, EmitterState st, Vector3 camPos, Vector3 camRight, Vector3 camUp, Vector3 camFwd, Texture2D tex, ParticleBlend blend, bool lit)
        {
            var e = st.def;
            Vector3 normal = inst.Forward;
            // surface frame for Aligned mode
            Vector3 tangent = Vector3.Cross(normal, MathF.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up);
            if (tangent.sqrMagnitude < 1E-08f) tangent = Vector3.right; else tangent = tangent.normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            for (int i = 0; i < st.count; i++)
            {
                ref Particle p = ref st.particles[i];
                float t = p.life > 0f ? Mathf.Clamp01(p.age / p.life) : 1f;

                // sprite / uv
                int frame = e.animateFrames && st.sprites.Length > 1 ? Math.Min(st.sprites.Length - 1, (int)(t * st.sprites.Length)) : Math.Min(p.frame, st.sprites.Length - 1);
                var sprite = st.sprites[frame] ?? st.sprites[0];
                if (sprite == null) continue;
                sprite.GetUVs(out float u0, out float v0, out float u1, out float v1);

                // colour
                Color c = Color.Lerp(e.colorStart, e.colorEnd, t);
                float alpha = c.a;
                if (e.fadeIn > 0f && t < e.fadeIn) alpha *= t / e.fadeIn;
                float b = e.brightness * p.tint;
                c = new Color(c.r * b, c.g * b, c.b * b, alpha);

                // size
                float size = Mathf.Lerp(p.size0, p.size1, t);
                float hw = size * 0.5f, hh = size * e.aspect * 0.5f;
                float rad = p.rotation * Mathf.Deg2Rad;
                float cs = MathF.Cos(rad), sn = MathF.Sin(rad);

                Vector3 ax, ay; // half-extent axes
                switch (e.renderMode)
                {
                    case ParticleRenderMode.Stretched:
                    {
                        Vector3 dir = p.velocity;
                        float speed = dir.magnitude;
                        if (speed < 1E-04f) dir = camUp; else dir /= speed;
                        float len = size * e.stretchLength + speed * e.stretchVelocity;
                        Vector3 toCam = camPos - p.position;
                        Vector3 side = Vector3.Cross(dir, toCam);
                        side = side.sqrMagnitude > 1E-08f ? side.normalized : camRight;
                        ax = dir * (len * 0.5f);
                        ay = side * hh;
                        break;
                    }
                    case ParticleRenderMode.Horizontal:
                        ax = (Vector3.right * cs + Vector3.forward * sn) * hw;
                        ay = (Vector3.forward * cs - Vector3.right * sn) * hh;
                        break;
                    case ParticleRenderMode.Aligned:
                        ax = (tangent * cs + bitangent * sn) * hw;
                        ay = (bitangent * cs - tangent * sn) * hh;
                        break;
                    default: // Billboard
                        ax = (camRight * cs + camUp * sn) * hw;
                        ay = (camUp * cs - camRight * sn) * hh;
                        break;
                }

                if (_quadCount >= MaxQuads) Flush(tex, blend, lit);
                int k = _quadCount * 4 * FloatsPerVertex;
                Vector3 pos = p.position;
                Put(k, pos - ax - ay, u0, v0, c); k += FloatsPerVertex;
                Put(k, pos + ax - ay, u1, v0, c); k += FloatsPerVertex;
                Put(k, pos + ax + ay, u1, v1, c); k += FloatsPerVertex;
                Put(k, pos - ax + ay, u0, v1, c);
                _quadCount++;
            }
        }

        private void Put(int k, Vector3 p, float u, float v, Color c)
        {
            _verts[k] = p.x; _verts[k + 1] = p.y; _verts[k + 2] = p.z;
            _verts[k + 3] = u; _verts[k + 4] = v;
            _verts[k + 5] = c.r; _verts[k + 6] = c.g; _verts[k + 7] = c.b; _verts[k + 8] = c.a;
        }

        private void Flush(Texture2D tex, ParticleBlend blend, bool lit)
        {
            if (_quadCount == 0 || tex == null) { _quadCount = 0; return; }
            switch (blend)
            {
                case ParticleBlend.Additive: GL.BlendFunc(GL.SRC_ALPHA, GL.ONE); break;
                case ParticleBlend.Multiply: GL.BlendFunc(GL.DST_COLOR, GL.ZERO); break;
                default: GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); break;
            }
            _shader.Use();
            _shader.SetInt("uLit", lit ? 1 : 0);
            _shader.SetInt("uMultiply", blend == ParticleBlend.Multiply ? 1 : 0);
            tex.Bind(0);
            _shader.SetInt("uTex", 0);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            fixed (float* p = _verts) GL.BufferSubData(GL.ARRAY_BUFFER, 0, _quadCount * 4 * FloatsPerVertex * sizeof(float), p);
            GL.DrawElements(GL.TRIANGLES, _quadCount * 6, GL.UNSIGNED_INT, 0);
            DrawCalls++;
            QuadsDrawn += _quadCount;
            _quadCount = 0;
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
        }
    }
}
