using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
using System;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game
{
    /// <summary>
    /// Standalone Rip-n-Rage entry point (no Unity). Creates the engine application, builds the
    /// first scene (a level, or the title over it) and runs the main loop.
    ///
    /// Options:
    ///   --assets &lt;dir&gt;        the assets folder, or a folder containing it (auto-detected otherwise)
    ///   --width W --height H   window size (default 1280x720)
    ///   --no-vsync             disable vertical sync
    ///   --ecs-demo             spawn the prefabs/demo ECS scene (camera, parented spinning meshes, sprite, light)
    ///   --dump-level FILE      write the built scene's entity manifest (10.0: the prefab-port gate) and report it
    ///   --overlay 0|1|2        debug overlay mode for this run (off / frame stats / full profiler)
    ///   --windowed / --borderless / --fullscreen   display mode (default: the one saved in graphics.json, else windowed)
    ///   --monitor N            monitor index for borderless / fullscreen (0 = first; default: primary)
    ///   --resolution WxH[@Hz]  exclusive fullscreen video mode (default: the monitor's desktop mode)
    ///   --mute                 disable audio
    ///   --verbose              print Debug.Log output
    ///   --frames N             run N frames then exit (automation)
    ///   --screenshot path.png  save a screenshot on exit (or every --shot-interval frames; "{n}" = frame number)
    ///   --fixed-dt seconds     deterministic frame delta for automation
    ///   --script name          automation script: "look", "fire", "walk", "weapons" (see DemoScript)
    ///   --tunables path.cfg    post-processing tunables file (default: tunables.cfg found in cwd/exe dir/parents)
    ///   --no-postfx            start with post-processing disabled (F4 toggles it)
    ///   --shaders dir          folder with the .glsl shader files (default: "shaders" found in cwd/exe dir/parents)
    ///   --level file.map       play a level file (default: levels/dock.map)
    ///   --export-dock out.map  write the built-in dock arena as a .map file and exit
    ///   --validate-access      throw when a system touches a component it did not declare (gate runs)
    ///   --threads N            rigid solver worker threads (0 = one per core, 1 = single-threaded: repeatable runs)
    ///   --seed N               seed the shared Random (scripted runs default to 12345; 0 = time-seeded)
    ///   --load slot            load a saved game (slots live in <persistentDataPath>/saves; "quick", "checkpoint", ...)
    ///   --menu / --no-menu     start on the title screen / straight in the level (default: the title screen, unless an
    ///                          automation option — --script, --frames, --load — is given)
    ///   --vfx dir              folder with the particle effect JSON files (default: "vfx" found in cwd / exe dir / parents)
    ///   --perf-log S           print a profiler summary (real frame time, stages, GC) every S seconds
    ///   --perf-csv file.csv    write one row per frame with the frame / stage times
    ///   --profile              glFinish after every GPU stage so CPU stage times include the GPU work (RIPNRAGE_PROFILE=1)
    ///   --gpu auto|high|software  GPU preference: high = discrete GPU on hybrid systems (Linux PRIME offload), software = accept a CPU rasterizer silently
    ///   --gpu-info             print the OpenGL renderer / vendor / version and whether it is a software rasterizer, then exit (code 3 if it is)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions();
            string script = null;
            int? seed = null;
            int threads = -1;
            string level = null;
            string dumpLevel = null;
            string exportDock = null, exportAnims = null;
            string loadSlot = null;
            bool? startInMenu = null;
            bool gpuInfoOnly = false;
            bool displayFromCli = false;
            bool ecsDemo = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                switch (a)
                {
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--width": options.Width = int.Parse(Next()); break;
                    case "--height": options.Height = int.Parse(Next()); break;
                    case "--no-vsync": options.VSync = false; break;
                    case "--ecs-demo": ecsDemo = true; break;
                    case "--overlay": DebugOverlay.ForcedMode = int.Parse(Next()); break;
                    case "--windowed": options.DisplayMode = RipNRage.Engine.Platform.DisplayMode.Windowed; displayFromCli = true; break;
                    case "--borderless": options.DisplayMode = RipNRage.Engine.Platform.DisplayMode.Borderless; displayFromCli = true; break;
                    case "--fullscreen": options.DisplayMode = RipNRage.Engine.Platform.DisplayMode.Fullscreen; displayFromCli = true; break;
                    case "--monitor": options.Monitor = int.Parse(Next()); break;
                    case "--resolution":
                    {
                        string r = Next();
                        string[] hz = r.Split('@');
                        string[] wh = hz[0].ToLowerInvariant().Split('x');
                        if (wh.Length != 2) throw new ArgumentException("--resolution expects WxH or WxH@Hz");
                        options.FullscreenWidth = int.Parse(wh[0]); options.FullscreenHeight = int.Parse(wh[1]);
                        if (hz.Length > 1) options.RefreshRate = int.Parse(hz[1]);
                        break;
                    }
                    case "--mute": options.Mute = true; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--shot-interval": options.ScreenshotInterval = int.Parse(Next()); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--script": script = Next(); break;
                    case "--seed": seed = int.Parse(Next()); break;
                    case "--threads": threads = int.Parse(Next()); break;
                    case "--validate-access":
                        RipNRage.Engine.Ecs.World.ValidateAccess = true;
                        RipNRage.Engine.Ecs.World.ReportAccessOnly = Environment.GetEnvironmentVariable("ECS_ACCESS_REPORT") == "1"; // audit: list, do not throw
                        break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--shaders": options.ShaderDir = Next(); break;
                    case "--level": level = Next(); break;
                    case "--dump-level": dumpLevel = Next(); break;
                    case "--export-dock": exportDock = Next(); break;
                    case "--load": loadSlot = Next(); break;
                    case "--no-postfx": options.DisablePostFx = true; break;
                    case "--menu": startInMenu = true; break;
                    case "--perf-log": options.PerfLogInterval = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--perf-csv": options.PerfCsvPath = Next(); break;
                    case "--profile": options.ProfileSync = true; break;
                    case "--gpu":
                    {
                        string g = Next().ToLowerInvariant();
                        options.Gpu = g == "high" || g == "discrete" || g == "performance" ? RipNRage.Engine.Platform.GpuPreference.High
                            : g == "software" || g == "cpu" ? RipNRage.Engine.Platform.GpuPreference.AllowSoftware : RipNRage.Engine.Platform.GpuPreference.Auto;
                        break;
                    }
                    case "--gpu-info": gpuInfoOnly = true; break;
                    case "--no-menu": startInMenu = false; break;
                    case "--animations": RipNRage.Game.Content.GameAssets.AnimationsPath = Next(); break;
                    case "--vfx": RipNRage.Game.Content.GameAssets.VfxPath = Next(); break;
                    case "--skeletons": RipNRage.Game.Content.GameAssets.SkeletonsPath = Next(); break;
                    case "--export-animations": exportAnims = Next(); break;
                    case "--help": case "-h":
                        Console.WriteLine("RipNRage standalone. See Program.cs for options.");
                        return 0;
                    default:
                        Console.WriteLine("Unknown option: " + a);
                        return 1;
                }
            }

            try
            {
                // saved display choices (graphics.json) — not for automation runs, which must stay windowed and sized
                bool automation = script != null || options.MaxFrames > 0 || gpuInfoOnly || exportDock != null || exportAnims != null;
                if (!automation && !displayFromCli) RipNRage.Game.Content.GraphicsSettings.ApplyStartupDisplay(options);
                // Scripted runs roll fixed dice: the shared Random (weapon spread, death roll, pickup bob) is time-seeded
                // otherwise, and a gate compared across builds is then a coin flip at every edge (found when a kill at
                // the end of a firing window came and went between identical runs). --seed N picks the seed, --seed 0
                // keeps the clock.
                if (seed == null && script != null) seed = 12345;
                if (seed is int fixedSeed && fixedSeed != 0) RipNRage.Engine.Random.InitState(fixedSeed);
                using var app = new Application(options);
                if (seed is int s && s != 0) Debug.LogWarning($"Random seeded: {s} (scripted runs are deterministic; --seed N overrides, --seed 0 keeps the clock)");
                // the rigid solver's worker count, as the bench takes it: 1 makes a scripted run bit-for-bit repeatable
                // (the parallel solver still carries an order dependence beyond the contact merge — see the roadmap)
                if (threads >= 0) { RipNRage.Engine.Physics.World.Rigid.threads = threads; RipNRage.Engine.PhysicsSettings.Current.threads = threads; Debug.LogWarning($"Rigid solver threads: {threads}"); }
                if (gpuInfoOnly)
                {
                    Console.WriteLine(RipNRage.Engine.Platform.GpuInfo.Summary);
                    if (RipNRage.Engine.Platform.GpuInfo.Remedies.Length > 0) Console.WriteLine(RipNRage.Engine.Platform.GpuInfo.Remedies);
                    Console.WriteLine($"timer queries: {(RipNRage.Engine.Platform.GL.HasTimerQueries ? "yes" : "no")}   occlusion queries: {(RipNRage.Engine.Graphics.Culling.GpuOcclusion.Available ? "yes" : "no")}");
                    return RipNRage.Engine.Platform.GpuInfo.IsSoftware ? 3 : 0;
                }
                RipNRage.Game.Content.MovementSettings.Attach(app); // tunables.cfg [movement], live
                RipNRage.Game.Content.RageDef.Attach(app);           // tunables.cfg [rage], live
                if (exportDock != null) { RipNRage.Game.Content.Level.DockLevel.Export(exportDock); return 0; }
                if (exportAnims != null) { RipNRage.Game.Content.GameAssets.Load(); RipNRage.Game.Content.GameAssets.ExportAnimations(exportAnims); return 0; }
                // a level is required since 9.0 (the built-in arena is gone)
                string levelPath = RipNRage.Game.Content.Level.LevelLoader.Locate(level ?? "dock.map");
                if (levelPath == null) { Console.Error.WriteLine("Level not found: " + (level ?? "dock.map")); return 1; }
                RipNRage.Engine.Ecs.Input.InputOverride.Scripted = script != null; // the devices stay out of a scripted run
                bool dumped = false;
                GameSession.OnSceneBuilt = () =>
                {
                    UiScreens.Add(new DebugOverlay());
                    if (script != null) UiScreens.Add(new DemoScript { scriptName = script });
                    if (dumpLevel == null) return;
                    // 10.0: what this scene built, as text — the gate a prefab port is compared against. The file is
                    // the first build's; every build reports its line (a rebuild that differs is the news). The build
                    // goes partly through the command buffer (colliders, props, joints), so the dump syncs first —
                    // what the first frame's Update does before anything else runs (and what --ecs-demo does too).
                    app.World.Sync();
                    string summary = dumped
                        ? RipNRage.Engine.Ecs.EntityManifest.Summary(RipNRage.Engine.Ecs.EntityManifest.Build(app.World))
                        : RipNRage.Engine.Ecs.EntityManifest.Write(app.World, dumpLevel);
                    dumped = true;
                    Debug.LogWarning($"[test] level: {summary}");
                };
                app.World.AddSystemsFromAssemblies(typeof(Program).Assembly); // game ECS systems
                InputSystem.Initialize();          // rebindable gameplay / UI schemes (controls.json)
                InputSystem.ManageCursor = true;   // locked in gameplay, free while any menu is up
                RipNRage.Game.Content.GraphicsSettings.Install(); // player's graphics overrides on top of tunables.cfg
                GameSession.DefaultLevelPath = levelPath;
                // automation runs (scripts, frame-limited captures, direct loads) skip the title screen
                bool menu = startInMenu ?? (script == null && loadSlot == null && options.MaxFrames == 0);
                if (menu) GameSession.ShowMainMenu(levelPath);
                else GameSession.StartLevel(levelPath);
                if (loadSlot != null && !RipNRage.Game.Content.Level.GameSaveSystem.Load(loadSlot)) Console.Error.WriteLine("Could not load slot " + loadSlot);
                if (ecsDemo) RipNRage.Game.Ecs.EcsDemo.Spawn(app.World, System.IO.Path.Combine(Application.dataPath, "..", "prefabs", "demo"));
                app.Run();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Fatal: " + e);
                return 2;
            }
        }
    }

    /// <summary>
    /// F3 cycles the on-screen readout: off → one-line frame stats → full profiler (frame-time graph, per-stage
    /// CPU/GPU table, memory, renderer counters, game state). The F-keys drive the save system / renderer toggles
    /// (Escape belongs to the pause menu).
    /// </summary>
    public sealed class DebugOverlay : IUiScreen, IUiLayer
    {
        public int UiLayer => UiLayers.Debug;
        /// <summary>0 = hidden, 1 = compact, 2 = full profiler.</summary>
        public int mode = 2;
        /// <summary>--overlay N: start in this mode regardless of graphics.json (profiling runs).</summary>
        public static int? ForcedMode;
        /// <summary>The live overlay (the graphics menu adjusts its mode).</summary>
        public static DebugOverlay Current { get; private set; }
        public bool visible { get => mode > 0; set => mode = value ? 2 : 0; }
        private static string _message = ""; private static float _messageTimer;

        public DebugOverlay()
        {
            Current = this;
            RipNRage.Game.Content.Level.GameSaveSystem.OnMessage = m => { _message = m; _messageTimer = 3f; };
            mode = ForcedMode ?? RipNRage.Game.Content.GraphicsSettings.Current.overlayMode ?? mode;
        }

        public void Update()
        {
            if (_messageTimer > 0f) _messageTimer -= Time.unscaledDeltaTime;
            if (InputSystem.Gameplay["quick_save"].Pressed) RipNRage.Game.Content.Level.GameSaveSystem.Save("quick");
            // the developer keys ask the flow like the menus do: allowed in any phase of a level, never on the title (8.8b)
            if (InputSystem.Gameplay["quick_load"].Pressed) RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.LoadQuick, debug: true);
            if (Input.GetKeyDown(KeyCode.F7)) RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.LoadCheckpoint, debug: true);
            if (Input.GetKeyDown(KeyCode.F8)) RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.Restart, debug: true);
            if (Input.GetKeyDown(KeyCode.F3)) { mode = (mode + 1) % 3; RipNRage.Game.Content.GraphicsSettings.Current.overlayMode = mode; RipNRage.Game.Content.GraphicsSettings.Save(); }
            if (Input.GetKeyDown(KeyCode.F4)) Application.Current.Renderer.PostFx.Enabled = !Application.Current.Renderer.PostFx.Enabled;
            if (Input.GetKeyDown(KeyCode.F5)) { Application.Current.Tunables?.Reload(); ShaderLibrary.ReloadAll(); }
            if (Input.GetKeyDown(KeyCode.F12)) Application.Current.Screenshot("screenshot_" + Time.frameCount + ".png");
            if (Input.GetKeyDown(KeyCode.F11)) { Profiler.SyncGpu = !Profiler.SyncGpu; _message = Profiler.SyncGpu ? "Profiler: GPU sync on (CPU times include GPU work)" : "Profiler: GPU sync off"; _messageTimer = 3f; }
        }

        private readonly RipNRage.Engine.UI.ClayUi _clay = new RipNRage.Engine.UI.ClayUi(8192);
        public void Close() { _clay.Dispose(); if (Current == this) Current = null; }

        // the overlay keeps its pixel-exact text size (2 px per glyph pixel at 1080p, never below 1): Clay lengths are
        // reference pixels, so screen sizes are divided by the canvas scale
        private float R(float screenPx) => screenPx / _clay.Scale;

        public void DrawUi(UiBatch ui)
        {
            var renderer = Application.Current.Renderer;
            bool software = RipNRage.Engine.Platform.GpuInfo.IsSoftware;
            if (_messageTimer <= 0f && !software && mode == 0) return;

            var c = _clay;
            c.PointerEnabled = false;
            c.Begin(ui);
            float s = Mathf.Max(1f, ui.Scale * 2f);
            float lineH = (BitmapFont.GlyphH + 1) * s;
            var line = new TextStyle(R(s), Color.white, wrap: Clay_TextWrapMode.Newlines);

            if (_messageTimer > 0f)
            {
                using (c.Box(ClayScreen.FloatingColumn(0.38f * ClayScreen.RefHeight)))
                    c.Text(_message.ToUpperInvariant(), ClayScreen.Line(R(Mathf.Max(1f, ui.Scale * 2f) * 1.5f), new Color(1f, 0.9f, 0.4f, 1f)));
            }
            if (software)
            {
                // always shown: the single most important performance fact about this machine
                string warn = $"SOFTWARE RENDERER ({RipNRage.Engine.Platform.GpuInfo.Renderer.ToUpperInvariant()}): THE GPU IS NOT IN USE, EVERY PIXEL IS COMPUTED ON THE CPU. SEE THE CONSOLE FOR FIXES.";
                var box = ClayScreen.FloatingColumn(R(lineH * 1.6f + 2f));
                box.padding = new RipNRage.Engine.UI.Pad(R(8f), R(4f));
                box.background = new Color(0.6f, 0.05f, 0.05f, 0.9f);
                using (c.Box(box)) c.Text(warn, ClayScreen.Line(R(s), Color.white));
            }
            if (mode == 0) { c.End(); return; }

            // ---- top-left column: frame line, then (full mode) the stats block
            using (c.Box(new El { vertical = true, gap = R(lineH * 0.2f), padding = new RipNRage.Engine.UI.Pad(R(12f), 0f, R(12f) - R(BitmapFont.GlyphH * s), 0f),
                floating = Float.ToRoot(Clay_FloatingAttachPointType.LeftTop, default, 1) }))
            {
                // the real numbers (wall clock), never the simulated delta
                string frame = $"FPS {Profiler.Fps:0.0}  1% LOW {Profiler.FpsLow1:0.0}   FRAME {Profiler.FrameMsAvg:0.0} MS AVG  {Profiler.FrameMsP99:0.0} P99  {Profiler.FrameMsMax:0.0} MAX";
                if (Profiler.GpuTimersAvailable) frame += $"   GPU {Profiler.GpuFrameMsAvg:0.0} MS";
                frame += $"   PRESENT {Profiler.PresentMsAvg:0.0} MS";
                if (Profiler.SimulationDeltaFixed) frame += $"   SIM DT FIXED {Profiler.SimulationDelta * 1000f:0.0} MS (NOT REAL TIME)";
                else if (Time.timeScale != 1f) frame += $"   TIMESCALE {Time.timeScale:0.00}";
                Color frameColor = Profiler.FrameMsAvg > 33.4 ? new Color(1f, 0.45f, 0.35f, 1f) : Profiler.FrameMsAvg > 16.8 ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.6f, 1f, 0.6f, 1f);
                c.Text(frame, new TextStyle(R(s), frameColor, wrap: Clay_TextWrapMode.None));
                if (mode == 2) c.Text(BuildStats(renderer), new TextStyle(R(s), new Color(1, 1, 1, 0.85f), wrap: Clay_TextWrapMode.Newlines));
            }
            if (mode == 2)
            {
                BuildStageTable(c, ui, s, lineH);
                BuildFrameGraph(c, ui, s, lineH);
            }
            c.End();
        }

        private string BuildStats(RenderPipeline renderer)
        {
            string text = "";
            text += $"GPU  {RipNRage.Engine.Platform.GpuInfo.Renderer.ToUpperInvariant()}  ({RipNRage.Engine.Platform.GpuInfo.Vendor.ToUpperInvariant()})  GL {RipNRage.Engine.Platform.GpuInfo.Version.ToUpperInvariant()}{(RipNRage.Engine.Platform.GpuInfo.IsIntegrated ? "  [INTEGRATED]" : "")}\n";
            text += $"MEMORY  GC GEN0/1/2 {Profiler.Gen0PerSecond:0.0}/{Profiler.Gen1PerSecond:0.0}/{Profiler.Gen2PerSecond:0.0} PER S   ALLOC {Profiler.AllocBytesPerFrame / 1024.0:0.0} KB/FRAME   HEAP {Profiler.HeapBytes / (1024.0 * 1024.0):0.0} MB\n";
            text += $"SCENE  FIXED STEPS {Application.Current.FixedStepsLastFrame}{(Application.Current.FixedStepsLastFrame >= 8 ? " (CLAMPED: SIM BEHIND)" : "")}   DRAWS {renderer.DrawCalls} ({renderer.BatchedSprites} BATCHED SPRITES, {renderer.InstancedMeshes} INSTANCED MESHES)\n";
            if (RipNRage.Game.Ecs.PlayerCamera.Alive)
            {
                var p = RipNRage.Game.Ecs.PlayerCamera.Position;
                text += $"POS {p.x:0.0} {p.y:0.0} {p.z:0.0}  YAW {RipNRage.Game.Ecs.PlayerCamera.Rotation.eulerAngles.y:0}";
            }
            if (RipNRage.Game.Ecs.PlayerLink.Alive)
            {
                string weapon = CombatController.CurrentWeaponData?.name ?? "-";
                RipNRage.Game.Ecs.Rages.TryGet(out var rage);
                text += $"   HP {RipNRage.Game.Ecs.PlayerLink.Health:0} ARMOR {RipNRage.Game.Ecs.PlayerLink.Armor:0} FURY {rage.Fury:0.00} CALM {rage.Calm:0.00} {rage.State}  WEAPON {weapon}";
            }
            text += "\n";
            var fx = renderer.PostFx;
            var st = fx.Settings;
            var lt = renderer.Lighting;
            text += $"POSTFX {(fx.IsActive ? "ON" : "OFF")} (F4)  BLOOM {(st.bloomEnabled ? "ON" : "OFF")}  CA {(st.chromaticEnabled ? "ON" : "OFF")}  VIGNETTE {(st.vignetteEnabled ? "ON" : "OFF")}  MOTION BLUR {(st.motionBlurEnabled ? "ON" : "OFF")}  RENDER SCALE {st.renderScale:0.00}  F5 RELOAD CFG\n";
            text += $"SHADOWS {(lt.shadowsEnabled ? lt.shadowResolution.ToString() : "OFF")}  VOLUMETRIC FOG {(lt.volumetricEnabled ? $"ON ({lt.volSteps} STEPS @ {lt.volScale:0.00})" : "OFF")}  SUN {lt.sunElevation:0}/{lt.sunAzimuth:0}  SHADERS {(ShaderLibrary.Directory != null ? "FILES (HOT RELOAD)" : "EMBEDDED")}\n";
            var sm = renderer.Shadows;
            text += $"SHADOW CACHE {sm.StaticRenders} STATIC RENDERS  {sm.StaticCasters} DRAWN / {sm.StaticCulled} CULLED  DYNAMIC {sm.DynamicCasters}  LIGHT SETS {renderer.LightUploads}\n";
            text += $"VFX {RipNRage.Engine.Graphics.Particles.VfxLibrary.Effects.Count} EFFECTS  LIVE {RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveInstances} FX / {RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveParticles} PARTICLES  {renderer.Particles.DrawCalls} DRAWS  {RipNRage.Engine.Graphics.Particles.ParticleWorld.CollisionRaycasts} RAYCASTS\n";
            var cc = renderer.CameraConfig;
            text += $"FOV WORLD {(RipNRage.Game.Ecs.PlayerCamera.Alive && Application.Current.World.Has<RipNRage.Engine.Ecs.Rendering.CameraLens>(RipNRage.Game.Ecs.PlayerLink.CameraEntity) ? Application.Current.World.Get<RipNRage.Engine.Ecs.Rendering.CameraLens>(RipNRage.Game.Ecs.PlayerLink.CameraEntity).FieldOfView : 0f):0} / VIEWMODEL {cc.viewmodelFov:0} ({(cc.viewmodelLit ? "LIT" : "UNLIT")})  CULLING {(cc.frustumCulling ? "FRUSTUM " : "")}{(cc.occlusionCulling ? $"OCCLUSION {cc.occlusionMode.ToString().ToUpperInvariant()}{(renderer.GpuOcclusion != null && cc.occlusionMode == OcclusionMode.Gpu ? $" ({renderer.GpuOcclusion.Tested} QUERIES, {renderer.GpuOcclusion.Pending} PENDING)" : "")} " : "")}{renderer.Culling}\n";
            var world = Physics.World;
            text += $"PHYSICS {world.StaticCount} STATIC ({world.CellCount} CELLS) + {world.DynamicCount} DYNAMIC COLLIDERS  {world.RigidBodyCount} RIGID BODIES ({world.Rigid.ManifoldCount} MANIFOLDS, {world.Rigid.ContactCount} CONTACTS ({world.Rigid.MatchedPoints} WARM, {world.Rigid.CachedManifolds} CACHED), {world.Rigid.JointCount} JOINTS SOLVED / {Application.Current.World.GetSystem<RipNRage.Engine.Ecs.Physics.JointSystem>()?.ActiveJoints ?? 0} ATTACHED, {world.Rigid.IslandCount} ISLANDS (MAX {world.Rigid.LargestIsland}), {world.Rigid.ThreadsUsed} THREADS, {world.Rigid.CcdSweepsThisStep} CCD, {world.Rigid.positionCorrection.ToString().ToUpperInvariant()})  {world.RaycastsLastFrame} RAYS / {world.SweepsLastFrame} SWEEPS / {world.QueriesLastFrame} QUERIES\n";
            if (RipNRage.Game.Ecs.PlayerLink.HasMotor)
            {
                ref var pm = ref RipNRage.Game.Ecs.PlayerLink.Motor;
                ref var cm = ref RipNRage.Game.Ecs.PlayerLink.Character;
                Vector3 vel = RipNRage.Game.Ecs.PlayerLink.Velocity;
                string move = pm.IsDashing ? "DASH" : pm.IsSliding ? "SLIDE" : pm.IsWallRunning ? $"WALLRUN {(pm.WallSide < 0 ? "L" : "R")}" : cm.IsGrounded ? "GROUND" : "AIR";
                float slope = Mathf.Acos(Mathf.Clamp(cm.GroundNormal.y, -1f, 1f)) * Mathf.Rad2Deg;
                var fall = RipNRage.Game.Ecs.FallSystem.Current;
                text += $"MOVER {move}  SPEED {new Vector3(vel.x, 0f, vel.z).magnitude:0.0} / {pm.CurrentSpeed:0.0} M/S  VSPEED {vel.y:0.0}  RIDE {cm.GroundDistance:0.00} / {cm.RideHeight:0.00}  SLOPE {slope:0}°  FALLS {fall.Count} ({RipNRage.Game.Ecs.Falls.Label(fall.LastKind)})\n";
            }
            text += "WASD MOVE  MOUSE LOOK  LMB FIRE  RMB JAB  1-4/WHEEL WEAPONS  SPACE JUMP  F3 OVERLAY  F11 GPU SYNC  ESC PAUSE\n";
            text += "F6 QUICK SAVE  F9 QUICK LOAD  F7 LOAD CHECKPOINT  F8 RESTART LEVEL";
            return text;
        }

        private void BuildStageTable(RipNRage.Engine.UI.ClayUi c, UiBatch ui, float s, float lineH)
        {
            var stages = new System.Collections.Generic.List<Profiler.Stage>(Profiler.Stages);
            stages.RemoveAll(st => st.cpuAvg < 0.02 && st.gpuAvg < 0.02);
            stages.Sort((a, b) => Mathf.Max((float)b.cpuAvg, (float)b.gpuAvg).CompareTo(Mathf.Max((float)a.cpuAvg, (float)a.gpuAvg)));
            float w = Mathf.Min(ui.Width * 0.42f, 560f * s / 2f);
            var text = new TextStyle(R(s), Color.white, wrap: Clay_TextWrapMode.None);
            // bottom-left box above the HUD's bottom strip
            using (c.Box(new El { vertical = true, padding = new RipNRage.Engine.UI.Pad(R(4f)), background = new Color(0f, 0f, 0f, 0.55f),
                floating = Float.ToRoot(Clay_FloatingAttachPointType.LeftBottom, new Vector2(R(8f), -R(72f * s)), 1) }))
            {
                string header = Profiler.GpuTimersAvailable ? "STAGE                    CPU MS   MAX     GPU MS" : "STAGE                    CPU MS   MAX";
                if (Profiler.SyncGpu) header += "   [SYNC]";
                text.color = new Color(1f, 0.85f, 0.4f, 1f);
                c.Text(header, text);
                double total = Mathf.Max((float)Profiler.FrameMsAvg, 0.001f);
                foreach (var st in stages)
                {
                    string name = st.name.ToUpperInvariant();
                    if (name.Length > 22) name = name.Substring(0, 22);
                    string row = $"{PadText(new string(' ', st.depth) + name, 24)} {st.cpuAvg,6:0.00} {st.cpuMax,6:0.0}";
                    if (st.hasGpu) row += $" {st.gpuAvg,8:0.00}";
                    // bar behind the row: fraction of the frame (custom element: drawn before the row's text)
                    float frac = Mathf.Clamp01((float)(Mathf.Max((float)st.cpuAvg, st.hasGpu ? (float)st.gpuAvg : 0f) / total));
                    using (c.Box(new El { width = Size.Fixed(R(w)), height = Size.Fixed(R(lineH)), alignY = Clay_LayoutAlignmentY.Center,
                        custom = (b, r, a) => b.FillRect(new Rect(r.x, r.y + 1f, r.width * frac, r.height - 3f * s), new Color(0.9f, 0.5f, 0.2f, 0.25f * a)) }))
                    {
                        text.color = st.depth > 0 ? new Color(0.8f, 0.8f, 0.85f, 0.9f) : new Color(1f, 1f, 1f, 0.95f);
                        c.Text(row, text);
                    }
                }
                string note = Profiler.GpuTimersAvailable
                    ? "GPU = TIMER QUERY (3 FRAMES OLD).  PRESENT = SWAPBUFFERS WAIT (VSYNC / GPU BEHIND).  F11 = GLFINISH PER STAGE"
                    : "NO GPU TIMER QUERIES: F11 (GLFINISH PER STAGE) MAKES CPU TIMES INCLUDE THE GPU WORK";
                c.Text(note, new TextStyle(R(s * 0.75f), new Color(0.7f, 0.7f, 0.75f, 0.9f), wrap: Clay_TextWrapMode.None));
            }
        }

        private System.Action<UiBatch, Rect, float> _drawGraph;
        private float _graphLineH, _graphS;

        private void BuildFrameGraph(RipNRage.Engine.UI.ClayUi c, UiBatch ui, float s, float lineH)
        {
            float gw = Mathf.Min(ui.Width * 0.3f, 480f * s / 2f), gh = 70f * s / 2f * 1.4f;
            _graphLineH = lineH; _graphS = s;
            c.Leaf(new El
            {
                width = Size.Fixed(R(gw)), height = Size.Fixed(R(gh)), background = new Color(0f, 0f, 0f, 0.55f),
                floating = Float.ToRoot(Clay_FloatingAttachPointType.RightTop, new Vector2(-R(12f), R(12f + lineH * 1.3f)), 1),
                custom = _drawGraph ??= DrawFrameGraph,
            });
        }

        /// <summary>Custom Clay element: bars of the frame-time history inside the box Clay laid out.</summary>
        private void DrawFrameGraph(UiBatch ui, Rect box, float alpha)
        {
            float s = _graphS, lineH = _graphLineH, gw = box.width, gh = box.height;
            float maxMs = 50f;
            foreach (var v in Profiler.History) if (v > maxMs) maxMs = Mathf.Min(v, 250f);
            float barW = gw / Profiler.HistoryLength;
            int idx = Profiler.HistoryIndex;
            for (int i = 0; i < Profiler.HistoryLength; i++)
            {
                float v = Profiler.History[(idx + i) % Profiler.HistoryLength];
                if (v <= 0f) continue;
                float hgt = Mathf.Clamp01(v / maxMs) * gh;
                Color col = v > 33.4f ? new Color(1f, 0.35f, 0.3f, 0.9f) : v > 16.8f ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(0.5f, 0.95f, 0.5f, 0.9f);
                ui.FillRect(new Rect(box.x + i * barW, box.y, Mathf.Max(1f, barW - 0.5f), hgt), col);
            }
            foreach (var guide in _guides)
            {
                if (guide > maxMs) continue;
                float gy = box.y + guide / maxMs * gh;
                ui.FillRect(new Rect(box.x, gy, gw, 1f), new Color(1f, 1f, 1f, 0.35f));
                ui.DrawText(guide < 20f ? "17" : "33", new Vector2(box.x + 2f, gy + lineH * 0.5f), s * 0.7f, new Color(1f, 1f, 1f, 0.6f));
            }
            ui.DrawText($"FRAME MS (LAST {Profiler.HistoryLength}, SCALE {maxMs:0} MS)", new Vector2(box.x + 2f, box.yMax - 2f), s * 0.7f, new Color(1f, 1f, 1f, 0.7f));
        }
        private static string PadText(string t, int n) => t.Length >= n ? t : t + new string(' ', n - t.Length);
        private static readonly float[] _guides = { 16.7f, 33.3f };
    }

    /// <summary>Scripted input for automated screenshots/tests (feeds PlayerInputActions' override hooks). A plain
    /// screen since 9.0 — updated at frame start, draws nothing — so the gates need no GameObject.</summary>
    public sealed class DemoScript : IUiScreen
    {
        public void DrawUi(UiBatch ui) { }
        public void Close() { }

        public string scriptName = "look";
        /// <summary>The script's frame. Since 9.0 the script is a screen: it acts at frame start, before the scene's fixed
        /// steps (the GameObject script acted after them), so every action lands one fixed step earlier relative to
        /// physics than before 9.0 — the gates were re-baselined on that.</summary>
        private int _frame;
        /// <summary>Where each enemy was first seen, so the probe can tell whether it has ever moved.</summary>
        private readonly System.Collections.Generic.Dictionary<Entity, Vector3> _enemyHome = new System.Collections.Generic.Dictionary<Entity, Vector3>();
        /// <summary>Ammo pickups still standing (entities since 7.5).</summary>
        private readonly System.Collections.Generic.List<Entity> _pickups = new System.Collections.Generic.List<Entity>();
        private int CountPickups()
        {
            RipNRage.Game.Ecs.Pickups.Collect(Application.Current.World, _pickups);
            return _pickups.Count;
        }

        /// <summary>9.1: interned meshes / textures / materials — the same before and after a level rebuild, or it leaks
        /// (the physics props' 13 boxes per build still do, until 9.2).</summary>
        private static void MeshProbe() =>
            Debug.LogWarning($"[test] assets meshes {RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Count} textures {RipNRage.Engine.Ecs.Rendering.RenderAssets.Textures.Count} materials {RipNRage.Engine.Ecs.Rendering.RenderAssets.Materials.Count}");

        /// <summary>9.0 → 9.7: the legacy model is deleted; the line stays so the gates assert it stays deleted.</summary>
        private static void Census(string scene) =>
            Debug.LogWarning($"[test] legacy {scene}: gone (the world is the scene)");

        /// <summary>8.2: the flow singleton and what the menus made of it.</summary>
        private void FlowProbe()
        {
            var f = RipNRage.Game.Ecs.Flows.Current;
            Debug.LogWarning($"[test] frame {_frame} flow {f.Phase} clock {f.DeathClock:0.000} checkpoint {f.CheckpointReached}"
                             + $" timeScale {Time.timeScale:0.00} deathMenu {(DeathScreen.Current != null)}");
        }

        private void LogRage()
        {
            RipNRage.Game.Ecs.Rages.TryGet(out var rage);
            Debug.LogWarning($"[test] frame {_frame} fury {rage.Fury:0.00} calm {rage.Calm:0.00} {rage.State}{(rage.Threat ? " threat" : "")}"
                             + $" speed x{RipNRage.Game.Ecs.Rages.SpeedMultiplier(in rage):0.00} hp {RipNRage.Game.Ecs.PlayerLink.Health:0} armour {RipNRage.Game.Ecs.PlayerLink.Armor:0}");
        }

        private readonly System.Collections.Generic.List<Entity> _enemies = new System.Collections.Generic.List<Entity>();

        // ---- layer 11.0: the navigation gates' geometry.
        // All three build on the dock's open run north of info_player_start (0, 1.61, 8), floor y 0.91, the player
        // teleported to a fixed spot and facing -z. The enemy never *sees* the player (a wall or a drop is in the
        // way): it is told where he is by the **gunshot** at frame 30 — a 30 m noise, and a wall only muffles past
        // half of that (Noises.WallMuffle), so the grunt hears him through it and chases the sound. What it does
        // next is the gate: today it walks the straight line and grinds, because there is no path to follow.

        /// <summary>
        /// 11.0 scaffolding, deleted at 11.1: what a nav bake of this level would cost. Walks the static colliders
        /// for the level's bounds, then samples every column at a cell size by casting down through it and counting
        /// the walkable tops (the bake's step 1-3, minus the clearance erosion). Prints the numbers that set the
        /// worldspawn defaults. Run with `--script navmeasure --frames 20`.
        /// </summary>
        private static void NavMeasure()
        {
            var w = Application.Current.World;
            // the level's extent, from the static colliders the brushes became. A convex brush carries its world
            // vertices, so it measures exactly; the dock's water and sky planes are hundreds of metres across and
            // are *excluded* — they are what makes a naive bounds unusable (see the numbers in LAYER11_NAV.md).
            var q = w.Query().WithReadOnly<RipNRage.Engine.Ecs.Physics.ColliderShape>().WithReadOnly<LocalToWorld>().Build();
            Vector3 lo = new Vector3(1e9f, 1e9f, 1e9f), hi = new Vector3(-1e9f, -1e9f, -1e9f);
            Vector3 allLo = lo, allHi = hi;
            int statics = 0, huge = 0;
            const float HugeExtent = 200f;
            foreach (var chunk in q)
            {
                var shapes = chunk.Get<RipNRage.Engine.Ecs.Physics.ColliderShape>(); var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || !shapes[i].IsStatic) continue;
                    statics++;
                    Vector3 blo, bhi;
                    if (shapes[i].Kind == RipNRage.Engine.Ecs.Physics.ShapeKind.Convex)
                    {
                        var asset = RipNRage.Engine.Ecs.Physics.PhysicsAssets.Hulls.Resolve(shapes[i].Hull);
                        if (asset == null || asset.Vertices.Length == 0) continue;
                        blo = bhi = asset.Vertices[0];           // StaticConvex interns world-space vertices
                        foreach (var v in asset.Vertices) { blo = Vector3.Min(blo, v); bhi = Vector3.Max(bhi, v); }
                    }
                    else
                    {
                        var c = ltw[i].Position + shapes[i].Center;
                        var e = shapes[i].Kind == RipNRage.Engine.Ecs.Physics.ShapeKind.Box ? shapes[i].Size * 0.5f
                              : new Vector3(shapes[i].Radius, Mathf.Max(shapes[i].Radius, shapes[i].Height * 0.5f), shapes[i].Radius);
                        blo = c - e; bhi = c + e;
                    }
                    allLo = Vector3.Min(allLo, blo); allHi = Vector3.Max(allHi, bhi);
                    if (bhi.x - blo.x > HugeExtent || bhi.z - blo.z > HugeExtent) { huge++; continue; }
                    lo = Vector3.Min(lo, blo); hi = Vector3.Max(hi, bhi);
                }
            }
            Debug.LogWarning($"[test] nav bounds all ({allLo.x:0.0} {allLo.y:0.0} {allLo.z:0.0}) .. ({allHi.x:0.0} {allHi.y:0.0} {allHi.z:0.0}) " +
                             $"size {allHi.x - allLo.x:0.0} x {allHi.z - allLo.z:0.0}; statics {statics}, {huge} over {HugeExtent:0} m excluded");
            Debug.LogWarning($"[test] nav bounds played ({lo.x:0.0} {lo.y:0.0} {lo.z:0.0}) .. ({hi.x:0.0} {hi.y:0.0} {hi.z:0.0}) " +
                             $"size {hi.x - lo.x:0.0} x {hi.y - lo.y:0.0} x {hi.z - lo.z:0.0}");
            float tile = LevelLoader.MeshChunkSize;
            Debug.LogWarning($"[test] nav tiles at {tile:0} m: {Mathf.CeilToInt((hi.x - lo.x) / tile)} x {Mathf.CeilToInt((hi.z - lo.z) / tile)}");

            foreach (float cell in new[] { 0.5f, 0.25f })
            {
                int nx = Mathf.CeilToInt((hi.x - lo.x) / cell), nz = Mathf.CeilToInt((hi.z - lo.z) / cell);
                int columns = nx * nz, spans = 0, occupied = 0;
                float top = hi.y + 1f, span = hi.y - lo.y + 2f;
                for (int iz = 0; iz < nz; iz++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        var at = new Vector3(lo.x + (ix + 0.5f) * cell, top, lo.z + (iz + 0.5f) * cell);
                        int here = 0; float y = top;
                        for (int guard = 0; guard < 16; guard++) // every solid top in the column, not just the first
                        {
                            if (!RipNRage.Engine.Physics.Raycast(new Vector3(at.x, y, at.z), Vector3.down, out var hit, y - lo.y + 1f, Noises.WallMask)) break;
                            here++;
                            y = hit.point.y - 0.05f;
                            if (y <= lo.y) break;
                        }
                        spans += here;
                        if (here > 0) occupied++;
                    }
                // NavSpan is 16 bytes, NavColumn 8
                double mb = (spans * 16.0 + columns * 8.0) / (1024 * 1024);
                Debug.LogWarning($"[test] nav cell {cell:0.00} m: {nx} x {nz} = {columns} columns, {occupied} with ground, " +
                                 $"{spans} spans ({(occupied > 0 ? (float)spans / occupied : 0f):0.00} per occupied column), {mb:0.00} MB");
            }
        }

        /// <summary>The dock floor the nav gates build on, and the spot the player is teleported to.</summary>
        private const float NavFloorY = 0.91f, NavPlayerZ = 8f;

        /// <summary>A static wall segment on Walkable, standing on the floor: x from <paramref name="x0"/> to <paramref name="x1"/>, at <paramref name="z"/>.</summary>
        private static void NavWall(float x0, float x1, float z, float height = 4f, float thick = 0.6f) =>
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World,
                new Vector3((x0 + x1) * 0.5f, NavFloorY + height * 0.5f, z), Quaternion.identity,
                new Vector3(x1 - x0, height, thick), Layers.Walkable);

        /// <summary>The grunt the nav gates watch: on the floor (or a platform top), facing +z — towards the player.</summary>
        private static void NavGrunt(float x, float y, float z) =>
            Enemies.Spawn(Application.Current.World, Enemies.KindOf(GameAssets.RiflemanData), new Vector3(x, y, z), 0f);

        /// <summary>
        /// The layer-6 watch line: what the nearest enemy is doing and what it has done to the player, read
        /// straight off the world (the enemy is only an entity since 6.6).
        /// </summary>
        private void AiProbe(string tag)
        {
            if (!RipNRage.Game.Ecs.PlayerLink.Alive) return;
            var w = Application.Current.World;
            Enemies.Collect(w, _enemies);
            var nearest = Entity.Null; float best = float.MaxValue;
            foreach (var en in _enemies)
            {
                var pos = w.Get<LocalTransform>(en).Position;
                if (!_enemyHome.ContainsKey(en)) _enemyHome[en] = pos;
                float d = (pos - RipNRage.Game.Ecs.PlayerLink.Position).sqrMagnitude;
                if (d < best) { best = d; nearest = en; }
            }
            if (nearest.IsNull) { Debug.LogWarning($"[test] {tag}: hp {RipNRage.Game.Ecs.PlayerLink.Health:0} enemy none"); return; }
            var brain = w.Has<AiBrain>(nearest) ? w.Get<AiBrain>(nearest) : default;
            var sense = w.Has<Perception>(nearest) ? w.Get<Perception>(nearest) : default;
            bool spotted = !sense.Target.IsNull;
            var anim = w.Has<EnemyVisual>(nearest) ? w.Get<EnemyVisual>(nearest).State : AnimationState.IDLE;
            float moved = (w.Get<LocalTransform>(nearest).Position - _enemyHome[nearest]).magnitude;
            float ehp = w.Has<Health>(nearest) ? w.Get<Health>(nearest).Current : 0f;
            // 11.0: how far the grunt still is from where it is trying to get to, and the path it is walking.
            // `path`/`corners` are -1 until 11.3 gives it one: a gate that reads -1 is a grunt on the straight line.
            Vector3 self = w.Get<LocalTransform>(nearest).Position;
            Vector3 goal = brain.MoveTo; goal.y = self.y;
            float goalD = brain.State == AiState.Idle ? -1f : (goal - self).magnitude;
            Debug.LogWarning($"[test] {tag}: hp {RipNRage.Game.Ecs.PlayerLink.Health:0} armour {RipNRage.Game.Ecs.PlayerLink.Armor:0} | enemy d {Mathf.Sqrt(best):0.0} moved {moved:0.00} " +
                             $"spotted {spotted} seen {sense.Seen} state {brain.State} anim {anim} ehp {ehp:0} goal d {goalD:0.0} path -1 corners -1");
        }
        /// <summary>killenemy: the frame the target died (0 = still alive) — makes a tuning change visible in the gate.</summary>
        private int _killFrame;
        private float _strafeDir;
        /// <summary>checkpoint: which scene of the loop this is (static: the script is rebuilt with each scene).</summary>
        private static int _checkpointStage;
        /// <summary>saveload: the quick load was pressed (static: the load rebuilds the scene and this script).</summary>
        private static bool _saveLoaded;
        /// <summary>die: 1 once RESTART was pressed (static, as above).</summary>
        private static int _dieStage;
        /// <summary>menu: 1 once NEW GAME was pressed (static, as above).</summary>
        private static int _menuStage;
        /// <summary>flowmoves / deathf8: 1 once the move was accepted (static, as above).</summary>
        private static int _movesStage, _f8Stage;

        /// <summary>8.8 (deferred from 8.0): the title screen → NEW GAME path, under <c>--menu --script menu</c>.
        /// Scene 1 is the title screen (no player); NEW GAME is the selected item, Enter starts the run.</summary>
        private void MenuScript()
        {
            if (_menuStage == 0)
            {
                if (_frame == 40) Census("title");
                if (_frame == 40) Debug.LogWarning($"[test] menu flow={RipNRage.Game.Ecs.Flows.Current.Phase} player={(RipNRage.Game.Ecs.PlayerLink.Alive)} flow {RipNRage.Game.Ecs.Flows.Current.Phase} timeScale {Time.timeScale:0.00}");
                if (_frame == 60) { _menuStage = 1; Input.SimulateKey(KeyCode.Return, true); } // NEW GAME (the fade takes input from frame 28)
            }
            else
            {
                if (_frame == 1) Input.SimulateKey(KeyCode.Return, false);
                if (_frame == 60 && RipNRage.Game.Ecs.PlayerLink.Alive)
                    Debug.LogWarning($"[test] new game flow={RipNRage.Game.Ecs.Flows.Current.Phase} player=True hp={RipNRage.Game.Ecs.PlayerLink.Health} ammo={CombatController.AmmoOf(AmmoType.LIGHT)} flow {RipNRage.Game.Ecs.Flows.Current.Phase} pos {RipNRage.Game.Ecs.PlayerLink.Position}");
            }
        }

        /// <summary>saveload: saves the restored game to "roundtrip" and prints every line that differs from "quick".</summary>
        private static void SaveRoundTrip()
        {
            GameSaveSystem.Save("roundtrip");
            var a = System.IO.File.ReadAllLines(GameSaveSystem.SlotPath("quick"));
            var b = System.IO.File.ReadAllLines(GameSaveSystem.SlotPath("roundtrip"));
            int diffs = 0;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].Trim() : "<none>", y = i < b.Length ? b[i].Trim() : "<none>";
                if (x == y || x.StartsWith("\"time\"") || SameNumber(x, y)) continue;
                diffs++;
                Debug.LogWarning($"[test] round-trip line {i + 1}: saved {x} restored {y}");
            }
            Debug.LogWarning($"[test] round-trip: {a.Length} lines, {diffs} differ (numbers within 0.05)");
        }

        /// <summary>Two `"key": number` lines whose numbers agree within 0.05 (the restored body settles a little).</summary>
        private static bool SameNumber(string x, string y)
        {
            int i = x.IndexOf(':'), j = y.IndexOf(':');
            if (i < 0 || j < 0 || x.Substring(0, i) != y.Substring(0, j)) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return double.TryParse(x.Substring(i + 1).TrimEnd(','), System.Globalization.NumberStyles.Float, ci, out var p)
                && double.TryParse(y.Substring(j + 1).TrimEnd(','), System.Globalization.NumberStyles.Float, ci, out var q)
                && Math.Abs(p - q) <= 0.05;
        }

        public void Update()
        {
            _frame++;
            // the title screen has no player, so the menu script runs before the player-facade guard (8.8)
            if (scriptName == "menu") { MenuScript(); return; }
            if (scriptName == "flowmoves" && _movesStage == 1) { if (_frame == 30) FlowProbe(); return; } // the title scene: no player
            if (!RipNRage.Game.Ecs.PlayerLink.Alive) return;
            RipNRage.Engine.Ecs.Input.InputOverride.Fire = null;
            RipNRage.Engine.Ecs.Input.InputOverride.Move = null;
            RipNRage.Engine.Ecs.Input.InputOverride.Look = null;
            switch (scriptName)
            {
                case "graphicsmenu":
                    // UI capture: opens the graphics screen and walks the selection down into the scrolling rows
                    if (_frame == 10) GraphicsMenu.Open(null);
                    if (_frame > 20 && _frame < 60 && _frame % 4 == 0) Input.SimulateKey(KeyCode.DownArrow, true);
                    else Input.SimulateKey(KeyCode.DownArrow, false);
                    break;
                case "look":
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = new Vector2(2f, 0f);
                    break;
                case "crates":
                    // walk to the crate pile and look at it
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 95 ? new Vector2(-0.2f, 1f) : Vector2.zero;
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame > 95 && _frame < 115 ? new Vector2(14f, -3f) : Vector2.zero;
                    break;
                case "sunward":
                    // walk down the pier, then turn around to face the sun through the lamp posts
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 270 ? new Vector2(0f, 1f) : Vector2.zero;
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame > 270 && _frame < 310 ? new Vector2(-36f, 1.0f) : Vector2.zero;
                    break;
                case "water":
                    // look over the railing at the water, then walk off the edge into it: the sky goes while the
                    // view is under (7.6; the 7.0 baseline recorded "sky True" all the way down — the old trigger
                    // waited for a camera GameObject that has been an entity since layer 4)
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 45 ? new Vector2(1f, 0f) : _frame > 100 ? new Vector2(1f, 0f) : Vector2.zero;
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame > 45 && _frame < 76 ? new Vector2(30f, -6f) : Vector2.zero;
                    if (_frame % 15 == 0 && _frame >= 90)
                    {
                        var ww = Application.Current.World;
                        int volumes = 0;
                        foreach (var ch in ww.Query().WithReadOnly<RipNRage.Game.Ecs.WaterVolume>().Build()) volumes += ch.Length;
                        Debug.LogWarning($"[test] frame {_frame} y {RipNRage.Game.Ecs.PlayerCamera.Position.y:0.00} sky {RenderSettings.drawSky}"
                                         + $" under {ww.GetSingleton<RipNRage.Game.Ecs.Underwater>().Under} water volumes {volumes}");
                    }
                    break;
                case "surface":
                    // what a bullet would kick up: rays from the eye at the level around the player start. Until 7.7
                    // every one of these answered "default" — the resolver was stubbed when physics went ECS (layer 2)
                    if (_frame == 20)
                    {
                        var eye = RipNRage.Game.Ecs.PlayerCamera.Position;
                        var lampPost = new Vector3(-13.5f, 2.5f, -28f); // dock.map's first lamp post, mid-pole
                        foreach (var (label, from, dir) in new[]
                        {
                            ("down", eye, Vector3.down), ("ahead", eye, new Vector3(0f, -0.15f, -1f)), ("left", eye, new Vector3(-1f, -0.1f, 0f)),
                            ("behind", eye, new Vector3(0f, -0.05f, 1f)), ("lamp post", eye, lampPost - eye),
                            ("off the dock, down", new Vector3(60f, 5f, 0f), Vector3.down), // through the water to the sand floor
                        })
                        {
                            bool hitSomething = RipNRage.Engine.Physics.Raycast(from, dir.normalized, out var hit, 120f, Weapons.HitMask);
                            Debug.LogWarning(hitSomething
                                ? $"[test] surface {label}: {SurfaceResolver.Resolve(hit)} at {hit.distance:0.0} m"
                                : $"[test] surface {label}: nothing within 120 m");
                        }
                    }
                    break;
                case "pickup":
                {
                    // the dock's shell-ammo pickup sits at (-3, 0.91, -20), thirty metres down the planks: stand
                    // the player four metres short of it and walk in. The ammo must go up and the pickup must be
                    // gone (an entity since 7.5; the 7.0 baseline was the AmmoPickup GameObject: shells 64 -> 80, 4 -> 3)
                    if (_frame == 10) RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(-3f, 1.8f, -16f), 180f);
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame > 12 && _frame < 70 ? new Vector2(0f, 1f) : Vector2.zero;
                    if (_frame % 20 == 0)
                    {
                        int shells = CombatController.AmmoOf(AmmoType.SHELL);
                        var pos = RipNRage.Game.Ecs.PlayerLink.Alive ? RipNRage.Game.Ecs.PlayerLink.Position : Vector3.zero;
                        Debug.LogWarning($"[test] frame {_frame} at ({pos.x:0.0}, {pos.z:0.0}) shells {shells} pickups {CountPickups()}");
                    }
                    break;
                }
                case "killenemy":
                {
                    // spawn a rifleman close by, aim at it, kill it with the MG, then watch the hit flash + dissolve
                    var world = Application.Current.World;
                    if (_frame == 2) Enemies.Spawn(world, Enemies.KindOf(GameAssets.RiflemanData), new Vector3(-1.5f, 0.91f, -6f), 0f);
                    if (_frame == 5) CombatController.OnChangeWeapon1();
                    var camPos = RipNRage.Game.Ecs.PlayerCamera.Position; var camFwd = RipNRage.Game.Ecs.PlayerCamera.Forward;
                    Enemies.Collect(world, _enemies);
                    var nearest = Entity.Null; float best = float.MaxValue;
                    foreach (var en in _enemies)
                    {
                        float d = (world.Get<LocalTransform>(en).Position - camPos).sqrMagnitude;
                        if (d < best) { best = d; nearest = en; }
                    }
                    if (!nearest.IsNull)
                    {
                        Vector3 to = world.Get<LocalTransform>(nearest).Position + Vector3.up * 0.3f - camPos;
                        float yawErr = Vector3.SignedAngle(new Vector3(camFwd.x, 0, camFwd.z), new Vector3(to.x, 0, to.z), Vector3.up);
                        float pitchNow = -Mathf.Asin(Mathf.Clamp(camFwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float pitchWanted = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                        // FPSCameraController: degrees = pixels * sensitivity(5) * fixedDeltaTime(0.02) = 0.1 deg/px
                        RipNRage.Engine.Ecs.Input.InputOverride.Look = new Vector2(Mathf.Clamp(yawErr * 10f, -60f, 60f), Mathf.Clamp((pitchNow - pitchWanted) * 10f, -60f, 60f));
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame >= 60 && _frame < 240; // many Glock hits: the grunt has 1000 hp + 1000 armour
                    // the gate: without this the script only proves the game does not crash while firing at something.
                    // Layer 5 has to turn "ALIVE 1000/1000" into a dead grunt.
                    bool dead = !nearest.IsNull && (world.Has<Dead>(nearest) || world.Get<Health>(nearest).Current <= 0f);
                    if (dead && _killFrame == 0) _killFrame = _frame;
                    if (_frame == 240 || _frame == 290)
                        Debug.LogWarning(nearest.IsNull
                            ? $"[test] frame {_frame}: no enemy in the scene"
                            : $"[test] frame {_frame}: enemy {(dead ? "DEAD" : "ALIVE")} health {world.Get<Health>(nearest).Current:0} " +
                              $"armour {(world.Has<Armor>(nearest) ? world.Get<Armor>(nearest).Current : 0f):0} killedAtFrame {_killFrame}");
                    break;
                }
                case "spin":
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = new Vector2(40f, 0f);
                    break;
                case "walk":
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(0f, 1f);
                    if (_frame % 90 == 0) RipNRage.Engine.Ecs.Input.InputOverride.JumpOnce();
                    if (_frame == 30) Census("level");
                    break;
                case "fire":
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(0f, 0.5f);
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame > 30 && (_frame / 20) % 2 == 0;
                    break;
                case "weapons":
                    if (_frame == 40) CombatController.OnChangeWeapon2();
                    if (_frame == 80) CombatController.OnChangeWeapon3();
                    if (_frame == 120) CombatController.OnChangeWeapon4();
                    if (_frame == 160) CombatController.OnChangeWeapon1();
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame % 40 > 30;
                    break;
                case "jab":
                    if (_frame == 30 || _frame == 45) CombatController.OnAltFire(true);
                    if (_frame == 32 || _frame == 47 || _frame == 120)
                    {
                        var w = Application.Current.World;
                        var pe = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;
                        if (w.IsAlive(pe) && w.Has<RipNRage.Game.Ecs.Combat.AltFire>(pe))
                            Debug.LogWarning($"[test] frame {_frame}: combo step {w.Get<RipNRage.Game.Ecs.Combat.AltFire>(pe).Step} " +
                                             $"holstered {w.Get<RipNRage.Game.Ecs.Combat.Weapon>(pe).Holstered}");
                    }
                    break;
                case "m1887":
                    if (_frame == 10) CombatController.OnChangeWeapon2();
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame == 30;
                    break;
                case "glockfire":
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame == 30;
                    break;
                case "peek":
                    // turn towards the nearby rifleman and watch it
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame < 40 ? new Vector2(1.2f, 0f) : Vector2.zero;
                    if (_frame == 60 || _frame == 150 || _frame == 290) AiProbe($"peek {_frame}");
                    break;
                case "close":
                    // walk up to the rifleman standing near the spawn
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 55 ? new Vector2(-0.25f, 1f) : Vector2.zero;
                    if (_frame == 60 || _frame == 150 || _frame == 290) AiProbe($"close {_frame}");
                    break;
                case "navmeasure":
                    // 11.0 scaffolding (not in the sweep): sizes the bake. Deleted at 11.1.
                    if (_frame == 5) NavMeasure();
                    break;
                case "maze":
                    // a wall across the run, the grunt behind it. It hears the shot, chases the sound and meets
                    // 12 m of wall with a 6 m way round either end. Today: it grinds on the wall and never arrives.
                    if (_frame == 2)
                    {
                        RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(0f, 1.8f, NavPlayerZ), 180f);
                        NavWall(-6f, 6f, NavPlayerZ - 6f);
                        NavGrunt(0f, NavFloorY, NavPlayerZ - 12f);
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame % 60 == 30; // a shot a second: the chase never lapses on memory
                    if (_frame == 60 || _frame == 180 || _frame == 300 || _frame == 390) AiProbe($"maze {_frame}");
                    break;
                case "door":
                    // the same wall with a 1.4 m gap, and a door hung in the gap — spawned swung open, so the way
                    // through exists. The straight line still runs into the left segment. 11.6 shuts it mid-path.
                    if (_frame == 2)
                    {
                        RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(0f, 1.8f, NavPlayerZ), 180f);
                        NavWall(-6f, 0.7f, NavPlayerZ - 6f);
                        NavWall(2.1f, 6f, NavPlayerZ - 6f);
                        // hinged on its left edge at the gap's left post, swung 90 deg out of the doorway
                        GamePrefabs.Library.Get("phys_door").Spawn(Application.Current.World,
                            new Vector3(0.7f, NavFloorY, NavPlayerZ - 6f), Quaternion.Euler(0f, 90f, 0f));
                        NavGrunt(0f, NavFloorY, NavPlayerZ - 12f);
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame % 60 == 30; // a shot a second: the chase never lapses on memory
                    if (_frame == 60 || _frame == 180 || _frame == 300 || _frame == 390) AiProbe($"door {_frame}");
                    break;
                case "ledge":
                    // the grunt starts on a 3.5 m platform; the player is on the floor below. The drop is further
                    // than the ledge guard's reach (1.125 + 0.35 + 3 = 4.475 m under the body centre), so today the
                    // grunt hears the shot, turns, and stands at the edge for the rest of the run. The platform is
                    // 18 m off so the player is never *seen* (sight is 10 m): the grunt has to come down to him.
                    if (_frame == 2)
                    {
                        RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(0f, 1.8f, NavPlayerZ), 180f);
                        RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World,
                            new Vector3(0f, NavFloorY + 1.75f, NavPlayerZ - 18f), Quaternion.identity,
                            new Vector3(8f, 3.5f, 8f), Layers.Walkable);
                        NavGrunt(0f, NavFloorY + 3.5f, NavPlayerZ - 18f);
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame % 60 == 30; // a shot a second: the chase never lapses on memory
                    if (_frame == 60 || _frame == 180 || _frame == 300 || _frame == 390) AiProbe($"ledge {_frame}");
                    break;
                case "evade":
                    // a grunt spawns in front and engages; the player runs at it, then dashes across its line of
                    // fire: being shot at and moving fills CALM (nothing here fills FURY — the player never shoots)
                    if (_frame == 2) Enemies.Spawn(Application.Current.World, Enemies.KindOf(GameAssets.RiflemanData),
                        RipNRage.Game.Ecs.PlayerCamera.Position + RipNRage.Game.Ecs.PlayerCamera.Forward * 14f - Vector3.up * 1.3f, 180f);
                    // weave in front of the grunt instead of running off the dock: strafe one way, then the other
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 120 ? new Vector2(0f, 1f)
                                       : _frame < 330 ? new Vector2(_frame % 60 < 30 ? 0.9f : -0.9f, 0f) // weave on the spot
                                       : Vector2.zero;
                    if (_frame > 140 && _frame % 40 == 0) RipNRage.Engine.Ecs.Input.InputOverride.Dash = true;
                    else if (_frame % 40 == 1) RipNRage.Engine.Ecs.Input.InputOverride.Dash = null;
                    // frame 240: spend the Calm the dodging earned — DEPRESSED halves what lands, regrows armour
                    // and shortens the enemies' sight (the probe prints hp / armour and whether they still have him)
                    if (_frame == 240) RipNRage.Engine.Ecs.Input.InputOverride.Calm = true;
                    else if (_frame == 241) RipNRage.Engine.Ecs.Input.InputOverride.Calm = null;
                    if (_frame % 30 == 0) LogRage();
                    break;
                case "approach":
                    // run towards the first rifleman, then stand and shoot
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 270 ? new Vector2(0f, 1f) : Vector2.zero;
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame > 300 && _frame % 30 == 0;
                    // 365: the first shot is at 330 and its sound carries 30 m in 23 frames (run with --frames 400)
                    if (_frame == 100 || _frame == 200 || _frame == 290 || _frame == 365) AiProbe($"approach {_frame}");
                    break;
                case "rocket":
                    if (_frame == 10) CombatController.OnChangeWeapon4();
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame > 20 && _frame < 26 ? new Vector2(0f, -60f) : Vector2.zero; // aim down at the dock
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame == 40;
                    if (_frame == 90) Debug.LogWarning($"[test] rocket aftermath: hp {RipNRage.Game.Ecs.PlayerLink.Health:0} armour {RipNRage.Game.Ecs.PlayerLink.Armor:0} y {RipNRage.Game.Ecs.PlayerLink.Position.y:0.00}");
                    break;
                case "rage":
                    // two grunts killed with the MG fill the Fury meter; at frame 310 the player presses the
                    // activation (Q / D-pad left) and spends it on a third grunt: ENRAGED hits harder and the
                    // blows feed the player (the probe prints hp / armour), then the meter drains and it ends
                    if (_frame == 2 || _frame == 150 || _frame == 320) Enemies.Spawn(Application.Current.World, Enemies.KindOf(GameAssets.RiflemanData),
                        RipNRage.Game.Ecs.PlayerCamera.Position + RipNRage.Game.Ecs.PlayerCamera.Forward * 6f - Vector3.up * 1.3f, 0f);
                    if (_frame == 5) CombatController.OnChangeWeapon3();
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = (_frame > 20 && _frame < 140) || (_frame > 160 && _frame < 300) || (_frame > 330 && _frame < 395);
                    if (_frame == 310) RipNRage.Engine.Ecs.Input.InputOverride.Fury = true;
                    else if (_frame == 311) RipNRage.Engine.Ecs.Input.InputOverride.Fury = null;
                    if (_frame % 30 == 0) LogRage();
                    break;
                case "saveload":
                    // walk, save (F6), walk more, load (F9): the player must be back at the saved spot with the saved ammo
                    if (_saveLoaded) // the scene the load rebuilt: stand still, check the round trip, stop
                    {
                        if (_frame == 1) Input.SimulateKey(KeyCode.F9, false);
                        if (_frame == 30) SaveRoundTrip();
                        break;
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = (_frame < 100 || (_frame > 120 && _frame < 200)) ? new Vector2(0f, 1f) : Vector2.zero;
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = _frame > 60 && _frame < 66;
                    if (_frame == 110) { Input.SimulateKey(KeyCode.F6, true); Debug.LogWarning($"[test] saved at {RipNRage.Game.Ecs.PlayerLink.Position} ammo {CombatController.AmmoOf(AmmoType.LIGHT)}"); }
                    if (_frame == 111) Input.SimulateKey(KeyCode.F6, false);
                    if (_frame == 205) Debug.LogWarning($"[test] before load at {RipNRage.Game.Ecs.PlayerLink.Position}");
                    if (_frame == 210) { _saveLoaded = true; Input.SimulateKey(KeyCode.F9, true); }
                    if (_frame % 20 == 0 && RipNRage.Game.Ecs.PlayerLink.Alive) LogRage();
                    if (_frame == 240 && RipNRage.Game.Ecs.PlayerLink.Alive) Debug.LogWarning($"[test] after load at {RipNRage.Game.Ecs.PlayerLink.Position} ammo {CombatController.AmmoOf(AmmoType.LIGHT)} hp {RipNRage.Game.Ecs.PlayerLink.Health}");
                    // 8.0: the whole save, not three fields. The load rebuilds the scene and this script restarts its
                    // count there (so the frame-240 line above never prints): 30 frames into the rebuilt scene, save
                    // again and diff the two files line by line, the wall-clock stamp aside.
                    break;
                case "glockalt":
                    // Three right clicks: jab, jab, then the Glock grapple finisher — which needs a victim within
                    // 1 m, and this level has none in reach, so the gate pins the refusal: no finisher entity, the
                    // combo reset, no rounds spent. The grapple itself is covered in vitro (GameChecks).
                    if (_frame == 30 || _frame == 40 || _frame == 50) CombatController.OnAltFire(true);
                    if (_frame == 55)
                    {
                        var w = Application.Current.World;
                        var pe = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;
                        var combo = w.Get<RipNRage.Game.Ecs.Combat.AltFire>(pe);
                        Debug.LogWarning($"[test] after 3 presses: step {combo.Step} finisher {(combo.Finisher.IsNull ? "none" : "live")} ammo {CombatController.AmmoOf(AmmoType.LIGHT)}");
                    }
                    break;
                case "die":
                    // the death loop: walk, take lethal damage at frame 40, watch the camera fall and the
                    // "You are dead" screen fade in, pick RESTART at frame 260 and confirm the respawn
                    // 8.0: RESTART rebuilds the scene and this script with it, so the frame-300 line below never printed
                    // and the new scene died again at its frame 40; the stage is static now and the respawned scene
                    // only reports
                    if (_dieStage == 1)
                    {
                        if (_frame == 1) Input.SimulateKey(KeyCode.Return, false);
                        if (_frame == 40) FlowProbe();
                        if (_frame == 40) Debug.LogWarning($"[test] after respawn: player={(RipNRage.Game.Ecs.PlayerLink.Alive)} dead={RipNRage.Game.Ecs.PlayerLink.IsDead} hp={RipNRage.Game.Ecs.PlayerLink.Health} deathEvents={RipNRage.Game.Ecs.PlayerDeathSystem.Deaths} timeScale={Time.timeScale:0.00} deathScreen={(DeathScreen.Current != null)}");
                        break;
                    }
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 40 ? new Vector2(0f, 1f) : Vector2.zero;
                    if (_frame == 40 && RipNRage.Game.Ecs.PlayerLink.Alive) RipNRage.Game.Ecs.PlayerLink.TakeDamage(10000f);
                    if (_frame == 120 && RipNRage.Game.Ecs.PlayerLink.Alive) Debug.LogWarning($"[test] dead={RipNRage.Game.Ecs.PlayerLink.IsDead} deathEvents={RipNRage.Game.Ecs.PlayerDeathSystem.Deaths} hp={RipNRage.Game.Ecs.PlayerLink.Health} timeScale={Time.timeScale:0.00} deathScreen={(DeathScreen.Current != null)}");
                    // 8.2: the flow through the death — Dying at once, Dead 2.4 real seconds (144 frames) after it
                    if (_frame == 41 || _frame == 120 || _frame == 183 || _frame == 185 || _frame == 259) FlowProbe();
                    if (_frame == 260) { _dieStage = 1; Input.SimulateKey(KeyCode.Return, true); }
                    if (_frame == 300) Debug.LogWarning($"[test] after respawn: player={(RipNRage.Game.Ecs.PlayerLink.Alive)} dead={RipNRage.Game.Ecs.PlayerLink.IsDead} hp={RipNRage.Game.Ecs.PlayerLink.Health} timeScale={Time.timeScale:0.00} deathScreen={(DeathScreen.Current != null)}");
                    break;
                case "vfx":
                {
                    // data-driven effects: shoot the floor / wall with every weapon, then a rocket; prints particle stats
                    if (_frame == 5) CombatController.OnChangeWeapon1();
                    if (_frame == 60) CombatController.OnChangeWeapon2();
                    if (_frame == 110) CombatController.OnChangeWeapon3();
                    if (_frame == 170) CombatController.OnChangeWeapon4();
                    RipNRage.Engine.Ecs.Input.InputOverride.Look = _frame > 10 && _frame < 16 ? new Vector2(0f, -40f) : Vector2.zero; // aim down at the planks
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = (_frame >= 20 && _frame < 24) || (_frame >= 70 && _frame < 72) || (_frame >= 120 && _frame < 150) || _frame == 180;
                    if (_frame % 30 == 0) Debug.LogWarning($"[test] vfx instances={RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveInstances} particles={RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveParticles} effects={RipNRage.Engine.Graphics.Particles.VfxLibrary.Effects.Count}");
                    break;
                }
                case "dash":
                {
                    // dash forward at frame 40 and again in the air after a jump at frame 120
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(0f, 1f);
                    if (_frame == 40 || _frame == 125) Input.SimulateKey(KeyCode.LeftShift, true);
                    if (_frame == 41 || _frame == 126) Input.SimulateKey(KeyCode.LeftShift, false);
                    if (_frame == 115) RipNRage.Engine.Ecs.Input.InputOverride.JumpOnce();
                    if (_frame % 10 == 0 && RipNRage.Game.Ecs.PlayerLink.HasMotor) Debug.LogWarning($"[test] frame {_frame} dashing={RipNRage.Game.Ecs.PlayerLink.Motor.IsDashing} pos {RipNRage.Game.Ecs.PlayerLink.Position}");
                    break;
                }
                case "slide":
                {
                    // run forward, slide at frame 50, jump out of it at frame 80; logs the controller state
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(0f, 1f);
                    if (_frame == 50) Input.SimulateKey(KeyCode.LeftControl, true);
                    if (_frame == 80) { Input.SimulateKey(KeyCode.LeftControl, false); Input.SimulateKey(KeyCode.Space, true); }
                    if (_frame == 81) Input.SimulateKey(KeyCode.Space, false);
                    if (_frame % 10 == 0 && RipNRage.Game.Ecs.PlayerLink.HasMotor) Debug.LogWarning($"[test] frame {_frame} sliding={RipNRage.Game.Ecs.PlayerLink.Motor.IsSliding} wallrun={RipNRage.Game.Ecs.PlayerLink.Motor.IsWallRunning} pos {RipNRage.Game.Ecs.PlayerLink.Position} speed {RipNRage.Game.Ecs.PlayerLink.Motor.CurrentSpeed:0.0}");
                    break;
                }
                case "style":
                {
                    // (the style meter is gone; the script is kept as the Fury-gain probe) a grunt killed with the
                    // Glock, then one with the MG: hits, kills and the close-range bonus fill the meter
                    if (_frame == 2 || _frame == 130) Enemies.Spawn(Application.Current.World, Enemies.KindOf(GameAssets.RiflemanData),
                        RipNRage.Game.Ecs.PlayerCamera.Position + RipNRage.Game.Ecs.PlayerCamera.Forward * 6f - Vector3.up * 1.3f, 0f);
                    if (_frame == 5) CombatController.OnChangeWeapon1();
                    if (_frame == 135) CombatController.OnChangeWeapon3();
                    RipNRage.Engine.Ecs.Input.InputOverride.Fire = (_frame > 20 && _frame < 120) || (_frame > 140 && _frame < 200);
                    if (_frame % 30 == 0) LogRage();
                    break;
                }
                case "fall":
                {
                    // walk sideways off the dock into the water: the fall must be classified as EDGE and then either kill
                    // the player (falls are always lethal since 8.4)
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame < 150 ? new Vector2(1f, 0f) : Vector2.zero;
                    var fr = RipNRage.Game.Ecs.FallSystem.Current;
                    if (_frame % 30 == 0 && RipNRage.Game.Ecs.PlayerLink.Alive) Debug.LogWarning($"[test] frame {_frame} pos {RipNRage.Game.Ecs.PlayerLink.Position} falls={fr.Count} last={RipNRage.Game.Ecs.Falls.Label(fr.LastKind)} dead={RipNRage.Game.Ecs.PlayerLink.IsDead} deathEvents={RipNRage.Game.Ecs.PlayerDeathSystem.Deaths} killY={Application.Current.World.GetSingleton<RipNRage.Engine.Ecs.Levels.Worldspawn>().KillY:0.0}");
                    break;
                }
                case "strafe":
                {
                    // strafe left and right across the middle of the dock (never near an edge), with jumps: a player that
                    // ends up below the planks here went THROUGH them. Prints PASS/FAIL at the end.
                    if (!RipNRage.Game.Ecs.PlayerLink.Alive) break;
                    // always steer back towards x = 0: with the controller's input smoothing that oscillates a few metres
                    // either side of the centre line (the dock is 32 m wide), on the ground and through bunny hops.
                    // The correction is in world space but the move input is player-relative, so it has to be rotated
                    // into the view's frame — the player spawns facing -z (info_player_start angle 180), where a raw
                    // world-space (+1, ...) means "strafe towards -x" and the correction pushed the player further
                    // off centre every frame instead of back. That, not a collision bug, is why this script walked
                    // off the dock edge at x ≈ -19 on every platform.
                    float x = RipNRage.Game.Ecs.PlayerLink.Position.x;
                    _strafeDir = x > 0f ? -1f : 1f;
                    // the sideways correction is world-space, the forward/back wiggle is player-relative, so only
                    // the first is rotated into the view's frame
                    Vector3 localWish = Quaternion.Euler(0f, -(RipNRage.Game.Ecs.PlayerLink.Yaw), 0f) * new Vector3(_strafeDir, 0f, 0f);
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(localWish.x, localWish.z + (_frame % 90 < 45 ? 0.4f : -0.4f));
                    if (_frame % 70 == 35 && Mathf.Abs(x) < 2f) RipNRage.Engine.Ecs.Input.InputOverride.JumpOnce();
                    var fr = RipNRage.Game.Ecs.FallSystem.Current;
                    if (_frame % 30 == 0) Debug.LogWarning($"[test] frame {_frame} pos {RipNRage.Game.Ecs.PlayerLink.Position} falls={fr.Count} last={RipNRage.Game.Ecs.Falls.Label(fr.LastKind)}");
                    if (_frame == 600)
                    {
                        bool ok = fr.Count == 0 && !RipNRage.Game.Ecs.PlayerLink.IsDead;
                        Debug.LogWarning(ok ? "[test] strafe PASS: 600 frames of strafing and jumping on the dock, no fall"
                                            : $"[test] strafe FAIL: falls={fr.Count} ({RipNRage.Game.Ecs.Falls.Label(fr.LastKind)}) dead={RipNRage.Game.Ecs.PlayerLink.IsDead} (the fall report is in the log)");
                    }
                    break;
                }
                case "checkpoint":
                {
                    // 8.0: the checkpoint loop across scenes (this script restarts with every scene build, so the stage
                    // is static). Scene 1: walk into the dock's checkpoint, die at frame 90, RESTART at 260. Scene 2
                    // (the checkpoint load): report the pose, the state and the DeathEvent count.
                    if (_checkpointStage == 1 && _frame == 1) Input.SimulateKey(KeyCode.Return, false);
                    if (!RipNRage.Game.Ecs.PlayerLink.Alive) break;
                    if (_checkpointStage == 0)
                    {
                        if (_frame == 10) RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(0f, 1.8f, -46f), 180f);
                        RipNRage.Engine.Ecs.Input.InputOverride.Move = _frame > 12 && _frame < 60 ? new Vector2(0f, 1f) : Vector2.zero;
                        if (_frame == 100) Debug.LogWarning($"[test] checkpoint reached={RipNRage.Game.Ecs.Flows.Current.CheckpointReached} available={GameSaveSystem.CheckpointAvailable()} pos {RipNRage.Game.Ecs.PlayerLink.Position}");
                        if (_frame == 90) RipNRage.Game.Ecs.PlayerLink.TakeDamage(10000f); // RESTART at 260: the death menu takes input after 2.4 s
                        if (_frame == 200) Debug.LogWarning($"[test] dead={RipNRage.Game.Ecs.PlayerLink.IsDead} deathEvents={RipNRage.Game.Ecs.PlayerDeathSystem.Deaths} canRespawnAtCheckpoint={GameSession.CanRespawnAtCheckpoint}");
                        if (_frame == 260) { _checkpointStage = 1; Input.SimulateKey(KeyCode.Return, true); }
                    }
                    else
                    {
                        if (_frame == 60) Debug.LogWarning($"[test] respawned at {RipNRage.Game.Ecs.PlayerLink.Position} dead={RipNRage.Game.Ecs.PlayerLink.IsDead} hp={RipNRage.Game.Ecs.PlayerLink.Health} deathEvents={RipNRage.Game.Ecs.PlayerDeathSystem.Deaths} reached={RipNRage.Game.Ecs.Flows.Current.CheckpointReached} timeScale={Time.timeScale:0.00}");
                    }
                    break;
                }
                case "flowmoves":
                    // 8.8b: a move refused while playing, then pause → MAIN MENU with a second choice in the same frame
                    // (refused: the first wins), carried out at the next frame start → the title scene (stage 1, above)
                    if (_frame == 30) { RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.MainMenu); }
                    if (_frame == 32) FlowProbe(); // still Playing, nothing pending
                    if (_frame == 60) Input.SimulateKey(KeyCode.Escape, true);
                    if (_frame == 61) Input.SimulateKey(KeyCode.Escape, false);
                    if (_frame == 90)
                    {
                        _movesStage = 1;
                        RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.MainMenu);
                        RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.Restart);
                    }
                    if (_frame == 90) FlowProbe(); // asked, not yet decided (end of frame)
                    break;
                case "deathf8":
                    // 8.8b: the developer keys ask the flow too, and restart even mid-death
                    if (_f8Stage == 1)
                    {
                        if (_frame == 1) Input.SimulateKey(KeyCode.F8, false);
                        if (_frame == 30) { FlowProbe(); Debug.LogWarning($"[test] after F8: hp={RipNRage.Game.Ecs.PlayerLink.Health} pos {RipNRage.Game.Ecs.PlayerLink.Position}"); }
                        if (_frame == 30) MeshProbe(); // 9.1: the rebuilt level reuses its meshes
                        break;
                    }
                    if (_frame == 20 && RipNRage.Game.Ecs.PlayerLink.Alive) RipNRage.Game.Ecs.PlayerLink.TakeDamage(10000f);
                    if (_frame == 40) { FlowProbe(); MeshProbe(); } // Dying
                    if (_frame == 50) { _f8Stage = 1; Input.SimulateKey(KeyCode.F8, true); }
                    break;
                case "pause":
                    // Esc opens the pause menu (world frozen), Esc again resumes
                    RipNRage.Engine.Ecs.Input.InputOverride.Move = new Vector2(0f, 1f);
                    if (_frame == 60) Input.SimulateKey(KeyCode.Escape, true);
                    if (_frame == 61) Input.SimulateKey(KeyCode.Escape, false);
                    if (_frame == 60 || _frame == 61 || _frame == 151 || _frame == 152) FlowProbe(); // the press, and the frame the menu follows
                    if (_frame == 90) Debug.LogWarning($"[test] paused={RipNRage.Game.Ecs.Flows.Current.Phase == RipNRage.Game.Ecs.FlowPhase.Paused} timeScale={Time.timeScale:0.00}");
                    if (_frame == 150) Input.SimulateKey(KeyCode.Escape, true);
                    if (_frame == 151) Input.SimulateKey(KeyCode.Escape, false);
                    if (_frame == 180) Debug.LogWarning($"[test] paused={RipNRage.Game.Ecs.Flows.Current.Phase == RipNRage.Game.Ecs.FlowPhase.Paused} timeScale={Time.timeScale:0.00}");
                    break;
            }
        }
    }
}
