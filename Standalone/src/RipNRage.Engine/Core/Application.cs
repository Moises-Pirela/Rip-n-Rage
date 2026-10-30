using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using RipNRage.Engine.Audio;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.PostFx;
using RipNRage.Engine.Platform;
using RipNRage.Engine.Tweening;

namespace RipNRage.Engine
{
    /// <summary>Start-up options (parsed from the command line by the game).</summary>
    public sealed class ApplicationOptions
    {
        public string Title = "Rip-n-Rage";
        public int Width = 1280;
        public int Height = 720;
        public bool VSync = true;
        /// <summary>Run this many frames then exit (automation / screenshots). 0 = run until the window closes.</summary>
        public int MaxFrames = 0;
        /// <summary>When set, a PNG screenshot is written to this path every <see cref="ScreenshotInterval"/> frames (and on exit).</summary>
        public string ScreenshotPath;
        public int ScreenshotInterval = 0;
        /// <summary>Fixed per-frame delta (seconds) for deterministic automation; 0 = real time.</summary>
        public float FixedFrameDelta = 0f;
        public string AssetRoot;
        public bool Verbose;
        public bool Mute;
        /// <summary>Path of tunables.cfg (auto-located in cwd / exe dir / parents when null).</summary>
        public string TunablesPath;
        /// <summary>Folder with the .glsl shader files (auto-located when null; embedded shaders are used if absent).</summary>
        public string ShaderDir;
        public bool DisablePostFx;
        /// <summary>Print a profiler summary line to the console every N seconds (0 = off).</summary>
        public float PerfLogInterval = 0f;
        /// <summary>Write one CSV row per frame with the frame / stage times (null = off).</summary>
        public string PerfCsvPath;
        /// <summary>glFinish after every GPU stage so CPU stage times include the GPU work (RIPNRAGE_PROFILE=1 does the same).</summary>
        public bool ProfileSync;
        /// <summary>Which GPU to ask for (--gpu auto|high|software).</summary>
        public Platform.GpuPreference Gpu = Platform.GpuPreference.Auto;
    }

    /// <summary>
    /// Owns the window, the renderer, the audio device and the scene, and runs the main loop
    /// (poll input → tick scene → tweens → render). Also exposes the UnityEngine.Application
    /// paths the save system needs.
    /// </summary>
    public sealed class Application : IDisposable
    {
        public static Application Current { get; private set; }
        public static bool isPlaying => Current != null;

        public Window Window { get; private set; }
        public RenderPipeline Renderer { get; private set; }
        public Scene Scene { get; private set; }
        public ApplicationOptions Options { get; }
        /// <summary>Hot-reloaded post-processing tunables (null when no tunables.cfg was found).</summary>
        public TunablesFile Tunables { get; private set; }

        public static string dataPath { get; private set; } = AppContext.BaseDirectory;
        public static string persistentDataPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RipNRage");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Real frames per second (wall clock, averaged over half a second) — see <see cref="Profiler"/> for the breakdown.</summary>
        public float Fps => (float)Profiler.Fps;
        private bool _quit;

        public Application(ApplicationOptions options)
        {
            Options = options;
            Current = this;
            Debug.Verbose = options.Verbose;
            dataPath = ResolveAssetRoot(options.AssetRoot);
            Debug.LogWarning("Asset root: " + dataPath);

            GpuInfo.ApplyPreference(options.Gpu); // must precede GLFW / libGL initialisation
            Window = new Window(options.Title, options.Width, options.Height, options.VSync);
            GpuInfo.Query();
            Debug.LogWarning($"OpenGL {Window.GlVersion} — {Window.GlRenderer} (GLFW {Glfw.GetVersionString()})");
            Debug.LogWarning("GPU: " + GpuInfo.Summary);
            if (GpuInfo.IsSoftware && options.Gpu != GpuPreference.AllowSoftware)
            {
                Debug.LogError("!!! SOFTWARE RENDERER: every frame is rasterised on the CPU — expect single-digit fps with the full pipeline. !!!");
                Debug.LogError(GpuInfo.Remedies);
                Debug.LogError("Pass --gpu software to silence this when a CPU renderer is intended (headless automation).");
            }
            else if (GpuInfo.Remedies.Length > 0) Debug.LogWarning(GpuInfo.Remedies);
            ShaderLibrary.Initialize(ShaderLibrary.Locate(options.ShaderDir));
            MaterialLibrary.TextureLoader = rel =>
            {
                try
                {
                    string p = Path.IsPathRooted(rel) ? rel : File.Exists(AssetPath(rel)) ? AssetPath(rel) : Path.Combine(ShaderLibrary.Directory ?? ".", rel);
                    return Texture2D.Load(p, FilterMode.Trilinear, TextureWrapMode.Repeat);
                }
                catch (Exception e) { Debug.LogWarning("material texture: " + e.Message); return null; }
            };
            Renderer = new RenderPipeline();
            if (!options.Mute) AudioEngine.Initialize();
            Scene = new Scene();

            string cfg = TunablesFile.Locate(options.TunablesPath);
            if (cfg != null)
            {
                Tunables = new TunablesFile(cfg);
                Renderer.PostFx.Settings = Tunables.Settings;
                Renderer.Lighting = Tunables.Lighting;
                Renderer.CameraConfig = Tunables.Camera;
                Debug.LogWarning("Tunables: " + cfg + " (hot reload on)");
            }
            else Debug.LogWarning("No tunables.cfg found — using default post-processing values.");
            if (options.DisablePostFx) Renderer.PostFx.Enabled = false;
            // Interactive session on a CPU rasterizer: clamp the expensive features so the game stays playable while
            // the user fixes the driver situation (the config file still hot-reloads; the clamp is re-applied).
            SoftwareFallback = GpuInfo.IsSoftware && options.Gpu != GpuPreference.AllowSoftware && options.MaxFrames == 0;
            if (SoftwareFallback)
            {
                ApplySoftwareFallback();
                if (Tunables != null) Tunables.Reloaded += _ => ApplySoftwareFallback();
            }
            if (options.ProfileSync) Profiler.SyncGpu = true;
            Profiler.SimulationDeltaFixed = options.FixedFrameDelta > 0f;
            Profiler.SimulationDelta = options.FixedFrameDelta;
        }

