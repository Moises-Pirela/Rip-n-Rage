using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.PostFx;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.Lighting
{
    /// <summary>
    /// Directional-light shadow map: an orthographic depth render of the casters inside a box of
    /// half-size <c>shadow_distance</c> around the camera.
    ///
    /// Performance design: the box is only re-centred when the camera has drifted a good fraction of the
    /// box away from the last centre (snapped to texels, so there is no shimmer), and the <b>static</b>
    /// casters (every mesh whose transform did not change) are rendered into a cached depth map only when
    /// that happens or a mesh moves. Every frame the cached depth is blitted into the live map and only the
    /// dynamic casters (billboard sprites: enemies, pickups) are drawn on top. Casters whose bounds lie
    /// outside the light box are skipped. The live map is sampled with hardware depth comparison
    /// (<c>sampler2DShadow</c>), so one fetch already gives bilinear PCF.
    /// </summary>
    public sealed class ShadowMap : IDisposable
    {
        private RenderTarget _target;       // live map: static blit + dynamic casters, compare-mode sampled
        private RenderTarget _static;       // cached static casters
        private readonly Shader _meshDepth, _spriteDepth;
        private uint _quadVao, _quadVbo;
        private readonly float[] _quad = new float[4 * 5];

        // cache state
        private bool _hasCenter;
        private Vector3 _centerLight;       // snapped box centre in light space (x, y) and the camera depth (z)
        private Vector3 _lastSunDir;
        private float _lastDistance;
        private ulong _staticHash;
        private bool _staticDirty = true;

        public Matrix4x4 ViewProj { get; private set; }
        /// <summary>Bias matrix * ViewProj: world → shadow uv (xy) and depth (z) in [0,1].</summary>
        public Matrix4x4 ShadowMatrix { get; private set; }
        public int Resolution => _target?.Width ?? 0;
        public bool Ready => _target != null;
        /// <summary>How many times the static casters were (re)rendered (debug overlay).</summary>
        public int StaticRenders { get; private set; }
        /// <summary>Casters skipped / drawn in the last static render and the last dynamic pass.</summary>
        public int StaticCasters { get; private set; }
        public int StaticCulled { get; private set; }
        public int DynamicCasters { get; private set; }

        public ShadowMap()
        {
            _meshDepth = new Shader(MeshDepthVert, DepthFrag, "shadow.mesh");
            _spriteDepth = new Shader(SpriteDepthVert, SpriteDepthFrag, "shadow.sprite");
            _quadVao = GL.GenVertexArray();
            _quadVbo = GL.GenBuffer();
            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _quadVbo);
            unsafe { GL.BufferData(GL.ARRAY_BUFFER, _quad.Length * sizeof(float), null, GL.DYNAMIC_DRAW); }
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.BindVertexArray(0);
        }

        public void BindTexture(int unit)
        {
            if (_target != null) _target.BindDepth(unit);
        }

        /// <summary>Forces the static casters to be re-rendered on the next frame (level loaded, geometry edited).</summary>
        public void Invalidate() { _staticDirty = true; _hasCenter = false; }

        /// <summary>Renders (or refreshes) the shadow map for this frame.</summary>
        public unsafe void Render(LightingSettings cfg, Vector3 sunDir, Camera cam, List<MeshRenderer> meshes, List<SpriteRenderer> sprites)
        {
            if (_target == null || _target.Width != cfg.shadowResolution)
            {
                _target?.Dispose(); _static?.Dispose();
                _target = new RenderTarget(cfg.shadowResolution, cfg.shadowResolution, false, true, linear: false, depthOnly: true);
                _target.SetDepthCompare(true);
                _static = new RenderTarget(cfg.shadowResolution, cfg.shadowResolution, false, true, linear: false, depthOnly: true);
                _staticDirty = true; _hasCenter = false;
            }

            // ---- light view: orthographic box around the camera, re-centred only when the camera drifts
            float r = cfg.shadowDistance;
            Quaternion lightRot = Quaternion.LookRotation(sunDir, MathF.Abs(sunDir.y) > 0.99f ? Vector3.forward : Vector3.up);
            Matrix4x4 lightWorld = Matrix4x4.TRS(Vector3.zero, lightRot, Vector3.one);
            Matrix4x4 lightView = lightWorld.inverse;
            float texel = 2f * r / cfg.shadowResolution;
            // centred on the camera position only (not ahead of it): turning around must not move the box
            Vector3 wanted = lightView.MultiplyPoint3x4(cam.transform.position);
            bool lightChanged = !_hasCenter || (sunDir - _lastSunDir).sqrMagnitude > 1E-08f || MathF.Abs(_lastDistance - r) > 1E-04f;
            float recenter = r * Mathf.Clamp(cfg.shadowRecenterFraction, 0.02f, 0.9f);
            if (lightChanged || MathF.Abs(wanted.x - _centerLight.x) > recenter || MathF.Abs(wanted.y - _centerLight.y) > recenter || MathF.Abs(wanted.z - _centerLight.z) > r)
            {
                // snap the centre to texel increments in light space (no shimmer when it moves)
                _centerLight = new Vector3(MathF.Floor(wanted.x / texel) * texel, MathF.Floor(wanted.y / texel) * texel, wanted.z);
                _lastSunDir = sunDir; _lastDistance = r; _hasCenter = true;
                _staticDirty = true;
            }
            Vector3 c = _centerLight;
            float depthRange = r * 4f;
            Vector3 eye = lightWorld.MultiplyPoint3x4(new Vector3(c.x, c.y, c.z - depthRange * 0.5f));
            Matrix4x4 view = Matrix4x4.TRS(eye, lightRot, Vector3.one).inverse;
            Matrix4x4 proj = Matrix4x4.Ortho(-r, r, -r, r, 0f, depthRange);
            ViewProj = proj * view;
            var bias = Matrix4x4.identity;
            bias.m00 = 0.5f; bias.m11 = 0.5f; bias.m22 = 0.5f; bias.m03 = 0.5f; bias.m13 = 0.5f; bias.m23 = 0.5f;
            ShadowMatrix = bias * ViewProj;

            // ---- static casters: only when something changed (camera drifted, a mesh moved / appeared)
            ulong hash = StaticHash(meshes);
            if (hash != _staticHash) { _staticHash = hash; _staticDirty = true; }
            if (_staticDirty || !cfg.shadowCache)
            {
                _staticDirty = false;
                StaticRenders++;
                RenderStatic(view, meshes, r);
            }

            // ---- live map = cached static depth + dynamic casters
            _static.BlitDepthTo(_target);
            _target.Bind();
            GL.Enable(GL.DEPTH_TEST);
            GL.DepthFunc(GL.LEQUAL);
            GL.DepthMask(true);
            GL.Disable(GL.BLEND);
            GL.Enable(GL.POLYGON_OFFSET_FILL);
            GL.PolygonOffset(2.5f, 4f);
            GL.Disable(GL.CULL_FACE);
            DynamicCasters = 0;
            if (cfg.spriteShadows)
            {
                _spriteDepth.Use();
                _spriteDepth.SetMatrix("uViewProj", ViewProj);
                _spriteDepth.SetInt("uTex", 0);
                GL.ActiveTexture(GL.TEXTURE0);
                float r2 = r * 1.1f;
                foreach (var sr in sprites)
                {
                    if (sr.sprite == null || !sr.castsShadow) continue;
                    // cheap reject: sprite centre outside the light box
                    Vector3 lp = lightView.MultiplyPoint3x4(sr.transform.position);
                    if (MathF.Abs(lp.x - c.x) > r2 || MathF.Abs(lp.y - c.y) > r2) continue;
                    DrawSpriteQuad(sr, cam);
                    DynamicCasters++;
                }
            }
            GL.Disable(GL.POLYGON_OFFSET_FILL);
            GL.CullFace(GL.BACK);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
        }

        /// <summary>Depth render of every shadow-casting mesh whose bounds touch the light box.</summary>
        private void RenderStatic(in Matrix4x4 lightViewForBox, List<MeshRenderer> meshes, float r)
        {
            _static.Bind();
            GL.Enable(GL.DEPTH_TEST);
            GL.DepthFunc(GL.LEQUAL);
            GL.DepthMask(true);
            GL.Disable(GL.BLEND);
            GL.Clear(GL.DEPTH_BUFFER_BIT);
            // No face culling: casters that sit on their receivers (crates on the dock) must store their
            // sun-facing surfaces; acne is handled by slope-scaled polygon offset + normal offset in the shaders.
            GL.Enable(GL.POLYGON_OFFSET_FILL);
            GL.PolygonOffset(2.5f, 4f);
            GL.Disable(GL.CULL_FACE);

            _meshDepth.Use();
            _meshDepth.SetMatrix("uViewProj", ViewProj);
            StaticCasters = 0; StaticCulled = 0;
            // the box in the space of the depth render: x,y in [-r, r]
            Matrix4x4 boxView = lightViewForBox;
            foreach (var mr in meshes)
            {
                if (mr.mesh == null || !mr.castsShadow) continue;
                if (!IntersectsLightBox(mr, boxView, r)) { StaticCulled++; continue; }
                _meshDepth.SetMatrix("uModel", mr.transform.localToWorldMatrix);
                mr.mesh.Draw();
                StaticCasters++;
            }
            GL.Disable(GL.POLYGON_OFFSET_FILL);
        }

        /// <summary>World AABB of the renderer, transformed into the light's view space, tested against the ortho box.</summary>
        private static bool IntersectsLightBox(MeshRenderer mr, in Matrix4x4 lightView, float r)
        {
            if (!Culling.RendererBounds.GetWorldAABB(mr, out var min, out var max)) return true;
            float lx0 = float.MaxValue, lx1 = float.MinValue, ly0 = float.MaxValue, ly1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 p = lightView.MultiplyPoint3x4(corner);
                if (p.x < lx0) lx0 = p.x; if (p.x > lx1) lx1 = p.x;
                if (p.y < ly0) ly0 = p.y; if (p.y > ly1) ly1 = p.y;
            }
            return lx1 >= -r && lx0 <= r && ly1 >= -r && ly0 <= r;
        }

        /// <summary>Order-independent fingerprint of the caster set and their transforms (cheap: a few floats per mesh).</summary>
        private static ulong StaticHash(List<MeshRenderer> meshes)
        {
            static ulong F(float v) => (uint)BitConverter.SingleToInt32Bits(v);
            ulong h = 1469598103934665603UL;
            foreach (var mr in meshes)
            {
                if (mr.mesh == null || !mr.castsShadow) continue;
                var m = mr.transform.localToWorldMatrix;
                ulong k = (ulong)(uint)mr.instanceId * 0x9E3779B97F4A7C15UL;
                k ^= F(m.m03) | (F(m.m13) << 32);
                k ^= (F(m.m23) << 7) ^ (F(m.m00) << 17) ^ (F(m.m11 + m.m22 + m.m01 + m.m12) << 29);
                h += k * 0xBF58476D1CE4E5B9UL; // commutative combine: the list order may change without the scene changing
            }
            return h;
        }

        private unsafe void DrawSpriteQuad(SpriteRenderer sr, Camera cam)
        {
            Sprite sp = sr.sprite;
            sp.GetLocalCorners(out Vector2 min, out Vector2 max);
            sp.GetUVs(out float u0, out float v0, out float u1, out float v1);
            Matrix4x4 model;
            if (sr.billboard)
            {
                Vector3 toCam = cam.transform.position - sr.transform.position; toCam.y = 0f;
                Quaternion rot = toCam.sqrMagnitude > 1E-06f ? Quaternion.LookRotation(-toCam.normalized) : Quaternion.identity;
                model = Matrix4x4.TRS(sr.transform.position, rot, sr.transform.lossyScale);
            }
            else model = sr.transform.localToWorldMatrix;
            _spriteDepth.SetMatrix("uModel", model);
            GL.BindTexture(GL.TEXTURE_2D, sp.texture.Handle);
            void Set(int i, float x, float y, float u, float v) { _quad[i * 5] = x; _quad[i * 5 + 1] = y; _quad[i * 5 + 2] = 0; _quad[i * 5 + 3] = u; _quad[i * 5 + 4] = v; }
            Set(0, min.x, min.y, u0, v0); Set(1, max.x, min.y, u1, v0); Set(2, min.x, max.y, u0, v1); Set(3, max.x, max.y, u1, v1);
            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _quadVbo);
            fixed (float* p = _quad) GL.BufferSubData(GL.ARRAY_BUFFER, 0, _quad.Length * sizeof(float), p);
            GL.DrawArrays(GL.TRIANGLE_STRIP, 0, 4);
        }

        /// <summary>Debug helper: reads the shadow depth map back as 8-bit grayscale (top row first).</summary>
        public unsafe byte[] ReadDepthImage()
        {
            if (_target == null) return null;
            int n = _target.Width * _target.Height;
            var depth = new float[n];
            GL.BindFramebuffer(GL.FRAMEBUFFER, _target.Framebuffer);
            GL.PixelStorei(GL.PACK_ALIGNMENT, 1);
            fixed (float* p = depth) GL.ReadPixels(0, 0, _target.Width, _target.Height, GL.DEPTH_COMPONENT, GL.FLOAT, p);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
            var img = new byte[n * 4];
            for (int y = 0; y < _target.Height; y++)
                for (int x = 0; x < _target.Width; x++)
                {
                    float d = depth[(_target.Height - 1 - y) * _target.Width + x];
                    byte v = (byte)(Mathf.Clamp01(d) * 255f);
                    int o = (y * _target.Width + x) * 4;
                    img[o] = img[o + 1] = img[o + 2] = v; img[o + 3] = 255;
                }
            return img;
        }

        public void Dispose()
        {
            _target?.Dispose(); _static?.Dispose();
            _meshDepth.Dispose(); _spriteDepth.Dispose();
            GL.DeleteVertexArray(_quadVao); GL.DeleteBuffer(_quadVbo);
        }

        private const string MeshDepthVert = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uViewProj; uniform mat4 uModel;
void main() { gl_Position = uViewProj * uModel * vec4(aPos, 1.0); }";

        private const string DepthFrag = @"#version 330 core
void main() { }";

        private const string SpriteDepthVert = @"#version 330 core
layout(location = 0) in vec3 aPos; layout(location = 1) in vec2 aUV;
uniform mat4 uViewProj; uniform mat4 uModel; out vec2 vUV;
void main() { vUV = aUV; gl_Position = uViewProj * uModel * vec4(aPos, 1.0); }";

        private const string SpriteDepthFrag = @"#version 330 core
in vec2 vUV; uniform sampler2D uTex;
void main() { if (texture(uTex, vUV).a < 0.5) discard; }";
    }
}
