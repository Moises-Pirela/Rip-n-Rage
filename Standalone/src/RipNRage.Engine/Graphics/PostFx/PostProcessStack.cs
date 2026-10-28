using System;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.PostFx
{
    /// <summary>
    /// Post-processing chain rendered through offscreen HDR targets:
    ///
    ///   scene (colour + depth) → motion blur (camera reprojection) → depth of field (CoC from depth,
    ///   half-res blur) → [first-person sprites composited on top, sharp] → bloom (soft-knee bright
    ///   pass + iterative blur) → final composite: chromatic aberration, colour filter/exposure/
    ///   saturation/contrast, vignette → UI drawn afterwards, unaffected.
    ///
    /// All parameters come from <see cref="PostProcessSettings"/> (tunables.cfg, hot-reloaded) plus
    /// a per-frame <see cref="PostProcessVolumeOverride"/> pushed by gameplay.
    /// </summary>
    public sealed unsafe class PostProcessStack : IDisposable
    {
        public PostProcessSettings Settings { get; set; } = new PostProcessSettings();
        public PostProcessVolumeOverride Override = PostProcessVolumeOverride.None;
        /// <summary>Runtime master switch (F4 in the game); independent of the cfg.</summary>
        public bool Enabled = true;

        /// <summary>The chain runs only when enabled and something would actually change the image.</summary>
        public bool IsActive => Enabled && ((Settings.enabled && (Settings.AnyEffect || Override.vignetteIntensity > 0f || Override.chromaticIntensity > 0f || Override.bloomIntensity > 0f || !IsWhite(Override.colorFilter))) || VolumetricActive);

        private bool VolumetricActive => _lighting != null && _lighting.volumetricEnabled && _lighting.volDensity > 0f;

        // ---- scene lighting data for the volumetric pass (set by the render pipeline each frame)
        private LightingSettings _lighting;
        private ShadowMap _shadow;
        private Vector3 _sunDir, _sunColor;
        private readonly Vector3[] _lightPos = new Vector3[8];
        private readonly Vector3[] _lightColor = new Vector3[8];
        private readonly float[] _lightRange = new float[8];
        private int _lightCount;
        private RenderTarget _fogRT;
        private readonly Shader _fog, _fogComposite;
        private float _time;

        public void SetSceneLighting(LightingSettings lighting, ShadowMap shadow, Vector3 sunDir, Vector3 sunColor, Vector3[] pos, Vector3[] color, float[] range, int count)
        {
            _lighting = lighting; _shadow = shadow; _sunDir = sunDir; _sunColor = sunColor;
            _lightCount = Math.Min(count, 8);
            for (int i = 0; i < _lightCount; i++) { _lightPos[i] = pos[i]; _lightColor[i] = color[i]; _lightRange[i] = range[i]; }
        }

        private static bool IsWhite(Color c) => c.r == 1f && c.g == 1f && c.b == 1f;

        private RenderTarget _scene, _tmp, _composite, _halfA, _halfB, _bloomA, _bloomB;
        private RenderTarget _current; // target holding the processed world (+ first-person sprites)
        private bool _hdrTargets, _linearTargets;
        private readonly Shader _copy, _blur, _motionBlur, _dof, _bright, _final;
        private uint _vao, _vbo;
        private Matrix4x4 _prevViewProj;
        private bool _hasPrev;
        private int _width, _height;

        public RenderTarget SceneTarget => _scene;

        // ---- optional GPU-stage profiling (RIPNRAGE_PROFILE=1): glFinish around each stage
        private static readonly bool Profile = Environment.GetEnvironmentVariable("RIPNRAGE_PROFILE") == "1";
        private readonly System.Collections.Generic.Dictionary<string, double> _stageMs = new System.Collections.Generic.Dictionary<string, double>();
        private int _profileFrames;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        private void Mark(string stage)
        {
            if (!Profile) return;
            GL.Finish();
            double ms = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
            _stageMs[stage] = _stageMs.TryGetValue(stage, out var v) ? v + ms : ms;
        }
        public string ProfileReport()
        {
            if (!Profile || _profileFrames == 0) return "";
            var sb = new System.Text.StringBuilder("PostFx stage ms/frame:");
            foreach (var kv in _stageMs) sb.Append($" {kv.Key}={kv.Value / _profileFrames:0.0}");
            return sb.ToString();
        }

        public PostProcessStack()
        {
            _copy = new Shader(FsVert, CopyFrag, "pp.copy");
            _blur = new Shader(FsVert, BlurFrag, "pp.blur");
            _motionBlur = new Shader(FsVert, MotionBlurFrag, "pp.motionblur");
            _dof = new Shader(FsVert, DofFrag, "pp.dof");
            _bright = new Shader(FsVert, BrightFrag, "pp.bright");
            _final = new Shader(FsVert, FinalFrag, "pp.final");
            _fog = new Shader(FsVert, FogFrag, "pp.volumetric");
            _fogComposite = new Shader(FsVert, FogCompositeFrag, "pp.fogcomposite");

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            float[] tri = { -1, -1, 3, -1, -1, 3 };
            fixed (float* p = tri) GL.BufferData(GL.ARRAY_BUFFER, tri.Length * sizeof(float), p, GL.STATIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        /// <summary>(Re)creates the targets for the window size and binds the scene target for world rendering.</summary>
        public void BeginScene(int width, int height)
        {
            // the 3D scene and the 1:1 passes run at render_scale; the final pass upsamples to the window
            width = Math.Max(1, (int)(width * Settings.renderScale));
            height = Math.Max(1, (int)(height * Settings.renderScale));
            int hw = Math.Max(1, (int)(width * Settings.effectScale)), hh = Math.Max(1, (int)(height * Settings.effectScale));
            int bw = Math.Max(1, (int)(width * Settings.bloomScale)), bh = Math.Max(1, (int)(height * Settings.bloomScale));
            bool hdr = Settings.hdr;
            bool upsampling = Settings.renderScale < 0.999f;
            if (_scene != null && (_hdrTargets != hdr || _linearTargets != upsampling)) ReleaseTargets();
            if (_scene == null)
            {
                _hdrTargets = hdr; _linearTargets = upsampling;
                // full-res targets are sampled 1:1 → nearest filtering (cheap on software GL) unless the final pass upsamples
                _scene = new RenderTarget(width, height, hdr, true, linear: upsampling);
                _tmp = new RenderTarget(width, height, hdr, false, linear: upsampling);
                _composite = new RenderTarget(width, height, hdr, false, linear: upsampling);
                _halfA = new RenderTarget(hw, hh, hdr, false);
                _halfB = new RenderTarget(hw, hh, hdr, false);
                _bloomA = new RenderTarget(bw, bh, hdr, false);
                _bloomB = new RenderTarget(bw, bh, hdr, false);
            }
            else
            {
                _scene.Resize(width, height); _tmp.Resize(width, height); _composite.Resize(width, height);
                _halfA.Resize(hw, hh); _halfB.Resize(hw, hh); _bloomA.Resize(bw, bh); _bloomB.Resize(bw, bh);
            }
            _width = width; _height = height;
            _scene.Bind();
        }

        private void ReleaseTargets()
        {
            _scene?.Dispose(); _tmp?.Dispose(); _composite?.Dispose(); _halfA?.Dispose(); _halfB?.Dispose(); _bloomA?.Dispose(); _bloomB?.Dispose(); _fogRT?.Dispose();
            _scene = _tmp = _composite = _halfA = _halfB = _bloomA = _bloomB = _fogRT = null;
        }

        /// <summary>
        /// Runs the depth-based world effects (motion blur, DOF). Leaves the composite target bound so the
        /// caller can draw first-person sprites on top of the processed world.
        /// </summary>
        public void ProcessWorld(Matrix4x4 view, Matrix4x4 proj, float near, float far)
        {
            if (Profile) { _sw.Restart(); Mark("scene"); }
            SetFullscreenState();
            Matrix4x4 viewProj = proj * view;
            if (!_hasPrev) { _prevViewProj = viewProj; _hasPrev = true; }

            RenderTarget src = _scene;
            RenderTarget Next(RenderTarget cur) => cur == _tmp ? _composite : _tmp;
            _time += Time.unscaledDeltaTime;

            // ---- volumetric fog (ray march at volScale, composite at full res)
            if (VolumetricActive)
            {
                int fw = Math.Max(1, (int)(_width * _lighting.volScale)), fh = Math.Max(1, (int)(_height * _lighting.volScale));
                if (_fogRT == null) _fogRT = new RenderTarget(fw, fh, _hdrTargets, false, linear: true); else _fogRT.Resize(fw, fh);
                _fogRT.Bind();
                _fog.Use();
                _scene.BindDepth(0);
                _fog.SetInt("uDepth", 0);
                bool shadows = _lighting.volShadows && _shadow != null && _shadow.Ready;
                _fog.SetInt("uShadowsOn", shadows ? 1 : 0);
                if (shadows) { _shadow.BindTexture(1); _fog.SetInt("uShadowMap", 1); _fog.SetMatrix("uShadowMatrix", _shadow.ShadowMatrix); }
                _fog.SetMatrix("uInvViewProj", viewProj.inverse);
                _fog.SetVector("uCamPos", view.inverse.GetPosition());
                _fog.SetVector("uSunDir", _sunDir);
                _fog.SetVector("uSunColor", _sunColor);
                _fog.SetVector("uAmbient", new Vector3(_lighting.ambientSky.r, _lighting.ambientSky.g, _lighting.ambientSky.b));
                _fog.SetVector("uFogAlbedo", new Vector3(_lighting.volColor.r, _lighting.volColor.g, _lighting.volColor.b));
                _fog.SetFloat("uDensity", _lighting.volDensity);
                _fog.SetFloat("uHeight", _lighting.volHeight);
                _fog.SetFloat("uFalloff", _lighting.volHeightFalloff);
                _fog.SetFloat("uAniso", _lighting.volAnisotropy);
                _fog.SetFloat("uSunScatter", _lighting.volSunScattering);
                _fog.SetFloat("uAmbientAmount", _lighting.volAmbient);
                _fog.SetInt("uSteps", _lighting.volSteps);
                _fog.SetFloat("uMaxDist", _lighting.volMaxDistance);
                _fog.SetFloat("uDither", _lighting.volDither);
                int lc = _lighting.volPointLights ? _lightCount : 0;
                _fog.SetInt("uLightCount", lc);
                _fog.SetVector3Array("uLightPos", _lightPos, lc);
                _fog.SetVector3Array("uLightColor", _lightColor, lc);
                _fog.SetFloatArray("uLightRange", _lightRange, lc);
                DrawTriangle();

                var dst = Next(src);
                dst.Bind();
                _fogComposite.Use();
                src.BindColor(0); _fogRT.BindColor(1);
                _fogComposite.SetInt("uTex", 0); _fogComposite.SetInt("uFog", 1);
                DrawTriangle();
                src = dst;
                Mark("volumetric");
            }

            // ---- motion blur (camera motion from depth reprojection)
            if (Settings.motionBlurEnabled && Settings.motionBlurIntensity > 0.001f)
            {
                var dst = Next(src);
                dst.Bind();
                _motionBlur.Use();
                src.BindColor(0); _scene.BindDepth(1);
                _motionBlur.SetInt("uTex", 0); _motionBlur.SetInt("uDepth", 1);
                _motionBlur.SetMatrix("uInvViewProj", viewProj.inverse);
                _motionBlur.SetMatrix("uPrevViewProj", _prevViewProj);
                _motionBlur.SetFloat("uIntensity", Settings.motionBlurIntensity);
                _motionBlur.SetInt("uSamples", Settings.motionBlurSamples);
                _motionBlur.SetFloat("uMaxVel", Settings.motionBlurMaxVelocity);
                DrawTriangle();
                src = dst;
                Mark("motionblur");
            }
            _prevViewProj = viewProj;

            // ---- depth of field
            if (Settings.dofEnabled && Settings.dofMaxBlur > 0.001f)
            {
                // downsample to half res, then blur (two separable passes, radius scaled by max_blur)
                Blit(src, _halfA);
                BlurPass(_halfA, _halfB, new Vector2(1, 0), 2.5f * Settings.dofMaxBlur);
                BlurPass(_halfB, _halfA, new Vector2(0, 1), 2.5f * Settings.dofMaxBlur);

                var dst = Next(src);
                dst.Bind();
                _dof.Use();
                src.BindColor(0); _halfA.BindColor(1); _scene.BindDepth(2);
                _dof.SetInt("uSharp", 0); _dof.SetInt("uBlur", 1); _dof.SetInt("uDepth", 2);
                _dof.SetVector("uProjZ", new Vector2(proj.m22, proj.m23));
                _dof.SetFloat("uFocus", Settings.dofFocusDistance);
                _dof.SetFloat("uRange", Settings.dofFocusRange);
                _dof.SetFloat("uFarStart", Mathf.Max(0.01f, Settings.dofFarBlurStart));
                _dof.SetFloat("uMaxBlur", Settings.dofMaxBlur);
                _dof.SetInt("uNearBlur", Settings.dofNearBlur ? 1 : 0);
                _dof.SetInt("uAutofocus", Settings.dofAutofocus ? 1 : 0);
                _dof.SetFloat("uFar", far);
                DrawTriangle();
                src = dst;
                Mark("dof");
            }

            // leave the processed target bound for the first-person pass (no extra copy)
            _current = src;
            _current.Bind();
        }

        /// <summary>Bloom + final composite to the default framebuffer.</summary>
        public void Finish(int width, int height)
        {
            Mark("fpsprites");
            SetFullscreenState();
            bool lens = Settings.enabled;
            bool bloom = lens && Settings.bloomEnabled && (Settings.bloomIntensity + Override.bloomIntensity) > 0.001f;
            if (bloom)
            {
                _bloomA.Bind();
                _bright.Use();
                _current.BindColor(0);
                _bright.SetInt("uTex", 0);
                _bright.SetFloat("uThreshold", Settings.bloomThreshold);
                _bright.SetFloat("uKnee", Settings.bloomThreshold * Settings.bloomSoftKnee);
                DrawTriangle();
                for (int i = 0; i < Settings.bloomPasses; i++)
                {
                    float r = Settings.bloomRadius * (1f + i * 0.8f);
                    BlurPass(_bloomA, _bloomB, new Vector2(1, 0), r);
                    BlurPass(_bloomB, _bloomA, new Vector2(0, 1), r);
                }
                Mark("bloom");
            }

            RenderTarget.BindDefault(width, height);
            _final.Use();
            _current.BindColor(0); _bloomA.BindColor(1);
            _final.SetInt("uTex", 0); _final.SetInt("uBloom", 1);
            _final.SetFloat("uBloomIntensity", bloom ? Settings.bloomIntensity + Override.bloomIntensity : 0f);
            float chroma = lens ? (Settings.chromaticEnabled ? Settings.chromaticIntensity : 0f) + Override.chromaticIntensity : 0f;
            _final.SetFloat("uChroma", chroma);
            _final.SetInt("uChromaRadial", Settings.chromaticRadial ? 1 : 0);
            _final.SetFloat("uAspect", (float)width / height);
            _final.SetFloat("uVigIntensity", lens && Settings.vignetteEnabled ? Settings.vignetteIntensity : 0f);
            _final.SetFloat("uVigSmooth", Settings.vignetteSmoothness);
            _final.SetFloat("uVigRound", Settings.vignetteRoundness);
            _final.SetColor("uVigColor", Settings.vignetteColor);
            _final.SetFloat("uVig2Intensity", lens ? Override.vignetteIntensity : 0f);
            _final.SetColor("uVig2Color", Override.vignetteColor);
            _final.SetColor("uColorFilter", lens ? Override.colorFilter : Color.white);
            _final.SetFloat("uExposure", lens ? Settings.exposure : 1f);
            _final.SetFloat("uSaturation", lens ? Settings.saturation : 1f);
            _final.SetFloat("uContrast", lens ? Settings.contrast : 1f);
            DrawTriangle();

            GL.BindTexture(GL.TEXTURE_2D, 0);
            GL.ActiveTexture(GL.TEXTURE0);
            Override = PostProcessVolumeOverride.None;
            Mark("final");
            _profileFrames++;
        }

        /// <summary>Adds a gameplay volume contribution for this frame.</summary>
        public void AddOverride(float vignetteIntensity, Color vignetteColor, float chromatic, Color colorFilter, float bloom = 0f)
        {
            var o = Override;
            if (vignetteIntensity > o.vignetteIntensity) { o.vignetteIntensity = vignetteIntensity; o.vignetteColor = vignetteColor; }
            o.chromaticIntensity += chromatic;
            o.colorFilter = o.colorFilter * colorFilter;
            o.bloomIntensity += bloom;
            Override = o;
        }

        // ------------------------------------------------------------------ helpers

        private void SetFullscreenState()
        {
            GL.Disable(GL.DEPTH_TEST);
            GL.Disable(GL.BLEND);
            GL.Disable(GL.CULL_FACE);
            GL.DepthMask(true);
        }

        private void DrawTriangle()
        {
            GL.BindVertexArray(_vao);
            GL.DrawArrays(GL.TRIANGLES, 0, 3);
        }

        private void Blit(RenderTarget src, RenderTarget dst)
        {
            dst.Bind();
            _copy.Use();
            src.BindColor(0);
            _copy.SetInt("uTex", 0);
            DrawTriangle();
        }

        private void BlurPass(RenderTarget src, RenderTarget dst, Vector2 dir, float radius)
        {
            dst.Bind();
            _blur.Use();
            src.BindColor(0);
            _blur.SetInt("uTex", 0);
            _blur.SetVector("uTexel", new Vector2(1f / src.Width, 1f / src.Height));
            _blur.SetVector("uDir", dir);
            _blur.SetFloat("uRadius", radius);
            DrawTriangle();
        }

        public void Dispose()
        {
            ReleaseTargets();
            _copy.Dispose(); _blur.Dispose(); _motionBlur.Dispose(); _dof.Dispose(); _bright.Dispose(); _final.Dispose(); _fog.Dispose(); _fogComposite.Dispose();
            GL.DeleteVertexArray(_vao); GL.DeleteBuffer(_vbo);
        }

        // ------------------------------------------------------------------ shaders

        private const string FsVert = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUV;
void main() { vUV = aPos * 0.5 + 0.5; gl_Position = vec4(aPos, 0.0, 1.0); }";

        private const string CopyFrag = @"#version 330 core
in vec2 vUV; uniform sampler2D uTex; out vec4 FragColor;
void main() { FragColor = texture(uTex, vUV); }";

        // 9-tap gaussian using bilinear pairs (5 fetches)
        private const string BlurFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform vec2 uTexel; uniform vec2 uDir; uniform float uRadius;
out vec4 FragColor;
void main() {
    vec2 step = uDir * uTexel * uRadius;
    vec3 c = texture(uTex, vUV).rgb * 0.2270270270;
    c += texture(uTex, vUV + step * 1.3846153846).rgb * 0.3162162162;
    c += texture(uTex, vUV - step * 1.3846153846).rgb * 0.3162162162;
    c += texture(uTex, vUV + step * 3.2307692308).rgb * 0.0702702703;
    c += texture(uTex, vUV - step * 3.2307692308).rgb * 0.0702702703;
    FragColor = vec4(c, 1.0);
}";

        private const string MotionBlurFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform sampler2D uDepth;
uniform mat4 uInvViewProj; uniform mat4 uPrevViewProj;
uniform float uIntensity; uniform int uSamples; uniform float uMaxVel;
out vec4 FragColor;
void main() {
    float d = texture(uDepth, vUV).r;
    vec4 ndc = vec4(vUV * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 wp = uInvViewProj * ndc; wp /= wp.w;
    vec4 pc = uPrevViewProj * wp;
    vec2 puv = (pc.xy / pc.w) * 0.5 + 0.5;
    vec2 vel = (vUV - puv) * uIntensity;
    float len = length(vel);
    if (len > uMaxVel) vel *= uMaxVel / len;
    if (len < 0.0002) { FragColor = texture(uTex, vUV); return; }
    vec3 col = vec3(0.0);
    for (int i = 0; i < uSamples; i++) {
        float t = float(i) / float(uSamples - 1) - 0.5;
        col += texture(uTex, clamp(vUV + vel * t, vec2(0.001), vec2(0.999))).rgb;
    }
    FragColor = vec4(col / float(uSamples), 1.0);
}";

        private const string DofFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uSharp; uniform sampler2D uBlur; uniform sampler2D uDepth;
uniform vec2 uProjZ; uniform float uFocus; uniform float uRange; uniform float uFarStart; uniform float uMaxBlur;
uniform int uNearBlur; uniform int uAutofocus; uniform float uFar;
out vec4 FragColor;
float linearDepth(float d) { float ndc = d * 2.0 - 1.0; return uProjZ.y / (ndc - uProjZ.x); }
void main() {
    float z = linearDepth(texture(uDepth, vUV).r);
    float focus = uAutofocus == 1 ? linearDepth(texture(uDepth, vec2(0.5)).r) : uFocus;
    focus = min(focus, uFar * 0.5);
    float nearEdge = focus - uRange * 0.5;
    float farEdge = focus + uRange * 0.5;
    float coc = 0.0;
    if (z > farEdge) coc = clamp((z - farEdge) / uFarStart, 0.0, 1.0);
    else if (z < nearEdge && uNearBlur == 1) coc = clamp((nearEdge - z) / max(nearEdge, 0.01), 0.0, 1.0);
    coc = clamp(coc * uMaxBlur, 0.0, 1.0);
    vec3 sharp = texture(uSharp, vUV).rgb;
    vec3 blur = texture(uBlur, vUV).rgb;
    FragColor = vec4(mix(sharp, blur, coc), 1.0);
}";

        // Ray-marched height fog with sun in-scattering (Henyey-Greenstein) shadowed by the shadow map,
        // plus point-light scattering. Output: rgb = in-scattered light, a = transmittance.
        private const string FogFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uDepth; uniform sampler2D uShadowMap;
uniform mat4 uInvViewProj; uniform mat4 uShadowMatrix; uniform int uShadowsOn;
uniform vec3 uCamPos; uniform vec3 uSunDir; uniform vec3 uSunColor; uniform vec3 uAmbient;
uniform vec3 uFogAlbedo; uniform float uDensity; uniform float uHeight; uniform float uFalloff;
uniform float uAniso; uniform float uSunScatter; uniform float uAmbientAmount;
uniform int uSteps; uniform float uMaxDist; uniform float uDither;
uniform int uLightCount; uniform vec3 uLightPos[8]; uniform vec3 uLightColor[8]; uniform float uLightRange[8];
out vec4 FragColor;
float ign(vec2 p) { return fract(52.9829189 * fract(0.06711056 * p.x + 0.00583715 * p.y)); }
float hg(float c, float g) { float g2 = g * g; return (1.0 - g2) / (4.0 * 3.14159265 * pow(1.0 + g2 - 2.0 * g * c, 1.5)); }
float shadowAt(vec3 p) {
    if (uShadowsOn == 0) return 1.0;
    vec4 sc = uShadowMatrix * vec4(p, 1.0); vec3 q = sc.xyz / sc.w;
    if (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0 || q.z > 1.0) return 1.0;
    return (q.z - 0.002 <= texture(uShadowMap, q.xy).r) ? 1.0 : 0.0;
}
void main() {
    float d = texture(uDepth, vUV).r;
    vec4 ndc = vec4(vUV * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 wp = uInvViewProj * ndc; wp /= wp.w;
    vec3 ray = wp.xyz - uCamPos;
    float len = length(ray);
    vec3 dir = ray / max(len, 1e-4);
    len = min(len, uMaxDist);
    float stepLen = len / float(uSteps);
    float t = stepLen * uDither * ign(gl_FragCoord.xy);
    float T = 1.0;
    vec3 acc = vec3(0.0);
    float phase = min(hg(dot(dir, -uSunDir), uAniso) * 4.0 * 3.14159265, 6.0); // normalised so g=0 gives 1; capped so the sun disc does not blow out
    for (int i = 0; i < uSteps; i++) {
        vec3 p = uCamPos + dir * t;
        float dens = uDensity * exp(-max(p.y - uHeight, 0.0) * uFalloff);
        if (dens > 1e-6) {
            vec3 light = uSunColor * uSunScatter * phase * shadowAt(p) + uAmbient * uAmbientAmount;
            for (int l = 0; l < uLightCount; l++) {
                float dist = distance(uLightPos[l], p);
                if (dist < uLightRange[l]) { float att = 1.0 - dist / uLightRange[l]; light += uLightColor[l] * att * att * 2.0; }
            }
            float s = dens * stepLen;
            float e = exp(-s);
            acc += T * light * uFogAlbedo * (1.0 - e);
            T *= e;
        }
        t += stepLen;
    }
    acc = acc / (1.0 + acc); // soft roll-off keeps bright shafts inside the LDR target
    FragColor = vec4(acc, T);
}";

        private const string FogCompositeFrag = @"#version 330 core
in vec2 vUV; uniform sampler2D uTex; uniform sampler2D uFog; out vec4 FragColor;
void main() {
    vec4 fog = texture(uFog, vUV);
    vec3 scene = texture(uTex, vUV).rgb;
    FragColor = vec4(scene * fog.a + fog.rgb, 1.0);
}";

        private const string BrightFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform float uThreshold; uniform float uKnee;
out vec4 FragColor;
void main() {
    vec3 c = texture(uTex, vUV).rgb;
    float br = max(c.r, max(c.g, c.b));
    float soft = clamp(br - uThreshold + uKnee, 0.0, 2.0 * uKnee);
    soft = soft * soft / (4.0 * uKnee + 1e-5);
    float contrib = max(soft, br - uThreshold) / max(br, 1e-5);
    FragColor = vec4(c * contrib, 1.0);
}";

        private const string FinalFrag = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform sampler2D uBloom;
uniform float uBloomIntensity; uniform float uChroma; uniform int uChromaRadial; uniform float uAspect;
uniform float uVigIntensity; uniform float uVigSmooth; uniform float uVigRound; uniform vec4 uVigColor;
uniform float uVig2Intensity; uniform vec4 uVig2Color;
uniform vec4 uColorFilter; uniform float uExposure; uniform float uSaturation; uniform float uContrast;
out vec4 FragColor;
float vignette(vec2 uv, float smoothness, float roundness) {
    vec2 d = (uv - 0.5) * 2.0;
    d.x *= mix(1.0, uAspect, roundness);
    float dist = length(d) / mix(1.0, uAspect, roundness) * 1.2;
    return smoothstep(0.55, 0.55 + smoothness * 1.4, dist);
}
void main() {
    vec2 dir = vUV - 0.5;
    float amount = uChroma * 0.012 * (uChromaRadial == 1 ? length(dir) * 2.0 : 1.0);
    vec2 off = (uChromaRadial == 1 ? dir : vec2(0.5, 0.0)) * amount;
    vec3 col, bloom;
    col.r = texture(uTex, vUV + off).r; col.g = texture(uTex, vUV).g; col.b = texture(uTex, vUV - off).b;
    bloom.r = texture(uBloom, vUV + off).r; bloom.g = texture(uBloom, vUV).g; bloom.b = texture(uBloom, vUV - off).b;
    col += bloom * uBloomIntensity;
    col *= uColorFilter.rgb * uExposure;
    float l = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(l), col, uSaturation);
    col = (col - 0.5) * uContrast + 0.5;
    float v = vignette(vUV, uVigSmooth, uVigRound) * uVigIntensity;
    col = mix(col, uVigColor.rgb, clamp(v, 0.0, 1.0));
    float v2 = vignette(vUV, 0.6, 1.0) * uVig2Intensity;
    col = mix(col, uVig2Color.rgb, clamp(v2, 0.0, 1.0));
    FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}";
    }
}
