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

        public float Fps { get; private set; }
        private bool _quit;

        public Application(ApplicationOptions options)
        {
            Options = options;
            Current = this;
            Debug.Verbose = options.Verbose;
            dataPath = ResolveAssetRoot(options.AssetRoot);
            Debug.LogWarning("Asset root: " + dataPath);

            Window = new Window(options.Title, options.Width, options.Height, options.VSync);
            Debug.LogWarning($"OpenGL {Window.GlVersion} — {Window.GlRenderer} (GLFW {Glfw.GetVersionString()})");
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
            Tweening.TweenRunner.Update(0f);
            Scene = new Scene();
            Time.timeScale = 1f;
        }

        public void Run()
        {
            var sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds;
            int frame = 0;
            float fpsAccum = 0f; int fpsFrames = 0;
            double benchStart = 0; int benchFrames = 0;

            while (!Window.ShouldClose && !_quit)
            {
                double now = sw.Elapsed.TotalSeconds;
                float dt = Options.FixedFrameDelta > 0f ? Options.FixedFrameDelta : (float)(now - last);
                last = now;
                if (dt < 0f) dt = 0f;

                Window.PollEvents();
                Input.BeginFrame();
                Tunables?.Poll(now);
                ShaderLibrary.Poll(now);
                Animation.AnimationLibrary.Poll(now);

                Scene.Tick(dt);
                Physics.Step();
                TweenRunner.Update(Time.deltaTime);

                Renderer.Render(Scene, Window.Width, Window.Height);
                Window.SwapBuffers();
                Input.EndFrame();

                frame++;
                if (frame == 20) benchStart = sw.Elapsed.TotalSeconds;
                if (frame > 20) benchFrames++;
                fpsAccum += dt; fpsFrames++;
                if (fpsAccum >= 0.5f) { Fps = fpsFrames / fpsAccum; fpsAccum = 0f; fpsFrames = 0; }

                if (Options.ScreenshotPath != null && Options.ScreenshotInterval > 0 && frame % Options.ScreenshotInterval == 0)
                    Screenshot(Options.ScreenshotPath.Replace("{n}", frame.ToString()));

                if (Options.MaxFrames > 0 && frame >= Options.MaxFrames) break;
            }

            if (Options.ScreenshotPath != null && Options.ScreenshotInterval <= 0)
                Screenshot(Options.ScreenshotPath);
            if (benchFrames > 0)
            {
                double avg = (sw.Elapsed.TotalSeconds - benchStart) / benchFrames;
                Debug.LogWarning($"Average frame time after warm-up: {avg * 1000:0.0} ms ({1 / avg:0.0} fps) over {benchFrames} frames");
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
