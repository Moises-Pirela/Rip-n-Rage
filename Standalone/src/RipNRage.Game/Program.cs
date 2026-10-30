using System;
using RipNRage.Game.Content;

namespace RipNRage.Game
{
    /// <summary>
    /// Standalone Rip-n-Rage entry point (no Unity). Creates the engine application, builds the
    /// TestArea scene and runs the main loop.
    ///
    /// Options:
    ///   --assets &lt;dir&gt;        the assets folder, or a folder containing it (auto-detected otherwise)
    ///   --width W --height H   window size (default 1280x720)
    ///   --no-vsync             disable vertical sync
    ///   --mute                 disable audio
    ///   --verbose              print Debug.Log output
    ///   --frames N             run N frames then exit (automation)
    ///   --screenshot path.png  save a screenshot on exit (or every --shot-interval frames; "{n}" = frame number)
    ///   --fixed-dt seconds     deterministic frame delta for automation
    ///   --script name          automation script: "look", "fire", "walk", "weapons" (see DemoScript)
    ///   --tunables path.cfg    post-processing tunables file (default: tunables.cfg found in cwd/exe dir/parents)
    ///   --no-postfx            start with post-processing disabled (F4 toggles it)
    ///   --shaders dir          folder with the .glsl shader files (default: "shaders" found in cwd/exe dir/parents)
    ///   --level file.map       play a level file (default: levels/dock.map if found; --no-level = built-in arena)
    ///   --export-dock out.map  write the built-in dock arena as a .map file and exit
    ///   --load slot            load a saved game (slots live in <persistentDataPath>/saves; "quick", "checkpoint", ...)
    ///   --menu / --no-menu     start on the title screen / straight in the level (default: the title screen, unless an
    ///                          automation option — --script, --frames, --load — is given)
    ///   --vfx dir              folder with the particle effect JSON files (default: "vfx" found in cwd / exe dir / parents)
    ///   --perf-log S           print a profiler summary (real frame time, stages, GC) every S seconds
    ///   --perf-csv file.csv    write one row per frame with the frame / stage times
    ///   --profile              glFinish after every GPU stage so CPU stage times include the GPU work (RIPNRAGE_PROFILE=1)
    ///   --gpu auto|high|software  GPU preference: high = discrete GPU on hybrid systems (Linux PRIME offload), software = accept a CPU rasterizer silently
    ///   --gpu-info             print the OpenGL renderer / vendor / version and whether it is a software rasterizer, then exit (code 3 if it is)
    ///   --fall-mode die|respawn  what falling off the level does: lethal (default; the death screen, RESTART = checkpoint / level start)
    ///                          or an arcade respawn on a spot stood on before the fall (a level's worldspawn "fall_mode" overrides this)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions();
            string script = null;
            string level = null;
            bool noLevel = false;
            string exportDock = null, exportAnims = null;
            string loadSlot = null;
            bool? startInMenu = null;
            bool gpuInfoOnly = false;
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
                    case "--mute": options.Mute = true; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--shot-interval": options.ScreenshotInterval = int.Parse(Next()); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--script": script = Next(); break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--shaders": options.ShaderDir = Next(); break;
                    case "--level": level = Next(); break;
                    case "--no-level": noLevel = true; break;
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
                    case "--fall-mode": FallRespawn.Mode = Next().ToLowerInvariant() == "respawn" ? FallMode.Respawn : FallMode.Die; break;
                    case "--no-menu": startInMenu = false; break;
                    case "--animations": RipNRage.Game.Content.GameAssets.AnimationsPath = Next(); break;
                    case "--vfx": RipNRage.Game.Content.GameAssets.VfxPath = Next(); break;
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
                using var app = new Application(options);
                if (gpuInfoOnly)
                {
                    Console.WriteLine(RipNRage.Engine.Platform.GpuInfo.Summary);
                    if (RipNRage.Engine.Platform.GpuInfo.Remedies.Length > 0) Console.WriteLine(RipNRage.Engine.Platform.GpuInfo.Remedies);
                    Console.WriteLine($"timer queries: {(RipNRage.Engine.Platform.GL.HasTimerQueries ? "yes" : "no")}   occlusion queries: {(RipNRage.Engine.Graphics.Culling.GpuOcclusion.Available ? "yes" : "no")}");
                    return RipNRage.Engine.Platform.GpuInfo.IsSoftware ? 3 : 0;
                }
                if (exportDock != null) { RipNRage.Game.Content.Level.DockLevel.Export(exportDock); return 0; }
                if (exportAnims != null) { RipNRage.Game.Content.GameAssets.Load(); RipNRage.Game.Content.GameAssets.ExportAnimations(exportAnims); return 0; }
                string levelPath = noLevel ? null : (level != null ? RipNRage.Game.Content.Level.LevelLoader.Locate(level) : RipNRage.Game.Content.Level.LevelLoader.Locate("dock.map"));
                if (level != null && levelPath == null) { Console.Error.WriteLine("Level not found: " + level); return 1; }
                GameSession.OnSceneBuilt = tools =>
                {
                    tools.AddComponent<DebugOverlay>();
                    if (script != null) tools.AddComponent<DemoScript>().scriptName = script;
                };
                InputSystem.Initialize();          // rebindable gameplay / UI schemes (controls.json)
                InputSystem.ManageCursor = true;   // locked in gameplay, free while any menu is up
                RipNRage.Game.Content.GraphicsSettings.Install(); // player's graphics overrides on top of tunables.cfg
                GameSession.DefaultLevelPath = levelPath;
                // automation runs (scripts, frame-limited captures, direct loads) skip the title screen
                bool menu = startInMenu ?? (script == null && loadSlot == null && options.MaxFrames == 0);
                if (menu) GameSession.ShowMainMenu(levelPath);
                else GameSession.StartLevel(levelPath);
                if (loadSlot != null && !RipNRage.Game.Content.Level.GameSaveSystem.Load(loadSlot)) Console.Error.WriteLine("Could not load slot " + loadSlot);
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
    public class DebugOverlay : MonoBehaviour, IUiDrawer, IUiLayer
    {
        public int UiLayer => UiLayers.Debug;
        /// <summary>0 = hidden, 1 = compact, 2 = full profiler.</summary>
        public int mode = 2;
        public bool visible { get => mode > 0; set => mode = value ? 2 : 0; }
        private static string _message = ""; private static float _messageTimer;

