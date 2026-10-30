using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.Culling;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Engine.Graphics.Particles;
using RipNRage.Engine.Graphics.PostFx;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Forward renderer. Passes, in order:
    /// 0. directional shadow map (all casters, no culling),
    /// 1. sky (full-screen gradient),
    /// 2. world: frustum + software occlusion culling, opaque meshes by render queue, transparent meshes,
    ///    world sprites back-to-front, debug lines; then the world post-process stage (volumetric fog, motion blur, DOF),
    /// 3. viewmodel: first-person sprites/meshes (<see cref="RenderLayer.FirstPerson"/>) with their own projection
    ///    (<see cref="CameraSettings.viewmodelFov"/>, near/far) and a cleared depth buffer so the weapon never clips into walls,
    /// 4. lens post-processing (bloom, chromatic aberration, vignette, colour) → default framebuffer,
    /// 5. screen-space UI.
    /// </summary>
    public sealed unsafe class RenderPipeline : IDisposable
    {
        private readonly Shader _uiShader;
        private readonly Shader _skyShader;
        // shaders already set up with the per-frame uniforms (camera, time, lights, shadows)
        private readonly HashSet<Shader> _prepared = new HashSet<Shader>();
        private Matrix4x4 _viewProj;
        private Vector3 _camPos;
        private bool _fpPass;
        // viewmodel pass: per-draw projection override (PrepareShader only uploads uViewProj once per shader per frame)
        private bool _overrideViewProj;
        private Matrix4x4 _passViewProj;
        private Vector3 _viewmodelNormal;
        private const int MaxSpriteVerts = 1024;
        private uint _quadVao, _quadVbo, _quadEbo;
        private uint _skyVao, _skyVbo;
        private readonly float[] _quadData = new float[MaxSpriteVerts * 5];
        private readonly uint[] _quadIndices = new uint[MaxSpriteVerts * 3];
        private readonly List<SpriteRenderer> _worldSprites = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _fpSprites = new List<SpriteRenderer>();
        private readonly List<MeshRenderer> _meshes = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _fpMeshes = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _visibleMeshes = new List<MeshRenderer>();
        private readonly List<SpriteRenderer> _visibleSprites = new List<SpriteRenderer>();
        private readonly List<IUiDrawer> _uiDrawers = new List<IUiDrawer>();
        private readonly List<SpriteRenderer> _casters = new List<SpriteRenderer>();

        public UiBatch Ui { get; }
        public int DrawCalls { get; private set; }
        public bool DrawUi = true;
        /// <summary>Post-processing chain (bloom, chromatic aberration, vignette, motion blur, volumetric fog).</summary>
        public PostProcessStack PostFx { get; }
        /// <summary>Sun / ambient / fog / shadow tunables (shared with tunables.cfg when present).</summary>
        public LightingSettings Lighting { get; set; } = new LightingSettings();
        /// <summary>[camera] / [viewmodel] / [culling] tunables.</summary>
        public CameraSettings CameraConfig { get; set; } = new CameraSettings();
        public ShadowMap Shadows { get; }
        /// <summary>Batched renderer for the data-driven particle effects (<see cref="ParticleWorld"/>).</summary>
        public ParticleRenderer Particles { get; }
        /// <summary>Software occlusion buffer (legacy CPU path, <c>[culling] occlusion = cpu</c>).</summary>
        public OcclusionBuffer Occlusion { get; }
        /// <summary>Hardware occlusion queries (default path, <c>[culling] occlusion = gpu</c>).</summary>
        public GpuOcclusion GpuOcclusion { get; }
        private readonly List<MeshRenderer> _occlusionCandidates = new List<MeshRenderer>();
        private bool _gpuOcclusionThisFrame;
        /// <summary>Slack added to every renderer AABB before culling (covers shader vertex animation and sprite wobble).</summary>
        public static readonly Vector3 BoundsPadding = new Vector3(0.3f, 0.3f, 0.3f);
        /// <summary>Counters from the last frame's culling.</summary>
        public CullingStats Culling { get; } = new CullingStats();
        /// <summary>Projection used for the viewmodel pass in the last frame (for scripts that need to project viewmodel points).</summary>
        public Matrix4x4 ViewmodelProjection { get; private set; } = Matrix4x4.identity;

        // per-frame light data
        private readonly Vector3[] _lightPos = new Vector3[16];
        private readonly Vector3[] _lightColor = new Vector3[16];
        private readonly float[] _lightRange = new float[16];
        private int _lightCount;
        private Vector3 _sunDir;
        private Vector3 _sunColor;
        private bool _shadowsThisFrame;
        // per-draw point-light culling: only the lights whose range touches the renderer are uploaded
        private readonly Vector3[] _dlPos = new Vector3[16];
        private readonly Vector3[] _dlColor = new Vector3[16];
        private readonly float[] _dlRange = new float[16];
        private Shader _dlShader;
        private int _dlMask = -1;
        /// <summary>Per-draw light set uploads in the last frame (debug overlay).</summary>
        public int LightUploads { get; private set; }

        public RenderPipeline()
        {
            _uiShader = ShaderLibrary.Get("ui");
            _skyShader = ShaderLibrary.Get("sky");
            ShaderLibrary.Get("mesh");
            ShaderLibrary.Get("sprite");
            Ui = new UiBatch(_uiShader);
            PostFx = new PostProcessStack();
            Shadows = new ShadowMap();
            Occlusion = new OcclusionBuffer();
            GpuOcclusion = GpuOcclusion.Available ? new GpuOcclusion() : null;
            Particles = new ParticleRenderer();

            _quadVao = GL.GenVertexArray();
            _quadVbo = GL.GenBuffer();
            _quadEbo = GL.GenBuffer();
            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _quadVbo);
            GL.BufferData(GL.ARRAY_BUFFER, _quadData.Length * sizeof(float), null, GL.DYNAMIC_DRAW);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _quadEbo);
            GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, _quadIndices.Length * sizeof(uint), null, GL.DYNAMIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, GL.FLOAT, false, 5 * sizeof(float), 3 * sizeof(float));

            _skyVao = GL.GenVertexArray();
            _skyVbo = GL.GenBuffer();
            GL.BindVertexArray(_skyVao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _skyVbo);
            float[] tri = { -1, -1, 3, -1, -1, 3 };
            fixed (float* p = tri) GL.BufferData(GL.ARRAY_BUFFER, tri.Length * sizeof(float), p, GL.STATIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, 2 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        public void Render(Scene scene, int width, int height)
        {
            DrawCalls = 0;
            LightUploads = 0;
            _dlShader = null; _dlMask = -1;
            var cam = Camera.main;
            if (cam == null || width == 0 || height == 0)
            {
                RenderTarget.BindDefault(width, height);
                GL.ClearColor(0, 0, 0, 1); GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
                // no camera (pure UI scenes such as a menu): still give the UI drawers their pass
                if (cam == null && width > 0 && height > 0) { Collect(scene); DrawUiPass(width, height, null); }
                GL.BindVertexArray(0);
                return;
            }

            Profiler.GpuFrameBegin();
            Profiler.Begin("collect");
            Collect(scene);
            ApplyLightingSettings();
            GatherLights(cam);
            Profiler.End();
            var cfg = CameraConfig ?? new CameraSettings();
            if (cfg.fov > 0f) cam.fieldOfView = cfg.fov;
            if (cfg.near > 0f) cam.nearClipPlane = cfg.near;
            if (cfg.far > 0f) cam.farClipPlane = cfg.far;

            // ---- pass 0: directional shadow map (every caster — shadows from off-screen geometry still matter)
            _shadowsThisFrame = Lighting.shadowsEnabled;
            if (_shadowsThisFrame)
            {
                Profiler.Begin("shadow map", gpu: true);
                _casters.Clear();
                foreach (var sr in _worldSprites) if (sr.lit) _casters.Add(sr);
                Shadows.Render(Lighting, _sunDir, cam, _meshes, _casters);
                DrawCalls++;
                Profiler.End();
            }

            float aspect = (float)width / height;
            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 proj = cam.ProjectionMatrix(aspect);
            Matrix4x4 viewProj = proj * view;
            Vector3 camPos = cam.transform.position;
            _viewProj = viewProj; _camPos = camPos;
            _prepared.Clear();
            _fpPass = false; _overrideViewProj = false;

            // ---- visibility: frustum + occlusion culling of world renderers and point lights
            Profiler.Begin("culling");
            CullWorld(cam, view, proj, viewProj, cfg);
            Profiler.End();

            PostFx.SetSceneLighting(Lighting, _shadowsThisFrame ? Shadows : null, _sunDir, _sunColor, _lightPos, _lightColor, _lightRange, _lightCount);
            bool post = PostFx.IsActive;
            if (post) PostFx.BeginScene(width, height); else RenderTarget.BindDefault(width, height);
            Color bg = cam.backgroundColor;
            GL.ClearColor(bg.r, bg.g, bg.b, 1f);
            GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);

            // ---- pass 1: sky
            Profiler.Begin("sky", gpu: true);
            if (RenderSettings.drawSky) RenderSky(cam, proj);
            Profiler.End();

            // ---- pass 2: world
            Profiler.Begin("world", gpu: true);
            RenderWorld(cam, view, viewProj);
            Profiler.End();

            // ---- post-processing (world effects) — leaves the composite target bound
            if (post)
            {
                Profiler.Begin("postfx world (fog/motion blur)", gpu: true);
                PostFx.ProcessWorld(view, proj, cam.nearClipPlane, cam.farClipPlane);
                GL.ActiveTexture(GL.TEXTURE0);
                Profiler.End();
            }

            // ---- pass 3: viewmodel (first-person weapon / hands) with its own FOV and depth range
            Profiler.Begin("viewmodel", gpu: true);
            if (cfg.viewmodelEnabled) RenderViewmodel(cam, view, aspect, cfg);
            Profiler.End();

            // ---- post-processing (lens effects) → default framebuffer
            if (post)
            {
                Profiler.Begin("postfx final (bloom/lens)", gpu: true);
                PostFx.Finish(width, height); DrawCalls += 8;
                Profiler.End();
            }

            // ---- pass 4: UI
            Profiler.Begin("ui", gpu: true);
            DrawUiPass(width, height, cfg);
            Profiler.End();
            Profiler.GpuFrameEnd();
            GL.BindVertexArray(0);
        }

        /// <summary>Screen-space UI: every <see cref="IUiDrawer"/> in <see cref="IUiLayer"/> order (stable), then the debug views.</summary>
        private void DrawUiPass(int width, int height, CameraSettings cfg)
        {
            if (!DrawUi) return;
            GL.Disable(GL.DEPTH_TEST);
            GL.Disable(GL.CULL_FACE); // UI quads are wound CCW; a mesh material may have left back-face culling on
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            Ui.Begin(width, height);
            SortUiDrawers();
            foreach (var d in _uiDrawers)
            {
                try { d.DrawUi(Ui); }
                catch (Exception e) { Debug.LogError($"DrawUi threw: {e}"); }
            }
            if (cfg != null && cfg.occlusionDebugView && cfg.occlusionMode == OcclusionMode.Cpu) DrawOcclusionDebug(cfg);
            Ui.Flush();
            GL.Disable(GL.SCISSOR_TEST); // in case a drawer left a clip rect pushed
            DrawCalls++;
        }

        private static int UiLayerOf(IUiDrawer d) => d is IUiLayer l ? l.UiLayer : UiLayers.Default;

        /// <summary>Stable insertion sort by UI layer (the list is tiny; List.Sort is not stable).</summary>
        private void SortUiDrawers()
        {
            for (int i = 1; i < _uiDrawers.Count; i++)
            {
                var item = _uiDrawers[i];
                int layer = UiLayerOf(item);
                int j = i - 1;
                while (j >= 0 && UiLayerOf(_uiDrawers[j]) > layer) { _uiDrawers[j + 1] = _uiDrawers[j]; j--; }
                _uiDrawers[j + 1] = item;
            }
        }

        private void RenderSky(Camera cam, in Matrix4x4 proj)
        {
            GL.Disable(GL.DEPTH_TEST);
            GL.Disable(GL.BLEND);
            GL.Disable(GL.CULL_FACE);
            _skyShader.Use();
            // rotation-only view so the sky is independent of camera position
            Matrix4x4 rotView = Matrix4x4.Rotate(Quaternion.Inverse(cam.transform.rotation) * Quaternion.AngleAxis(RenderSettings.skyboxRotation, Vector3.up));
            _skyShader.SetMatrix("uInvViewProj", (proj * rotView).inverse);
            _skyShader.SetVector("uHorizon", new Vector3(RenderSettings.skyHorizon.r, RenderSettings.skyHorizon.g, RenderSettings.skyHorizon.b));
            _skyShader.SetVector("uZenith", new Vector3(RenderSettings.skyZenith.r, RenderSettings.skyZenith.g, RenderSettings.skyZenith.b));
            _skyShader.SetVector("uGround", new Vector3(RenderSettings.skyGround.r, RenderSettings.skyGround.g, RenderSettings.skyGround.b));
            _skyShader.SetVector("uSunDir", RenderSettings.sunDirection);
            _skyShader.SetFloat("uTime", Time.time);
            GL.BindVertexArray(_skyVao);
            GL.DrawArrays(GL.TRIANGLES, 0, 3);
            DrawCalls++;
        }

        /// <summary>Opaque meshes by render queue, transparent meshes, world sprites back-to-front, debug lines.</summary>
        private void RenderWorld(Camera cam, in Matrix4x4 view, in Matrix4x4 viewProj)
        {
            GL.DepthFunc(GL.LEQUAL);
            GL.FrontFace(GL.CW); // left-handed world: clockwise-from-the-front triangles face the viewer
            // opaque: by queue, then front to back so early-Z rejects the expensive lit fragments of hidden geometry
            _meshDepthCache.Clear();
            foreach (var mr in _visibleMeshes)
            {
                RendererBounds.GetWorldAABB(mr, out var bmin, out var bmax);
                _meshDepthCache[mr] = ((bmin + bmax) * 0.5f - _camPos).sqrMagnitude;
            }
            _visibleMeshes.Sort((a, b) =>
            {
                int q = a.sharedMaterial.renderQueue.CompareTo(b.sharedMaterial.renderQueue);
                return q != 0 ? q : _meshDepthCache[a].CompareTo(_meshDepthCache[b]);
            });
            foreach (var mr in _visibleMeshes)
            {
                if (mr.sharedMaterial.IsTransparent) continue;
                DrawMesh(mr);
            }
            if (_gpuOcclusionThisFrame)
            {
                // hardware occlusion: test every frustum-visible chunk's box against the depth buffer just written
                Profiler.Begin("occlusion queries", gpu: false);
                GpuOcclusion.IssueQueries(_occlusionCandidates, viewProj, _camPos, cam.nearClipPlane, Time.frameCount);
                Profiler.End();
            }
            foreach (var mr in _visibleMeshes)
            {
                if (!mr.sharedMaterial.IsTransparent) continue;
                DrawMesh(mr);
            }
            GL.DepthMask(true);

            SortByDepth(_visibleSprites, view);
            foreach (var sr in _visibleSprites) DrawSprite(sr, cam, true);

            // data-driven particle effects: batched, depth tested, no depth writes
            Profiler.Begin("particles draw");
            Particles.Render(cam, view, viewProj, (CameraConfig ?? new CameraSettings()).frustumCulling, PrepareShader);
            DrawCalls += Particles.DrawCalls;
            Profiler.End();

            DebugDraw.Render(viewProj);
        }

        /// <summary>
        /// First-person layer: same view as the world camera but its own projection (FOV / near / far), drawn on a
        /// cleared depth buffer after the world post-process stage so motion blur/fog never smear the weapon.
        /// Sprites sort by sortingOrder then depth; FirstPerson meshes are drawn first (depth tested among themselves).
        /// </summary>
        private void RenderViewmodel(Camera cam, in Matrix4x4 view, float aspect, CameraSettings cfg)
        {
            if (_fpSprites.Count == 0 && _fpMeshes.Count == 0) return;
            GL.Clear(GL.DEPTH_BUFFER_BIT);
            Matrix4x4 vmProj = Matrix4x4.Perspective(cfg.viewmodelFov, aspect, cfg.viewmodelNear, cfg.viewmodelFar);
            ViewmodelProjection = vmProj;
            _passViewProj = vmProj * view;
            _overrideViewProj = true;
            _fpPass = true;
            // lit viewmodel: light it as a surface facing the camera (flattened so the sun/lights read like on a billboard)
            Vector3 n = -cam.transform.forward; n.y *= 0.35f;
            _viewmodelNormal = n.sqrMagnitude > 1E-06f ? n.normalized : Vector3.back;

            GL.DepthFunc(GL.LEQUAL);
            GL.FrontFace(GL.CW);
            foreach (var mr in _fpMeshes) DrawMesh(mr);
            GL.DepthMask(true);

            SortByDepth(_fpSprites, view);
            _fpSprites.Sort((a, b) => a.sortingOrder != b.sortingOrder ? a.sortingOrder.CompareTo(b.sortingOrder) : 0);
            foreach (var sr in _fpSprites) DrawSprite(sr, cam, cfg.viewmodelLit);
            _fpPass = false;
            _overrideViewProj = false;
            GL.DepthMask(true);
        }

        /// <summary>Frustum + occlusion tests for world meshes, sprites and point lights (shadow casters are never culled).</summary>
        private void CullWorld(Camera cam, in Matrix4x4 view, in Matrix4x4 proj, in Matrix4x4 viewProj, CameraSettings cfg)
        {
            var st = Culling;
            st.Reset();
            _visibleMeshes.Clear(); _visibleSprites.Clear(); _occlusionCandidates.Clear();
            bool frustumOn = cfg.frustumCulling;
            bool gpuOcc = cfg.occlusionMode == OcclusionMode.Gpu && GpuOcclusion != null;
            bool occlusionOn = cfg.occlusionMode == OcclusionMode.Cpu; // the CPU rasterizer path
            _gpuOcclusionThisFrame = gpuOcc;
            int frame = Time.frameCount;
            Frustum frustum = frustumOn ? Frustum.FromViewProj(viewProj) : default;

            if (occlusionOn)
            {
                Occlusion.Begin(cfg.occlusionWidth, cfg.occlusionHeight, view, proj, cam.nearClipPlane);
                foreach (var mr in _meshes)
                {
                    if (!mr.occluder || mr.mesh == null || mr.sharedMaterial.IsTransparent) continue;
                    if (frustumOn && RendererBounds.GetWorldAABB(mr, out var omin, out var omax) && !frustum.IntersectsAABB(omin, omax)) continue;
                    Occlusion.AddOccluder(mr.mesh, mr.transform.localToWorldMatrix);
                }
                st.occluderTriangles = Occlusion.TrianglesRasterised;
            }

            foreach (var mr in _meshes)
            {
                if (mr.mesh == null) continue;
                st.meshes++;
                if (frustumOn || occlusionOn)
                {
                    RendererBounds.GetWorldAABB(mr, out var min, out var max);
                    min -= BoundsPadding; max += BoundsPadding; // room for shader vertex animation (water waves)
                    if (frustumOn && !frustum.IntersectsAABB(min, max)) { st.meshesFrustumCulled++; continue; }
                    // occluders themselves are tested too (a chunk entirely behind another wall still gets skipped)
                    if (occlusionOn && !Occlusion.IsVisible(min, max)) { st.meshesOccluded++; continue; }
                }
                if (gpuOcc)
                {
                    // the result of last frame's box query decides; the query for this frame is issued after the opaque pass
                    _occlusionCandidates.Add(mr);
                    if (!GpuOcclusion.IsVisible(mr, frame)) { st.meshesOccluded++; continue; }
                }
                _visibleMeshes.Add(mr);
            }
            foreach (var sr in _worldSprites)
            {
                st.sprites++;
                if (frustumOn || occlusionOn)
                {
                    RendererBounds.GetWorldAABB(sr, out var min, out var max);
                    min -= BoundsPadding; max += BoundsPadding;
                    if (frustumOn && !frustum.IntersectsAABB(min, max)) { st.spritesFrustumCulled++; continue; }
                    if (occlusionOn && !Occlusion.IsVisible(min, max)) { st.spritesOccluded++; continue; }
                }
                _visibleSprites.Add(sr);
            }
            // point lights: drop the ones whose whole sphere of influence is off screen or hidden
            st.lights = _lightCount;
            if (cfg.cullLights && (frustumOn || occlusionOn))
            {
                int kept = 0;
                for (int i = 0; i < _lightCount; i++)
                {
                    Vector3 p = _lightPos[i]; float r = _lightRange[i];
                    bool visible = !(frustumOn && !frustum.IntersectsSphere(p, r));
                    if (visible && occlusionOn) visible = Occlusion.IsVisible(p, r);
                    if (!visible) { st.lightsCulled++; continue; }
                    _lightPos[kept] = p; _lightColor[kept] = _lightColor[i]; _lightRange[kept] = r; kept++;
                }
                _lightCount = kept;
            }
            if (occlusionOn) { Occlusion.Pause(); st.occlusionMs = Occlusion.ElapsedMs; }
        }

        private void DrawOcclusionDebug(CameraSettings cfg)
        {
            var tex = Occlusion.GetDebugTexture();
            float w = Mathf.Min(Ui.Width * 0.3f, 420f * Ui.Scale * 2f);
            float h = w * Occlusion.Height / Mathf.Max(1, Occlusion.Width);
            var r = new Rect(Ui.Width - w - 12f, 12f, w, h);
            Ui.FillRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.8f));
            Ui.DrawRaw(tex, r, 0, 0, 1, 1, Color.white);
            Ui.DrawText($"OCCLUSION {Occlusion.Width}X{Occlusion.Height}  {Culling.occluderTriangles} TRIS {Culling.occlusionMs:0.0}MS  CULLED {Culling.Culled}/{Culling.Total}", new Vector2(r.x, r.y + r.height + 10f * Ui.Scale * 2f), Mathf.Max(1f, Ui.Scale * 2f), new Color(1f, 0.9f, 0.5f, 1f));
        }

        /// <summary>Pushes the config into the legacy RenderSettings (sky shader, sun) every frame.</summary>
        private void ApplyLightingSettings()
        {
            RenderSettings.skyZenith = Lighting.skyZenith;
            RenderSettings.skyHorizon = Lighting.skyHorizon;
            RenderSettings.skyGround = Lighting.skyGround;
            RenderSettings.fogColor = Lighting.fogColor;
            RenderSettings.fogStartDistance = Lighting.fogStart;
            RenderSettings.fogEndDistance = Lighting.fogEnabled ? Lighting.fogEnd : 0f;
            _sunDir = Lighting.SunDirection;
            _sunColor = new Vector3(Lighting.sunColor.r, Lighting.sunColor.g, Lighting.sunColor.b) * Lighting.sunIntensity;
        }

        /// <summary>Directional Light components override the sun; the nearest point lights fill the light arrays.</summary>
        private void GatherLights(Camera cam)
        {
            _lightCount = 0;
            Vector3 camPos = cam.transform.position;
            var points = new List<Light>();
            foreach (var l in Light.Lights)
            {
                if (l.destroyed || !l.isActiveAndEnabled) continue;
                if (l.type == LightType.Directional)
                {
                    _sunDir = l.transform.forward.normalized;
                    _sunColor = new Vector3(l.color.r, l.color.g, l.color.b) * l.intensity;
                }
                else if (l.intensity > 0f && l.range > 0f) points.Add(l);
            }
            points.Sort((a, b) => (a.transform.position - camPos).sqrMagnitude.CompareTo((b.transform.position - camPos).sqrMagnitude));
            int max = Mathf.Min(Lighting.maxPointLights, 16);
            foreach (var l in points)
            {
                if (_lightCount >= max) break;
                _lightPos[_lightCount] = l.transform.position;
                _lightColor[_lightCount] = new Vector3(l.color.r, l.color.g, l.color.b) * l.intensity;
                _lightRange[_lightCount] = l.range;
                _lightCount++;
            }
            RenderSettings.sunDirection = _sunDir;
        }

        private void ApplyLightingUniforms(Shader sh, Vector3 camPos)
        {
            sh.SetVector("uSunDir", _sunDir);
            sh.SetVector("uSunColor", _sunColor);
            sh.SetVector("uAmbientSky", new Vector3(Lighting.ambientSky.r, Lighting.ambientSky.g, Lighting.ambientSky.b));
            sh.SetVector("uAmbientGround", new Vector3(Lighting.ambientGround.r, Lighting.ambientGround.g, Lighting.ambientGround.b));
            sh.SetInt("uLightCount", _lightCount);
            sh.SetVector3Array("uLightPos", _lightPos, _lightCount);
            sh.SetVector3Array("uLightColor", _lightColor, _lightCount);
            sh.SetFloatArray("uLightRange", _lightRange, _lightCount);
            sh.SetFloat("uSpecular", Lighting.specular);
            sh.SetVector("uCameraPos", camPos);
            sh.SetVector("uFogColor", new Vector3(Lighting.fogColor.r, Lighting.fogColor.g, Lighting.fogColor.b));
            sh.SetVector("uFogRange", new Vector2(Lighting.fogStart, Lighting.fogEnabled ? Lighting.fogEnd : 0f));
            sh.SetInt("uShadowsOn", _shadowsThisFrame ? 1 : 0);
            if (_shadowsThisFrame)
            {
                Shadows.BindTexture(3);
                sh.SetInt("uShadowMap", 3);
                sh.SetMatrix("uShadowMatrix", Shadows.ShadowMatrix);
                sh.SetFloat("uShadowBias", Lighting.shadowBias);
                sh.SetFloat("uShadowNormalOffset", Lighting.shadowNormalOffset);
                sh.SetFloat("uShadowSoftness", Lighting.shadowSoftness);
                sh.SetFloat("uShadowTexel", 1f / Shadows.Resolution);
            }
            GL.ActiveTexture(GL.TEXTURE0);
        }

        /// <summary>Binds a shader and, once per frame per shader, uploads the camera/time/lighting uniforms.</summary>
        private void PrepareShader(Shader sh)
        {
            sh.Use();
            bool fresh = _prepared.Add(sh);
            // the viewmodel pass (last world-space pass of the frame) uses its own projection: upload it per draw
            if (_overrideViewProj) sh.SetMatrix("uViewProj", _passViewProj);
            if (!fresh) return;
            if (!_overrideViewProj) sh.SetMatrix("uViewProj", _viewProj);
            sh.SetFloat("uTime", Time.time);
            sh.SetVector4("uTimeVec", Time.time, Mathf.Sin(Time.time), Mathf.Cos(Time.time), Time.deltaTime);
            sh.SetInt("uTex", 0);
            ApplyLightingUniforms(sh, _camPos);
            if (sh == _dlShader) _dlMask = -1; // the global light list was just re-uploaded: forget the per-draw subset
            sh.SetFloat("uWrap", Lighting.spriteWrap);
            sh.SetInt("uDebugShadow", DebugShadowView ? 1 : 0);
        }

        /// <summary>RIPNRAGE_DEBUG_SHADOW=2: meshes show their shadow factor only.</summary>
        public static readonly bool DebugShadowView = Environment.GetEnvironmentVariable("RIPNRAGE_DEBUG_SHADOW") == "2";

        private void DrawMesh(MeshRenderer mr)
        {
            var mat = mr.sharedMaterial;
            PrepareShader(mat.shader);
            mat.ApplyState();
            var sh = mat.shader;
            sh.SetMatrix("uModel", mr.transform.localToWorldMatrix);
            if (_lightCount > 0 && Lighting.lightsPerDraw > 0 && RendererBounds.GetWorldAABB(mr, out var lmin, out var lmax)) SetDrawLights(sh, lmin, lmax);
            // legacy per-renderer fields act as defaults; material and property block override them
            sh.SetColor("uTint", mr.color);
            sh.SetVector("uUVScale", mr.uvScale);
            sh.SetFloat("uEmissive", mr.emissive);
            mat.UploadProperties(mr.propertyBlock, 4);
            (mr.texture ?? mat.mainTexture ?? Texture2D.White).Bind(0);
            sh.SetInt("uTex", 0);
            mr.mesh.Draw();
            DrawCalls++;
        }

        /// <summary>
        /// Uploads only the point lights whose range sphere touches the renderer's world AABB (at most
        /// <see cref="LightingSettings.lightsPerDraw"/>). The surface shaders loop over uLightCount, so a level
        /// with 16 visible lights costs each pixel only the 0–3 lights that can actually reach it. Consecutive
        /// draws with the same shader and subset skip the upload.
        /// </summary>
        private void SetDrawLights(Shader sh, in Vector3 min, in Vector3 max)
        {
            int cap = Mathf.Min(Lighting.lightsPerDraw, 16);
            int mask = 0, n = 0;
            for (int i = 0; i < _lightCount && n < cap; i++)
            {
                Vector3 p = _lightPos[i]; float r = _lightRange[i];
                // squared distance from the light to the box
                float dx = p.x < min.x ? min.x - p.x : p.x > max.x ? p.x - max.x : 0f;
                float dy = p.y < min.y ? min.y - p.y : p.y > max.y ? p.y - max.y : 0f;
                float dz = p.z < min.z ? min.z - p.z : p.z > max.z ? p.z - max.z : 0f;
                if (dx * dx + dy * dy + dz * dz > r * r) continue;
                mask |= 1 << i;
                _dlPos[n] = p; _dlColor[n] = _lightColor[i]; _dlRange[n] = r; n++;
            }
            if (sh == _dlShader && mask == _dlMask) return;
            _dlShader = sh; _dlMask = mask;
            sh.SetInt("uLightCount", n);
            if (n > 0)
            {
                sh.SetVector3Array("uLightPos", _dlPos, n);
                sh.SetVector3Array("uLightColor", _dlColor, n);
                sh.SetFloatArray("uLightRange", _dlRange, n);
            }
            LightUploads++;
        }

        private void Collect(Scene scene)
        {
            _worldSprites.Clear(); _fpSprites.Clear(); _meshes.Clear(); _fpMeshes.Clear(); _uiDrawers.Clear();
            foreach (var go in scene.GameObjects)
            {
                if (go.destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.Components)
                {
                    if (c.destroyed) continue;
                    if (c is Behaviour b && !b.enabled) continue;
                    switch (c)
                    {
                        case SpriteRenderer sr when sr.sprite != null:
                            if (sr.renderLayer == RenderLayer.FirstPerson) _fpSprites.Add(sr); else _worldSprites.Add(sr);
                            break;
                        case MeshRenderer mr:
                            if (mr.renderLayer == RenderLayer.FirstPerson) _fpMeshes.Add(mr); else _meshes.Add(mr);
                            break;
                    }
                    if (c is IUiDrawer ui) _uiDrawers.Add(ui);
                }
            }
        }

        private readonly Dictionary<SpriteRenderer, float> _depthCache = new Dictionary<SpriteRenderer, float>();
        private readonly Dictionary<MeshRenderer, float> _meshDepthCache = new Dictionary<MeshRenderer, float>();

        private void SortByDepth(List<SpriteRenderer> list, Matrix4x4 view)
        {
            _depthCache.Clear();
            foreach (var s in list) _depthCache[s] = view.MultiplyPoint3x4(s.transform.position).z;
            // stable sort: far first
            list.Sort((a, b) => _depthCache[b].CompareTo(_depthCache[a]));
        }

        private void DrawSprite(SpriteRenderer sr, Camera cam, bool applyLighting)
        {
            Sprite sp = sr.sprite;
            sp.GetLocalCorners(out Vector2 min, out Vector2 max);
            sp.GetUVs(out float u0, out float v0, out float u1, out float v1);
            if (sr.flipX) { float t = u0; u0 = u1; u1 = t; }
            if (sr.flipY) { float t = v0; v0 = v1; v1 = t; }

            Matrix4x4 model;
            if (sr.billboard)
            {
                // Face the camera around the Y axis only (classic 2.5D sprite).
                Vector3 toCam = cam.transform.position - sr.transform.position;
                toCam.y = 0f;
                Quaternion rot = toCam.sqrMagnitude > 1E-06f ? Quaternion.LookRotation(-toCam.normalized) : Quaternion.identity;
                model = Matrix4x4.TRS(sr.transform.position, rot, sr.transform.lossyScale);
            }
            else model = sr.transform.localToWorldMatrix;

            var mat = sr.sharedMaterial;
            var sh = mat.shader;
            PrepareShader(sh);
            mat.ApplyState();
            if (_fpPass) GL.Disable(GL.DEPTH_TEST); // first-person sprites sort by order, not depth
            bool lit = applyLighting && !sr.fullBright && (sr.lit || _fpPass);
            sh.SetInt("uLit", lit ? 1 : 0);
            if (lit && _lightCount > 0 && Lighting.lightsPerDraw > 0)
            {
                if (_fpPass) SetDrawLights(sh, cam.transform.position - Vector3.one * 2f, cam.transform.position + Vector3.one * 2f);
                else if (RendererBounds.GetWorldAABB(sr, out var lmin, out var lmax)) SetDrawLights(sh, lmin, lmax);
            }
            Color tint = sr.color;
            if (lit)
            {
                if (_fpPass)
                {
                    var cfg = CameraConfig;
                    sh.SetVector("uSpriteNormal", _viewmodelNormal);
                    sh.SetFloat("uWrap", cfg.viewmodelWrap);
                    float e = cfg.viewmodelExposure;
                    tint = new Color(tint.r * e, tint.g * e, tint.b * e, tint.a);
                }
                else
                {
                    // billboards face the camera: light them with the flattened view direction as their normal
                    Vector3 n = cam.transform.position - sr.transform.position; n.y = 0f;
                    sh.SetVector("uSpriteNormal", n.sqrMagnitude > 1E-06f ? n.normalized : Vector3.back);
                    sh.SetFloat("uWrap", Lighting.spriteWrap);
                }
            }
            sh.SetMatrix("uModel", model);
            sh.SetColor("uTint", tint);
            sh.SetFloat("uAlphaCutoff", mat.alphaCutoff);
            mat.UploadProperties(sr.propertyBlock, 4);
            sp.texture.Bind(0);
            sh.SetInt("uTex", 0);
            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _quadVbo);

            if (sp.HasMesh && sp.vertices.Length <= MaxSpriteVerts && !sr.flipX && !sr.flipY)
            {
                // Tight polygon mesh (TexturePacker polygon packing): only the opaque region is drawn,
                // which also keeps overlapping atlas rects from bleeding into each other.
                int n = sp.vertices.Length;
                for (int i = 0; i < n; i++)
                {
                    sp.GetMeshVertex(i, out Vector2 pos, out Vector2 uv);
                    Set(i, pos.x, pos.y, uv.x, uv.y);
                }
                int icount = Mathf.Min(sp.triangles.Length, _quadIndices.Length);
                for (int i = 0; i < icount; i++) _quadIndices[i] = (uint)sp.triangles[i];
                fixed (float* p = _quadData) GL.BufferSubData(GL.ARRAY_BUFFER, 0, n * 5 * sizeof(float), p);
                GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _quadEbo);
                fixed (uint* p = _quadIndices) GL.BufferSubData(GL.ELEMENT_ARRAY_BUFFER, 0, icount * sizeof(uint), p);
                GL.DrawElements(GL.TRIANGLES, icount, GL.UNSIGNED_INT, 0);
            }
            else
            {
                // two triangles as a strip: BL, BR, TL, TR
                Set(0, min.x, min.y, u0, v0);
                Set(1, max.x, min.y, u1, v0);
                Set(2, min.x, max.y, u0, v1);
                Set(3, max.x, max.y, u1, v1);
                fixed (float* p = _quadData) GL.BufferSubData(GL.ARRAY_BUFFER, 0, 4 * 5 * sizeof(float), p);
                GL.DrawArrays(GL.TRIANGLE_STRIP, 0, 4);
            }
            DrawCalls++;
        }

        private void Set(int i, float x, float y, float u, float v)
        {
            _quadData[i * 5] = x; _quadData[i * 5 + 1] = y; _quadData[i * 5 + 2] = 0f;
            _quadData[i * 5 + 3] = u; _quadData[i * 5 + 4] = v;
        }

        /// <summary>Reads back the framebuffer (RGBA, top row first).</summary>
        public byte[] ReadFramebuffer(int width, int height)
        {
            var data = new byte[width * height * 4];
            GL.PixelStorei(GL.PACK_ALIGNMENT, 1);
            fixed (byte* p = data) GL.ReadPixels(0, 0, width, height, GL.RGBA, GL.UNSIGNED_BYTE, p);
            // flip to top-down
            var flipped = new byte[data.Length];
            int stride = width * 4;
            for (int y = 0; y < height; y++) Buffer.BlockCopy(data, (height - 1 - y) * stride, flipped, y * stride, stride);
            return flipped;
        }

        public void Dispose()
        {
            Ui.Dispose();
            PostFx.Dispose();
            Particles.Dispose();
            GpuOcclusion?.Dispose();
            Shadows.Dispose();
            Occlusion.Dispose();
            GL.DeleteVertexArray(_quadVao); GL.DeleteBuffer(_quadVbo); GL.DeleteBuffer(_quadEbo);
            GL.DeleteVertexArray(_skyVao); GL.DeleteBuffer(_skyVbo);
        }
    }
}
