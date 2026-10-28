using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.PostFx;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.Lighting
{
    /// <summary>
    /// Directional-light shadow map: an orthographic depth render of the casters inside a box of
    /// half-size <c>shadow_distance</c> around the camera. The box origin is snapped to shadow-map
    /// texels so the shadows do not shimmer when the camera moves.
    /// </summary>
    public sealed class ShadowMap : IDisposable
    {
        private RenderTarget _target;
        private readonly Shader _meshDepth, _spriteDepth;
        private uint _quadVao, _quadVbo;
        private readonly float[] _quad = new float[4 * 5];

        public Matrix4x4 ViewProj { get; private set; }
        /// <summary>Bias matrix * ViewProj: world → shadow uv (xy) and depth (z) in [0,1].</summary>
        public Matrix4x4 ShadowMatrix { get; private set; }
        public int Resolution => _target?.Width ?? 0;
        public bool Ready => _target != null;

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

        /// <summary>Renders the shadow map for this frame.</summary>
        public unsafe void Render(LightingSettings cfg, Vector3 sunDir, Camera cam, List<MeshRenderer> meshes, List<SpriteRenderer> sprites)
        {
            if (_target == null || _target.Width != cfg.shadowResolution)
            {
                _target?.Dispose();
                _target = new RenderTarget(cfg.shadowResolution, cfg.shadowResolution, false, true, linear: false, depthOnly: true);
            }

            // ---- light view: orthographic box centred a bit ahead of the camera
            float r = cfg.shadowDistance;
            Vector3 center = cam.transform.position + cam.transform.forward * (r * 0.4f);
            Quaternion lightRot = Quaternion.LookRotation(sunDir, MathF.Abs(sunDir.y) > 0.99f ? Vector3.forward : Vector3.up);
            Matrix4x4 lightWorld = Matrix4x4.TRS(Vector3.zero, lightRot, Vector3.one);
            Matrix4x4 lightView = lightWorld.inverse;
            // snap the centre to texel increments in light space
            float texel = 2f * r / cfg.shadowResolution;
            Vector3 c = lightView.MultiplyPoint3x4(center);
            c.x = MathF.Floor(c.x / texel) * texel;
            c.y = MathF.Floor(c.y / texel) * texel;
            float depthRange = r * 4f;
            Vector3 eye = lightWorld.MultiplyPoint3x4(new Vector3(c.x, c.y, c.z - depthRange * 0.5f));
            Matrix4x4 view = Matrix4x4.TRS(eye, lightRot, Vector3.one).inverse;
            Matrix4x4 proj = Matrix4x4.Ortho(-r, r, -r, r, 0f, depthRange);
            ViewProj = proj * view;
            var bias = Matrix4x4.identity;
            bias.m00 = 0.5f; bias.m11 = 0.5f; bias.m22 = 0.5f; bias.m03 = 0.5f; bias.m13 = 0.5f; bias.m23 = 0.5f;
            ShadowMatrix = bias * ViewProj;

            // ---- render casters
            _target.Bind();
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
            foreach (var mr in meshes)
            {
                if (mr.mesh == null || !mr.castsShadow) continue;
                _meshDepth.SetMatrix("uModel", mr.transform.localToWorldMatrix);
                mr.mesh.Draw();
            }

            if (cfg.spriteShadows)
            {
                _spriteDepth.Use();
                _spriteDepth.SetMatrix("uViewProj", ViewProj);
                _spriteDepth.SetInt("uTex", 0);
                GL.ActiveTexture(GL.TEXTURE0);
                foreach (var sr in sprites)
                {
                    if (sr.sprite == null || !sr.castsShadow) continue;
                    DrawSpriteQuad(sr, cam);
                }
            }
            GL.Disable(GL.POLYGON_OFFSET_FILL);
            GL.CullFace(GL.BACK);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
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
            _target?.Dispose();
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