        protected override void Start()
        {
            RipNRage.Game.Content.Level.GameSaveSystem.OnMessage = m => { _message = m; _messageTimer = 3f; };
            mode = RipNRage.Game.Content.GraphicsSettings.Current.overlayMode ?? mode;
        }

        protected override void Update()
        {
            if (_messageTimer > 0f) _messageTimer -= Time.unscaledDeltaTime;
            if (InputSystem.Gameplay["quick_save"].Pressed) RipNRage.Game.Content.Level.GameSaveSystem.Save("quick");
            if (InputSystem.Gameplay["quick_load"].Pressed) RipNRage.Game.Content.Level.GameSaveSystem.Load("quick");
            if (Input.GetKeyDown(KeyCode.F7)) RipNRage.Game.Content.Level.GameSaveSystem.Load("checkpoint");
            if (Input.GetKeyDown(KeyCode.F8) && GameSession.LevelPath != null) GameSession.RestartLevel(GameSession.LevelPath);
            if (Input.GetKeyDown(KeyCode.F3)) { mode = (mode + 1) % 3; RipNRage.Game.Content.GraphicsSettings.Current.overlayMode = mode; RipNRage.Game.Content.GraphicsSettings.Save(); }
            if (Input.GetKeyDown(KeyCode.F4)) Application.Current.Renderer.PostFx.Enabled = !Application.Current.Renderer.PostFx.Enabled;
            if (Input.GetKeyDown(KeyCode.F5)) { Application.Current.Tunables?.Reload(); ShaderLibrary.ReloadAll(); }
            if (Input.GetKeyDown(KeyCode.F12)) Application.Current.Screenshot("screenshot_" + Time.frameCount + ".png");
            if (Input.GetKeyDown(KeyCode.F11)) { Profiler.SyncGpu = !Profiler.SyncGpu; _message = Profiler.SyncGpu ? "Profiler: GPU sync on (CPU times include GPU work)" : "Profiler: GPU sync off"; _messageTimer = 3f; }
        }

