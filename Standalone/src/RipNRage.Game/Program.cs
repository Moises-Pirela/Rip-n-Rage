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
                    case "--animations": RipNRage.Game.Content.GameAssets.AnimationsPath = Next(); break;
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
                if (exportDock != null) { RipNRage.Game.Content.Level.DockLevel.Export(exportDock); return 0; }
                if (exportAnims != null) { RipNRage.Game.Content.GameAssets.Load(); RipNRage.Game.Content.GameAssets.ExportAnimations(exportAnims); return 0; }
                string levelPath = noLevel ? null : (level != null ? RipNRage.Game.Content.Level.LevelLoader.Locate(level) : RipNRage.Game.Content.Level.LevelLoader.Locate("dock.map"));
                if (level != null && levelPath == null) { Console.Error.WriteLine("Level not found: " + level); return 1; }
                GameSession.OnSceneBuilt = tools =>
                {
                    tools.AddComponent<DebugOverlay>();
                    if (script != null) tools.AddComponent<DemoScript>().scriptName = script;
                };
                GameSession.StartLevel(levelPath);
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

    /// <summary>F3 toggles an on-screen readout; Escape releases the mouse, Escape again quits.</summary>
    public class DebugOverlay : MonoBehaviour, IUiDrawer
    {
        public bool visible = true;
        private bool _mouseReleased;
        private static string _message = ""; private static float _messageTimer;

        protected override void Start()
        {
            RipNRage.Game.Content.Level.GameSaveSystem.OnMessage = m => { _message = m; _messageTimer = 3f; };
        }

        protected override void Update()
        {
            if (_messageTimer > 0f) _messageTimer -= Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.F6)) RipNRage.Game.Content.Level.GameSaveSystem.Save("quick");
            if (Input.GetKeyDown(KeyCode.F9)) RipNRage.Game.Content.Level.GameSaveSystem.Load("quick");
            if (Input.GetKeyDown(KeyCode.F7)) RipNRage.Game.Content.Level.GameSaveSystem.Load("checkpoint");
            if (Input.GetKeyDown(KeyCode.F8) && GameSession.LevelPath != null) GameSession.RestartLevel(GameSession.LevelPath);
            if (Input.GetKeyDown(KeyCode.F3)) visible = !visible;
            if (Input.GetKeyDown(KeyCode.F4)) Application.Current.Renderer.PostFx.Enabled = !Application.Current.Renderer.PostFx.Enabled;
            if (Input.GetKeyDown(KeyCode.F5)) { Application.Current.Tunables?.Reload(); ShaderLibrary.ReloadAll(); }
            if (Input.GetKeyDown(KeyCode.F12)) Application.Current.Screenshot("screenshot_" + Time.frameCount + ".png");
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (!_mouseReleased) { _mouseReleased = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                else Application.Quit();
            }
            else if (_mouseReleased && Input.GetMouseButtonDown(0))
            {
                _mouseReleased = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
        }

        public void DrawUi(UiBatch ui)
        {
            if (!visible) return;
            var ps = PlayerState.Current;
            var cam = Camera.main;
            string text = $"RIP-N-RAGE STANDALONE  FPS {Application.Current.Fps:0}  DRAWS {Application.Current.Renderer.DrawCalls}\n";
            if (cam != null)
            {
                var p = cam.transform.position;
                text += $"POS {p.x:0.0} {p.y:0.0} {p.z:0.0}  YAW {cam.transform.eulerAngles.y:0}\n";
            }
            if (ps != null)
            {
                var combat = EnragedInjector<CombatController>.GetInstance();
                string weapon = combat?.inventory?.equippedWeapon?.weaponData?.name ?? "-";
                text += $"HP {ps.Health:0} ARMOR {ps.Armor:0} RAGE {ps.Rage:0.00} {ps.RageState} {ps.CombatState}  WEAPON {weapon}\n";
            }
            var fx = Application.Current.Renderer.PostFx;
            var st = fx.Settings;
            var lt = Application.Current.Renderer.Lighting;
            text += $"POSTFX {(fx.IsActive ? "ON" : "OFF")} (F4)  BLOOM {(st.bloomEnabled ? "ON" : "OFF")}  CA {(st.chromaticEnabled ? "ON" : "OFF")}  VIGNETTE {(st.vignetteEnabled ? "ON" : "OFF")}  DOF {(st.dofEnabled ? "ON" : "OFF")}  MOTION BLUR {(st.motionBlurEnabled ? "ON" : "OFF")}  F5 RELOAD CFG\n";
            text += $"SHADOWS {(lt.shadowsEnabled ? lt.shadowResolution.ToString() : "OFF")}  VOLUMETRIC FOG {(lt.volumetricEnabled ? "ON" : "OFF")}  SUN {lt.sunElevation:0}/{lt.sunAzimuth:0}  LIGHTS {RipNRage.Engine.Graphics.Lighting.Light.Lights.Count}  SHADERS {(ShaderLibrary.Directory != null ? "FILES (HOT RELOAD)" : "EMBEDDED")}\n";
            var cc = Application.Current.Renderer.CameraConfig;
            text += $"FOV WORLD {cam?.fieldOfView:0} / VIEWMODEL {cc.viewmodelFov:0} ({(cc.viewmodelLit ? "LIT" : "UNLIT")})  CULLING {(cc.frustumCulling ? "FRUSTUM " : "")}{(cc.occlusionCulling ? "OCCLUSION " : "")}{Application.Current.Renderer.Culling}\n";
            text += "WASD MOVE  MOUSE LOOK  LMB FIRE  RMB JAB  1-4/WHEEL WEAPONS  SPACE JUMP  F3 HUD  ESC MOUSE/QUIT\n";
            text += "F6 QUICK SAVE  F9 QUICK LOAD  F7 LOAD CHECKPOINT  F8 RESTART LEVEL";
            float s = Mathf.Max(1f, ui.Scale * 2f);
            ui.DrawText(text, new Vector2(12, ui.Height - 12), s, new Color(1, 1, 1, 0.85f));
            if (_messageTimer > 0f) ui.DrawText(_message.ToUpperInvariant(), new Vector2(ui.Width * 0.5f - _message.Length * 3.5f * s, ui.Height * 0.62f), s * 1.5f, new Color(1f, 0.9f, 0.4f, 1f));
        }
    }

    /// <summary>Scripted input for automated screenshots/tests (feeds PlayerInputActions' override hooks).</summary>
    public class DemoScript : MonoBehaviour
    {
        public string scriptName = "look";
        private int _frame;

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
            }
        }
    }
}