        /// <summary>True when the software-renderer quality clamp is active (see the constructor).</summary>
        public bool SoftwareFallback { get; private set; }

        private void ApplySoftwareFallback()
        {
            var pp = Renderer.PostFx.Settings; var lt = Renderer.Lighting; var cam = Renderer.CameraConfig;
            pp.renderScale = Math.Min(pp.renderScale, 0.5f);
            pp.motionBlurEnabled = false; pp.bloomPasses = Math.Min(pp.bloomPasses, 1);
            lt.volumetricEnabled = false;
            lt.shadowResolution = Math.Min(lt.shadowResolution, 1024); lt.shadowSoftness = 0f;
            lt.maxPointLights = Math.Min(lt.maxPointLights, 4); lt.lightsPerDraw = Math.Min(lt.lightsPerDraw, 2);
            if (cam.occlusionMode == OcclusionMode.Cpu) cam.occlusionMode = OcclusionMode.Gpu;
            Debug.LogWarning("Software renderer: quality clamped (render_scale 0.5, no volumetric fog / motion blur, 1024 shadows, 1 bloom pass). Pass --gpu software to keep the configured values.");
        }

        /// <summary>
        /// Finds the game's <c>assets</c> folder (sprites, textures, audio): <c>--assets</c>, then <c>RIPNRAGE_ASSETS</c>,
        /// then an <c>assets</c> folder in the executable / cwd directory or any of their parents.
        /// Either the assets folder itself or the folder containing it may be passed explicitly.
        /// </summary>
        public static string ResolveAssetRoot(string explicitRoot)
        {
            if (!string.IsNullOrEmpty(explicitRoot))
                return FindAssetsIn(explicitRoot) ?? throw new DirectoryNotFoundException("Not an assets folder (no Sprites inside): " + explicitRoot);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_ASSETS");
            if (!string.IsNullOrEmpty(env) && FindAssetsIn(env) is string fromEnv) return fromEnv;
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "assets");
                    if (IsAssetsFolder(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the game's assets folder. Pass --assets <path-to-Standalone/assets>.");
        }

        private static bool IsAssetsFolder(string dir) => Directory.Exists(Path.Combine(dir, "Sprites"));

        private static string FindAssetsIn(string dir)
        {
            dir = Path.GetFullPath(dir);
            if (IsAssetsFolder(dir)) return dir;
            string nested = Path.Combine(dir, "assets");
            return IsAssetsFolder(nested) ? nested : null;
        }

        /// <summary>Absolute path of a file under the game's <c>assets</c> folder.</summary>
        public static string AssetPath(string relative) => Path.Combine(dataPath, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

        public static void Quit() { if (Current != null) Current._quit = true; }

        /// <summary>Destroys every GameObject and starts a fresh scene (level restart / load game).</summary>
        public void ResetScene()
        {
            foreach (var go in new List<GameObject>(Scene.GameObjects))
                if (!go.destroyed && go.transform.parent == null) GameObject.DestroyImmediate(go);
            Graphics.Camera.main = null;
            Graphics.Particles.ParticleWorld.Clear();
            Renderer.GpuOcclusion?.Reset();
            InputSystem.ClearUiOwners();
            Tweening.TweenRunner.Update(0f);
            Scene = new Scene();
            Time.timeScale = 1f;
        }

        public void Run()
        {
            var sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds;
            int frame = 0;
            double benchStart = 0; int benchFrames = 0;
            double nextPerfLog = Options.PerfLogInterval > 0f ? Options.PerfLogInterval : double.MaxValue;
            System.IO.StreamWriter csv = null;
            if (Options.PerfCsvPath != null)
            {
                try { csv = new System.IO.StreamWriter(Options.PerfCsvPath, false); }
                catch (Exception e) { Debug.LogError("perf csv: " + e.Message); }
            }
            bool csvHeader = false;

            while (!Window.ShouldClose && !_quit)
            {
                Profiler.BeginFrame();
                double now = sw.Elapsed.TotalSeconds;
                // --fixed-dt only fakes the simulation delta; the profiler measures the real frame time separately
                float dt = Options.FixedFrameDelta > 0f ? Options.FixedFrameDelta : (float)(now - last);
                last = now;
                if (dt < 0f) dt = 0f;
                Profiler.SimulationDelta = dt;

                Profiler.Begin("poll");
                Window.PollEvents();
                Input.BeginFrame();
                InputSystem.Update(Math.Min(dt, 0.1f));
                Profiler.End();
                Profiler.Begin("hot reload");
                Tunables?.Poll(now);
                ShaderLibrary.Poll(now);
                Animation.AnimationLibrary.Poll(now);
                Graphics.Particles.VfxLibrary.Poll(now);
                Profiler.End();

                Profiler.Begin("scene");
                Scene.Tick(dt);
                Profiler.End();
                Profiler.Begin("physics");
                Physics.Step();
                Profiler.End();
                Profiler.Begin("tweens");
                TweenRunner.Update(Time.deltaTime);
                Profiler.End();
                Profiler.Begin("particles sim");
                Graphics.Particles.ParticleWorld.Update(Time.deltaTime);
                Profiler.End();

                Profiler.Begin("render");
                Renderer.Render(Scene, Window.Width, Window.Height);
                Profiler.End();
                Profiler.Begin("present", gpu: false);
                Window.SwapBuffers();
                Profiler.End();
                Input.EndFrame();
                Profiler.EndFrame();

                frame++;
                if (frame == 20) benchStart = sw.Elapsed.TotalSeconds;
                if (frame > 20) benchFrames++;

                if (csv != null)
                {
                    if (!csvHeader && frame > 2) { csv.WriteLine(Profiler.CsvHeader()); csvHeader = true; }
                    if (csvHeader) csv.WriteLine(Profiler.CsvRow());
                }
                if (now >= nextPerfLog)
                {
                    nextPerfLog = now + Options.PerfLogInterval;
                    Debug.LogWarning("[perf] " + Profiler.Summary());
                }

                if (Options.ScreenshotPath != null && Options.ScreenshotInterval > 0 && frame % Options.ScreenshotInterval == 0)
                    Screenshot(Options.ScreenshotPath.Replace("{n}", frame.ToString()));

                if (Options.MaxFrames > 0 && frame >= Options.MaxFrames) break;
            }

            csv?.Dispose();
            if (Options.ScreenshotPath != null && Options.ScreenshotInterval <= 0)
                Screenshot(Options.ScreenshotPath);
            if (benchFrames > 0)
            {
                double avg = (sw.Elapsed.TotalSeconds - benchStart) / benchFrames;
                Debug.LogWarning($"Average frame time after warm-up: {avg * 1000:0.0} ms ({1 / avg:0.0} fps) over {benchFrames} frames (wall clock{(Options.FixedFrameDelta > 0f ? $"; the simulation ran at a fixed {Options.FixedFrameDelta * 1000f:0.0} ms per frame" : "")})");
                Debug.LogWarning("[perf] " + Profiler.Summary());
                string rep = Renderer.PostFx.ProfileReport();
                if (rep.Length > 0) Debug.LogWarning(rep);
            }
        }

        public void Screenshot(string path)
        {
            try
            {
                Platform.GL.Finish();
                var px = Renderer.ReadFramebuffer(Window.Width, Window.Height);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                PngEncoder.Save(path, Window.Width, Window.Height, px);
                Debug.LogWarning("Screenshot saved: " + path);
                if (Environment.GetEnvironmentVariable("RIPNRAGE_DEBUG_SHADOW") == "1" && Renderer.Shadows.Ready)
                {
                    var sm = Renderer.Shadows.ReadDepthImage();
                    string smPath = Path.ChangeExtension(path, null) + "_shadowmap.png";
                    PngEncoder.Save(smPath, Renderer.Shadows.Resolution, Renderer.Shadows.Resolution, sm);
                    Debug.LogWarning("Shadow map saved: " + smPath + "  matrix:\n" + Renderer.Shadows.ShadowMatrix);
                }
            }
            catch (Exception e) { Debug.LogError("Screenshot failed: " + e.Message); }
        }

        public void Dispose()
        {
            AudioEngine.Shutdown();
            Renderer?.Dispose();
            Window?.Dispose();
            Current = null;
        }
    }
}