        public void DrawUi(UiBatch ui)
        {
            if (_messageTimer > 0f)
            {
                float ms = Mathf.Max(1f, ui.Scale * 2f);
                ui.DrawText(_message.ToUpperInvariant(), new Vector2(ui.Width * 0.5f - _message.Length * 3.5f * ms, ui.Height * 0.62f), ms * 1.5f, new Color(1f, 0.9f, 0.4f, 1f));
            }
            float s = Mathf.Max(1f, ui.Scale * 2f);
            float lineH = (BitmapFont.GlyphH + 1) * s;
            var renderer = Application.Current.Renderer;
            if (RipNRage.Engine.Platform.GpuInfo.IsSoftware)
            {
                // always shown: the single most important performance fact about this machine
                string warn = $"SOFTWARE RENDERER ({RipNRage.Engine.Platform.GpuInfo.Renderer.ToUpperInvariant()}): THE GPU IS NOT IN USE, EVERY PIXEL IS COMPUTED ON THE CPU. SEE THE CONSOLE FOR FIXES.";
                float ww = ui.MeasureText(warn, s).x + 16f;
                ui.FillRect(new Rect(ui.Width * 0.5f - ww * 0.5f, ui.Height - lineH * 2.6f - 6f, ww, lineH + 8f), new Color(0.6f, 0.05f, 0.05f, 0.9f));
                ui.DrawText(warn, new Vector2(ui.Width * 0.5f - ww * 0.5f + 8f, ui.Height - lineH * 1.6f - 2f), s, Color.white);
            }
            if (mode == 0) return;

            // ---- frame line: the real numbers (wall clock), never the simulated delta
            string frame = $"FPS {Profiler.Fps:0.0}  1% LOW {Profiler.FpsLow1:0.0}   FRAME {Profiler.FrameMsAvg:0.0} MS AVG  {Profiler.FrameMsP99:0.0} P99  {Profiler.FrameMsMax:0.0} MAX";
            if (Profiler.GpuTimersAvailable) frame += $"   GPU {Profiler.GpuFrameMsAvg:0.0} MS";
            frame += $"   PRESENT {Profiler.PresentMsAvg:0.0} MS";
            if (Profiler.SimulationDeltaFixed) frame += $"   SIM DT FIXED {Profiler.SimulationDelta * 1000f:0.0} MS (NOT REAL TIME)";
            else if (Time.timeScale != 1f) frame += $"   TIMESCALE {Time.timeScale:0.00}";
            Color frameColor = Profiler.FrameMsAvg > 33.4 ? new Color(1f, 0.45f, 0.35f, 1f) : Profiler.FrameMsAvg > 16.8 ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.6f, 1f, 0.6f, 1f);
            ui.DrawText(frame, new Vector2(12, ui.Height - 12), s, frameColor);
            if (mode == 1) return;

            float y = ui.Height - 12 - lineH * 1.2f;
            string text = "";
            var ps = PlayerState.Current;
            var cam = Camera.main;
            text += $"GPU  {RipNRage.Engine.Platform.GpuInfo.Renderer.ToUpperInvariant()}  ({RipNRage.Engine.Platform.GpuInfo.Vendor.ToUpperInvariant()})  GL {RipNRage.Engine.Platform.GpuInfo.Version.ToUpperInvariant()}{(RipNRage.Engine.Platform.GpuInfo.IsIntegrated ? "  [INTEGRATED]" : "")}\n";
            text += $"MEMORY  GC GEN0/1/2 {Profiler.Gen0PerSecond:0.0}/{Profiler.Gen1PerSecond:0.0}/{Profiler.Gen2PerSecond:0.0} PER S   ALLOC {Profiler.AllocBytesPerFrame / 1024.0:0.0} KB/FRAME   HEAP {Profiler.HeapBytes / (1024.0 * 1024.0):0.0} MB\n";
            var scene = Application.Current.Scene;
            text += $"SCENE  {scene.GameObjects.Count} OBJECTS  {scene.BehaviourCount} BEHAVIOURS  {scene.CoroutineCount} COROUTINES  FIXED STEPS {scene.FixedStepsLastFrame}{(scene.FixedStepsLastFrame >= 8 ? " (CLAMPED: SIM BEHIND)" : "")}   DRAWS {renderer.DrawCalls}\n";
            if (cam != null)
            {
                var p = cam.transform.position;
                text += $"POS {p.x:0.0} {p.y:0.0} {p.z:0.0}  YAW {cam.transform.eulerAngles.y:0}";
            }
            if (ps != null)
            {
                var combat = EnragedInjector<CombatController>.GetInstance();
                string weapon = combat?.inventory?.equippedWeapon?.weaponData?.name ?? "-";
                text += $"   HP {ps.Health:0} ARMOR {ps.Armor:0} RAGE {ps.Rage:0.00} {ps.RageState} {ps.CombatState}  WEAPON {weapon}";
            }
            text += "\n";
            var fx = renderer.PostFx;
            var st = fx.Settings;
            var lt = renderer.Lighting;
            text += $"POSTFX {(fx.IsActive ? "ON" : "OFF")} (F4)  BLOOM {(st.bloomEnabled ? "ON" : "OFF")}  CA {(st.chromaticEnabled ? "ON" : "OFF")}  VIGNETTE {(st.vignetteEnabled ? "ON" : "OFF")}  MOTION BLUR {(st.motionBlurEnabled ? "ON" : "OFF")}  RENDER SCALE {st.renderScale:0.00}  F5 RELOAD CFG\n";
            text += $"SHADOWS {(lt.shadowsEnabled ? lt.shadowResolution.ToString() : "OFF")}  VOLUMETRIC FOG {(lt.volumetricEnabled ? $"ON ({lt.volSteps} STEPS @ {lt.volScale:0.00})" : "OFF")}  SUN {lt.sunElevation:0}/{lt.sunAzimuth:0}  LIGHTS {RipNRage.Engine.Graphics.Lighting.Light.Lights.Count}  SHADERS {(ShaderLibrary.Directory != null ? "FILES (HOT RELOAD)" : "EMBEDDED")}\n";
            var sm = renderer.Shadows;
            text += $"SHADOW CACHE {sm.StaticRenders} STATIC RENDERS  {sm.StaticCasters} DRAWN / {sm.StaticCulled} CULLED  DYNAMIC {sm.DynamicCasters}  LIGHT SETS {renderer.LightUploads}\n";
            text += $"VFX {RipNRage.Engine.Graphics.Particles.VfxLibrary.Effects.Count} EFFECTS  LIVE {RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveInstances} FX / {RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveParticles} PARTICLES  {renderer.Particles.DrawCalls} DRAWS  {RipNRage.Engine.Graphics.Particles.ParticleWorld.CollisionRaycasts} RAYCASTS\n";
            var cc = renderer.CameraConfig;
            text += $"FOV WORLD {cam?.fieldOfView:0} / VIEWMODEL {cc.viewmodelFov:0} ({(cc.viewmodelLit ? "LIT" : "UNLIT")})  CULLING {(cc.frustumCulling ? "FRUSTUM " : "")}{(cc.occlusionCulling ? $"OCCLUSION {cc.occlusionMode.ToString().ToUpperInvariant()}{(renderer.GpuOcclusion != null && cc.occlusionMode == OcclusionMode.Gpu ? $" ({renderer.GpuOcclusion.Tested} QUERIES, {renderer.GpuOcclusion.Pending} PENDING)" : "")} " : "")}{renderer.Culling}\n";
            var mover = ps != null ? ps.GetComponent<RipNRage.Engine.CharacterController>() : null;
            if (mover != null)
            {
                var fall = ps.GetComponent<FallRespawn>();
                text += $"MOVER  {(mover.isGrounded ? "GROUNDED" : "AIRBORNE")}  SPEED {mover.velocity.magnitude:0.0} M/S  SUBSTEPS {mover.LastSubsteps}  PUSH-OUT {mover.LastCorrection * 100f:0} CM  STUCK REWINDS {mover.StuckRewinds}{(fall != null ? $"  FALLS {fall.Falls} (MODE {FallRespawn.Mode.ToString().ToUpperInvariant()}, LAST: {fall.LastFallKind})" : "")}\n";
            }
            text += "WASD MOVE  MOUSE LOOK  LMB FIRE  RMB JAB  1-4/WHEEL WEAPONS  SPACE JUMP  F3 OVERLAY  F11 GPU SYNC  ESC PAUSE\n";
            text += "F6 QUICK SAVE  F9 QUICK LOAD  F7 LOAD CHECKPOINT  F8 RESTART LEVEL";
            ui.DrawText(text, new Vector2(12, y), s, new Color(1, 1, 1, 0.85f));

            DrawStageTable(ui, s, lineH);
            DrawFrameGraph(ui, s, lineH);
        }

        /// <summary>Per-stage CPU / GPU milliseconds (window average, worst) with proportional bars, sorted by cost.</summary>
        private void DrawStageTable(UiBatch ui, float s, float lineH)
        {
            var stages = new System.Collections.Generic.List<Profiler.Stage>(Profiler.Stages);
            stages.RemoveAll(st => st.cpuAvg < 0.02 && st.gpuAvg < 0.02);
            stages.Sort((a, b) => Mathf.Max((float)b.cpuAvg, (float)b.gpuAvg).CompareTo(Mathf.Max((float)a.cpuAvg, (float)a.gpuAvg)));
            float x = 12f, w = Mathf.Min(ui.Width * 0.42f, 560f * s / 2f);
            float rows = stages.Count + 2;
            float h = rows * lineH + 10f * s;
            float y0 = 12f * s + 60f * s; // above the HUD's bottom strip
            var box = new Rect(x - 4f, y0, w + 8f, h);
            ui.FillRect(box, new Color(0f, 0f, 0f, 0.55f));
            float y = box.yMax - 4f;
            string header = Profiler.GpuTimersAvailable ? "STAGE                    CPU MS   MAX     GPU MS" : "STAGE                    CPU MS   MAX";
            if (Profiler.SyncGpu) header += "   [SYNC]";
            ui.DrawText(header, new Vector2(x, y), s, new Color(1f, 0.85f, 0.4f, 1f));
            y -= lineH;
            double total = Mathf.Max((float)Profiler.FrameMsAvg, 0.001f);
            foreach (var st in stages)
            {
                string name = st.name.ToUpperInvariant();
                if (name.Length > 22) name = name.Substring(0, 22);
                string line = $"{Pad(new string(' ', st.depth) + name, 24)} {st.cpuAvg,6:0.00} {st.cpuMax,6:0.0}";
                if (st.hasGpu) line += $" {st.gpuAvg,8:0.00}";
                // bar: fraction of the frame
                float frac = (float)(Mathf.Max((float)st.cpuAvg, st.hasGpu ? (float)st.gpuAvg : 0f) / total);
                ui.FillRect(new Rect(x, y - lineH + 2f * s, w * Mathf.Clamp01(frac), lineH - 3f * s), new Color(0.9f, 0.5f, 0.2f, 0.25f));
                ui.DrawText(line, new Vector2(x, y), s, st.depth > 0 ? new Color(0.8f, 0.8f, 0.85f, 0.9f) : new Color(1f, 1f, 1f, 0.95f));
                y -= lineH;
            }
            string note = Profiler.GpuTimersAvailable
                ? "GPU = TIMER QUERY (3 FRAMES OLD).  PRESENT = SWAPBUFFERS WAIT (VSYNC / GPU BEHIND).  F11 = GLFINISH PER STAGE"
                : "NO GPU TIMER QUERIES: F11 (GLFINISH PER STAGE) MAKES CPU TIMES INCLUDE THE GPU WORK";
            ui.DrawText(note, new Vector2(x, y), s * 0.75f, new Color(0.7f, 0.7f, 0.75f, 0.9f));
        }

        private static string Pad(string t, int n) => t.Length >= n ? t : t + new string(' ', n - t.Length);

        /// <summary>Frame-time history (last 240 frames) as bars with 16.7 / 33.3 ms guide lines, top right.</summary>
        private void DrawFrameGraph(UiBatch ui, float s, float lineH)
        {
            float gw = Mathf.Min(ui.Width * 0.3f, 480f * s / 2f), gh = 70f * s / 2f * 1.4f;
            var box = new Rect(ui.Width - gw - 12f, ui.Height - 12f - lineH * 1.3f - gh, gw, gh);
            ui.FillRect(box, new Color(0f, 0f, 0f, 0.55f));
            float maxMs = 50f;
            foreach (var v in Profiler.History) if (v > maxMs) maxMs = Mathf.Min(v, 250f);
            float barW = gw / Profiler.HistoryLength;
            int idx = Profiler.HistoryIndex;
            for (int i = 0; i < Profiler.HistoryLength; i++)
            {
                float v = Profiler.History[(idx + i) % Profiler.HistoryLength];
                if (v <= 0f) continue;
                float hgt = Mathf.Clamp01(v / maxMs) * gh;
                Color c = v > 33.4f ? new Color(1f, 0.35f, 0.3f, 0.9f) : v > 16.8f ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(0.5f, 0.95f, 0.5f, 0.9f);
                ui.FillRect(new Rect(box.x + i * barW, box.y, Mathf.Max(1f, barW - 0.5f), hgt), c);
            }
            foreach (var guide in new[] { 16.7f, 33.3f })
            {
                if (guide > maxMs) continue;
                float gy = box.y + guide / maxMs * gh;
                ui.FillRect(new Rect(box.x, gy, gw, 1f), new Color(1f, 1f, 1f, 0.35f));
                ui.DrawText($"{guide:0}", new Vector2(box.x + 2f, gy + lineH * 0.5f), s * 0.7f, new Color(1f, 1f, 1f, 0.6f));
            }
            ui.DrawText($"FRAME MS (LAST {Profiler.HistoryLength}, SCALE {maxMs:0} MS)", new Vector2(box.x + 2f, box.yMax - 2f), s * 0.7f, new Color(1f, 1f, 1f, 0.7f));
        }
    }

    /// <summary>Scripted input for automated screenshots/tests (feeds PlayerInputActions' override hooks).</summary>
    public class DemoScript : MonoBehaviour
    {
        public string scriptName = "look";
        private int _frame;
        private float _strafeDir;

        protected override void Update()
        {
            _frame++;
            var input = EnragedInjector<PlayerInputActions>.GetInstance();
            var combat = EnragedInjector<CombatController>.GetInstance();
            if (input == null || combat == null) return;
            input.overrideFiring = null; input.overrideMove = null; input.overrideLook = null;
            switch (scriptName)
            {
                case "look":
                    input.overrideLook = new Vector2(2f, 0f);
                    break;
                case "crates":
                    // walk to the crate pile and look at it
                    input.overrideMove = _frame < 95 ? new Vector2(-0.2f, 1f) : Vector2.zero;
                    input.overrideLook = _frame > 95 && _frame < 115 ? new Vector2(14f, -3f) : Vector2.zero;
                    break;
                case "sunward":
                    // walk down the pier, then turn around to face the sun through the lamp posts
                    input.overrideMove = _frame < 270 ? new Vector2(0f, 1f) : Vector2.zero;
                    input.overrideLook = _frame > 270 && _frame < 310 ? new Vector2(-36f, 1.0f) : Vector2.zero;
                    break;
                case "water":
                    // look over the railing at the water
                    input.overrideMove = _frame < 45 ? new Vector2(1f, 0f) : Vector2.zero;
                    input.overrideLook = _frame > 45 && _frame < 76 ? new Vector2(30f, -6f) : Vector2.zero;
                    break;
                case "killenemy":
                {
                    // spawn a rifleman close by, aim at it, kill it with the MG, then watch the hit flash + dissolve
                    if (_frame == 2) RipNRage.Game.Content.Prefabs.SpawnRifleman(new Vector3(-1.5f, 0.91f, -6f), Quaternion.Euler(0, 0, 0));
                    if (_frame == 5) combat.OnChangeWeapon1();
                    var cam = Camera.main.transform;
                    AIState nearest = null; float best = float.MaxValue;
                    foreach (var st in Scene.Current.FindObjectsOfType<AIState>())
                    {
                        float d = (st.transform.position - cam.position).sqrMagnitude;
                        if (d < best) { best = d; nearest = st; }
                    }
                    if (nearest != null)
                    {
                        Vector3 to = nearest.transform.position + Vector3.up * 0.3f - cam.position;
                        float yawErr = Vector3.SignedAngle(new Vector3(cam.forward.x, 0, cam.forward.z), new Vector3(to.x, 0, to.z), Vector3.up);
                        float pitchNow = -Mathf.Asin(Mathf.Clamp(cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float pitchWanted = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                        // FPSCameraController: degrees = pixels * sensitivity(5) * fixedDeltaTime(0.02) = 0.1 deg/px
                        input.overrideLook = new Vector2(Mathf.Clamp(yawErr * 10f, -60f, 60f), Mathf.Clamp((pitchNow - pitchWanted) * 10f, -60f, 60f));
                    }
                    input.overrideFiring = _frame >= 60 && _frame < 240; // many Glock hits: the grunt has 1000 hp + 1000 armour
                    break;
                }
                case "spin":
                    input.overrideLook = new Vector2(40f, 0f);
                    break;
                case "walk":
                    input.overrideMove = new Vector2(0f, 1f);
                    if (_frame % 90 == 0) input.OnJump();
                    break;
                case "fire":
                    input.overrideMove = new Vector2(0f, 0.5f);
                    input.overrideFiring = _frame > 30 && (_frame / 20) % 2 == 0;
                    break;
                case "weapons":
                    if (_frame == 40) combat.OnChangeWeapon2();
                    if (_frame == 80) combat.OnChangeWeapon3();
                    if (_frame == 120) combat.OnChangeWeapon4();
                    if (_frame == 160) combat.OnChangeWeapon1();
                    input.overrideFiring = _frame % 40 > 30;
                    break;
                case "jab":
                    if (_frame == 30 || _frame == 45) combat.OnAltFire(true);
                    break;
                case "m1887":
                    if (_frame == 10) combat.OnChangeWeapon2();
                    input.overrideFiring = _frame == 30;
                    break;
                case "glockfire":
                    input.overrideFiring = _frame == 30;
                    break;
                case "peek":
                    // turn towards the nearby rifleman and watch it
                    input.overrideLook = _frame < 40 ? new Vector2(1.2f, 0f) : Vector2.zero;
                    break;
                case "close":
                    // walk up to the rifleman standing near the spawn
                    input.overrideMove = _frame < 55 ? new Vector2(-0.25f, 1f) : Vector2.zero;
                    break;
                case "approach":
                    // run towards the first rifleman, then stand and shoot
                    input.overrideMove = _frame < 270 ? new Vector2(0f, 1f) : Vector2.zero;
                    input.overrideFiring = _frame > 300 && _frame % 30 == 0;
                    break;
                case "rocket":
                    if (_frame == 10) combat.OnChangeWeapon4();
                    input.overrideLook = _frame > 20 && _frame < 26 ? new Vector2(0f, -60f) : Vector2.zero; // aim down at the dock
                    input.overrideFiring = _frame == 40;
                    break;
                case "rage":
                    // force the enraged state to check the rage post-processing volume
                    if (_frame == 20) { PlayerState.Current.SetViolentCombatState(); PlayerState.Current.AddToRage(1f); }
                    break;
                case "saveload":
                    // walk, save (F6), walk more, load (F9): the player must be back at the saved spot with the saved ammo
                    input.overrideMove = (_frame < 100 || (_frame > 120 && _frame < 200)) ? new Vector2(0f, 1f) : Vector2.zero;
                    input.overrideFiring = _frame > 60 && _frame < 66;
                    if (_frame == 110) { Input.SimulateKey(KeyCode.F6, true); Debug.LogWarning($"[test] saved at {PlayerState.Current.transform.position} ammo {combat.inventory.ammo[0]}"); }
                    if (_frame == 111) Input.SimulateKey(KeyCode.F6, false);
                    if (_frame == 205) Debug.LogWarning($"[test] before load at {PlayerState.Current.transform.position}");
                    if (_frame == 210) Input.SimulateKey(KeyCode.F9, true);
                    if (_frame == 211) Input.SimulateKey(KeyCode.F9, false);
                    if (_frame == 240 && PlayerState.Current != null) Debug.LogWarning($"[test] after load at {PlayerState.Current.transform.position} ammo {EnragedInjector<CombatController>.GetInstance().inventory.ammo[0]} hp {PlayerState.Current.Health}");
                    break;
                case "glockalt":
                    // three right clicks: jab, jab, then the Glock grapple/alt-fire melee (needs an enemy within 1 m)
                    if (_frame == 30 || _frame == 40 || _frame == 50) combat.OnAltFire(true);
                    break;
                case "die":
                    // the death loop: walk, take lethal damage at frame 40, watch the camera fall and the
                    // "You are dead" screen fade in, pick RESTART at frame 260 and confirm the respawn
                    input.overrideMove = _frame < 40 ? new Vector2(0f, 1f) : Vector2.zero;
                    if (_frame == 40 && PlayerState.Current != null) PlayerState.Current.TakeDamage(10000f);
                    if (_frame == 120 && PlayerState.Current != null) Debug.LogWarning($"[test] dead={PlayerState.Current.IsDead} hp={PlayerState.Current.Health} timeScale={Time.timeScale:0.00} deathScreen={(DeathScreen.Current != null)}");
                    if (_frame == 260) Input.SimulateKey(KeyCode.Return, true);
                    if (_frame == 261) Input.SimulateKey(KeyCode.Return, false);
                    if (_frame == 300) Debug.LogWarning($"[test] after respawn: player={(PlayerState.Current != null)} dead={PlayerState.Current?.IsDead} hp={PlayerState.Current?.Health} timeScale={Time.timeScale:0.00} deathScreen={(DeathScreen.Current != null)}");
                    break;
                case "vfx":
                {
                    // data-driven effects: shoot the floor / wall with every weapon, then a rocket; prints particle stats
                    if (_frame == 5) combat.OnChangeWeapon1();
                    if (_frame == 60) combat.OnChangeWeapon2();
                    if (_frame == 110) combat.OnChangeWeapon3();
                    if (_frame == 170) combat.OnChangeWeapon4();
                    input.overrideLook = _frame > 10 && _frame < 16 ? new Vector2(0f, -40f) : Vector2.zero; // aim down at the planks
                    input.overrideFiring = (_frame >= 20 && _frame < 24) || (_frame >= 70 && _frame < 72) || (_frame >= 120 && _frame < 150) || _frame == 180;
                    if (_frame % 30 == 0) Debug.LogWarning($"[test] vfx instances={RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveInstances} particles={RipNRage.Engine.Graphics.Particles.ParticleWorld.LiveParticles} effects={RipNRage.Engine.Graphics.Particles.VfxLibrary.Effects.Count}");
                    break;
                }
                case "dash":
                {
                    // dash forward at frame 40 and again in the air after a jump at frame 120
                    input.overrideMove = new Vector2(0f, 1f);
                    if (_frame == 40 || _frame == 125) Input.SimulateKey(KeyCode.LeftShift, true);
                    if (_frame == 41 || _frame == 126) Input.SimulateKey(KeyCode.LeftShift, false);
                    if (_frame == 115) input.OnJump();
                    var fps = PlayerState.Current?.GetComponent<FPSController>();
                    if (_frame % 10 == 0 && fps != null) Debug.LogWarning($"[test] frame {_frame} dashing={fps.IsDashing} pos {PlayerState.Current.transform.position}");
                    break;
                }
                case "slide":
                {
                    // run forward, slide at frame 50, jump out of it at frame 80; logs the controller state
                    input.overrideMove = new Vector2(0f, 1f);
                    if (_frame == 50) Input.SimulateKey(KeyCode.LeftControl, true);
                    if (_frame == 80) { Input.SimulateKey(KeyCode.LeftControl, false); Input.SimulateKey(KeyCode.Space, true); }
                    if (_frame == 81) Input.SimulateKey(KeyCode.Space, false);
                    var fps = PlayerState.Current?.GetComponent<FPSController>();
                    if (_frame % 10 == 0 && fps != null) Debug.LogWarning($"[test] frame {_frame} sliding={fps.IsSliding} wallrun={fps.IsWallRunning} pos {PlayerState.Current.transform.position} speed {fps.MCurrentSpeed:0.0}");
                    break;
                }
                case "style":
                {
                    // spawn a grunt in front, kill it with the Glock, spawn another, kill it with the MG: the meter should reach B and show
                    if (_frame == 2 || _frame == 130) RipNRage.Game.Content.Prefabs.SpawnRifleman(Camera.main.transform.position + Camera.main.transform.forward * 6f - Vector3.up * 1.3f, Quaternion.identity);
                    if (_frame == 5) combat.OnChangeWeapon1();
                    if (_frame == 135) combat.OnChangeWeapon3();
                    input.overrideFiring = (_frame > 20 && _frame < 120) || (_frame > 140 && _frame < 200);
                    var sm = StyleMeter.Current;
                    if (_frame % 30 == 0 && sm != null) Debug.LogWarning($"[test] frame {_frame} style {sm.CurrentRank} {sm.Points:0} x{sm.Multiplier} visible={sm.Visible}");
                    break;
                }
                case "fall":
                {
                    // walk sideways off the dock into the water: the fall must be classified as EDGE and then either kill
                    // the player (default mode) or put them back on the planks (--fall-mode respawn)
                    input.overrideMove = _frame < 150 ? new Vector2(1f, 0f) : Vector2.zero;
                    var fr = PlayerState.Current?.GetComponent<FallRespawn>();
                    if (_frame % 30 == 0 && PlayerState.Current != null) Debug.LogWarning($"[test] frame {_frame} pos {PlayerState.Current.transform.position} falls={fr?.Falls} last={fr?.LastFallKind} dead={PlayerState.Current.IsDead} killY={FallRespawn.KillY:0.0} mode={FallRespawn.Mode}");
                    break;
                }
                case "strafe":
                {
                    // strafe left and right across the middle of the dock (never near an edge), with jumps: a player that
                    // ends up below the planks here went THROUGH them. Prints PASS/FAIL at the end.
                    var p = PlayerState.Current;
                    if (p == null) break;
                    // always steer back towards x = 0: with the controller's input smoothing that oscillates a few metres
                    // either side of the centre line (the dock is 32 m wide), on the ground and through bunny hops
                    float x = p.transform.position.x;
                    _strafeDir = x > 0f ? -1f : 1f;
                    input.overrideMove = new Vector2(_strafeDir, _frame % 90 < 45 ? 0.4f : -0.4f);
                    if (_frame % 70 == 35 && Mathf.Abs(x) < 2f) input.OnJump();
                    var fr = p.GetComponent<FallRespawn>();
                    var cc = p.GetComponent<RipNRage.Engine.CharacterController>();
                    if (_frame % 30 == 0) Debug.LogWarning($"[test] frame {_frame} pos {p.transform.position} grounded={cc?.isGrounded} pushout={(cc != null ? cc.LastCorrection * 100f : 0f):0}cm rewinds={cc?.StuckRewinds} falls={fr?.Falls} last={fr?.LastFallKind}");
                    if (_frame == 600)
                    {
                        bool ok = (fr == null || fr.Falls == 0) && (cc == null || cc.StuckRewinds == 0) && !p.IsDead;
                        Debug.LogWarning(ok ? "[test] strafe PASS: 600 frames of strafing and jumping on the dock, no fall, no stuck rewind"
                                            : $"[test] strafe FAIL: falls={fr?.Falls} ({fr?.LastFallKind}) rewinds={cc?.StuckRewinds} dead={p.IsDead} — {fr?.LastFallReport}");
                    }
                    break;
                }
                case "pause":
                    // Esc opens the pause menu (world frozen), Esc again resumes
                    input.overrideMove = new Vector2(0f, 1f);
                    if (_frame == 60) Input.SimulateKey(KeyCode.Escape, true);
                    if (_frame == 61) Input.SimulateKey(KeyCode.Escape, false);
                    if (_frame == 90) Debug.LogWarning($"[test] paused={PauseMenu.IsPaused} timeScale={Time.timeScale:0.00}");
                    if (_frame == 150) Input.SimulateKey(KeyCode.Escape, true);
                    if (_frame == 151) Input.SimulateKey(KeyCode.Escape, false);
                    if (_frame == 180) Debug.LogWarning($"[test] paused={PauseMenu.IsPaused} timeScale={Time.timeScale:0.00}");
                    break;
            }
        }
    }
}
