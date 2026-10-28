using System;
using System.IO;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;
using RipNRage.Level;

namespace RipNRage.Editor
{
    /// <summary>
    /// Rip-n-Rage level editor (separate executable).
    ///   RipNRageEditor [levels/file.map] [--assets dir] [--width W --height H] [--import-obj file.obj --scale 0.25] [--frames N --screenshot out.png --script name]
    /// Opens the file if it exists (otherwise starts a new level saved there with Ctrl+S).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions { Title = "Rip-n-Rage Level Editor", Width = 1280, Height = 720, Mute = true };
            string file = null, importObj = null, script = null; float importScale = 0.25f;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                switch (a)
                {
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--width": options.Width = int.Parse(Next()); break;
                    case "--height": options.Height = int.Parse(Next()); break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--import-obj": importObj = Next(); break;
                    case "--scale": importScale = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--script": script = Next(); break;
                    case "--verbose": options.Verbose = true; break;
                    case "--help": case "-h": Console.WriteLine("RipNRageEditor [file.map] [--assets dir] [--import-obj file.obj --scale s]"); return 0;
                    default:
                        if (a.StartsWith("--")) { Console.WriteLine("Unknown option " + a); return 1; }
                        file = a; break;
                }
            }
            options.DisablePostFx = false;

            try
            {
                using var app = new Application(options);
                // the editor never wants the heavy lens effects or DOF hiding geometry
                app.Renderer.PostFx.Enabled = false;
                GameAssets.Load();
                Materials.Load();

                var camGo = new GameObject("EditorCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 70f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
                Camera.main = cam;
                var fly = camGo.AddComponent<EditorCamera>();
                camGo.transform.position = new Vector3(0f, 14f, 30f);
                fly.yaw = 180f; fly.pitch = 22f;

                var ctrlGo = new GameObject("Editor");
                var editor = ctrlGo.AddComponent<EditorController>();
                editor.cam = fly;

                if (file != null)
                {
                    string located = LevelLoader.Locate(file) ?? file;
                    if (File.Exists(located)) { editor.doc = MapFile.Load(located); Debug.LogWarning("Opened " + located); }
                    else { editor.doc = new MapDocument { path = file }; Debug.LogWarning("New level: " + file); }
                }
                else editor.doc = new MapDocument { path = "levels/untitled.map" };

                if (importObj != null)
                {
                    foreach (var b in MapFile.ImportObjBrushes(importObj, importScale, true, Vector3.zero, "planks", 8f)) editor.doc.Brushes.Add(b);
                    Debug.LogWarning($"Imported {editor.doc.Brushes.Count} brushes from {importObj}");
                }
                if (script != null) ctrlGo.AddComponent<EditorScript>().scriptName = script;

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

    /// <summary>Scripted interactions for automated screenshots/tests of the editor (drives the real input paths).</summary>
    public class EditorScript : Behaviour
    {
        public string scriptName = "";
        private int _frame;
        private EditorController _ed;

        private static Vector2 Px(float nx, float ny)
        {
            var w = Application.Current.Window;
            return new Vector2(nx * w.Width, ny * w.Height);
        }

        private static void Tap(KeyCode k) { Input.SimulateKey(k, true); }
        private static void Release(KeyCode k) { Input.SimulateKey(k, false); }

        protected override void Update()
        {
            _frame++;
            _ed ??= GetComponent<EditorController>();
            switch (scriptName)
            {
                case "paint":
                    // paint strokes on the dock top, a second colour, erase a bit, drop a spawner, save
                    if (_frame == 3) Tap(KeyCode.V); if (_frame == 4) Release(KeyCode.V);
                    if (_frame == 5) Tap(KeyCode.RightBracket); if (_frame == 6) Release(KeyCode.RightBracket);
                    if (_frame == 8) Input.SimulateMousePosition(Px(0.35f, 0.42f));
                    if (_frame == 9) Input.SimulateMouseButton(0, true);
                    if (_frame >= 10 && _frame <= 60) Input.SimulateMousePosition(Px(0.35f + (_frame - 10) * 0.006f, 0.42f + MathF.Sin((_frame - 10) * 0.3f) * 0.05f));
                    if (_frame == 61) Input.SimulateMouseButton(0, false);
                    if (_frame == 64) Tap(KeyCode.Alpha5); if (_frame == 65) Release(KeyCode.Alpha5);
                    if (_frame == 66) Input.SimulateMousePosition(Px(0.45f, 0.55f));
                    if (_frame == 67) Input.SimulateMouseButton(0, true);
                    if (_frame >= 68 && _frame <= 100) Input.SimulateMousePosition(Px(0.45f + (_frame - 68) * 0.004f, 0.55f));
                    if (_frame == 101) Input.SimulateMouseButton(0, false);
                    if (_frame == 104) Tap(KeyCode.LeftShift);
                    if (_frame == 105) Input.SimulateMousePosition(Px(0.55f, 0.45f));
                    if (_frame == 106) Input.SimulateMouseButton(0, true);
                    if (_frame >= 107 && _frame <= 120) Input.SimulateMousePosition(Px(0.55f + (_frame - 107) * 0.003f, 0.45f));
                    if (_frame == 121) { Input.SimulateMouseButton(0, false); Release(KeyCode.LeftShift); }
                    if (_frame == 125) Input.SimulateMousePosition(Px(0.62f, 0.52f));
                    if (_frame == 126) Tap(KeyCode.N); if (_frame == 127) Release(KeyCode.N);
                    if (_frame == 130) { Tap(KeyCode.LeftControl); Tap(KeyCode.S); }
                    if (_frame == 131) { Release(KeyCode.S); Release(KeyCode.LeftControl); }
                    break;
                case "snapshots":
                    // F7 snapshot, delete every brush (select all via repeated clicks is slow: use the document), open the browser, restore, screenshot
                    if (_frame == 5) Tap(KeyCode.F7); if (_frame == 6) Release(KeyCode.F7);
                    if (_frame == 10) { _ed.doc.Brushes.Clear(); typeof(EditorController).GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(_ed, null); }
                    if (_frame == 12) { Tap(KeyCode.LeftControl); Tap(KeyCode.S); }
                    if (_frame == 13) { Release(KeyCode.S); Release(KeyCode.LeftControl); }
                    if (_frame == 20) Tap(KeyCode.F8); if (_frame == 21) Release(KeyCode.F8);
                    if (_frame == 25) Tap(KeyCode.DownArrow); if (_frame == 26) Release(KeyCode.DownArrow); // select the manual snapshot (2nd newest)
                    if (_frame == 40) Tap(KeyCode.Return); if (_frame == 41) Release(KeyCode.Return);
                    if (_frame == 45) Debug.LogWarning($"[test] brushes after restore: {_ed.doc.Brushes.Count}");
                    if (_frame == 50) Tap(KeyCode.F8); if (_frame == 51) Release(KeyCode.F8);
                    break;
                case "lowgrid":
                    if (_frame == 2) _ed.workHeight = -3f;
                    break;
                case "session":
                    // 1) drag-create a brush on the ground in front of the camera
                    if (_frame == 5) Input.SimulateMousePosition(Px(0.40f, 0.45f));
                    if (_frame == 6) Input.SimulateMouseButton(0, true);
                    if (_frame >= 7 && _frame <= 30) Input.SimulateMousePosition(Px(0.40f + (_frame - 6) * 0.006f, 0.45f - (_frame - 6) * 0.004f));
                    if (_frame == 31) Input.SimulateMouseButton(0, false);
                    // 2) push the top face up twice with '=' (the created brush is selected, top face not yet selected: click it first)
                    if (_frame == 40) Input.SimulateMousePosition(Px(0.47f, 0.40f));
                    if (_frame == 41) Input.SimulateMouseButton(0, true);
                    if (_frame == 42) Input.SimulateMouseButton(0, false);
                    if (_frame == 45 || _frame == 47) Tap(KeyCode.Equals);
                    if (_frame == 46 || _frame == 48) Release(KeyCode.Equals);
                    // 3) texture it orange with 'T' after selecting the texture via '.' presses
                    if (_frame == 50 || _frame == 52 || _frame == 54 || _frame == 56) Tap(KeyCode.Period);
                    if (_frame == 51 || _frame == 53 || _frame == 55 || _frame == 57) Release(KeyCode.Period);
                    if (_frame == 58) Tap(KeyCode.T); if (_frame == 59) Release(KeyCode.T);
                    // 4) duplicate and rotate
                    if (_frame == 62) { Tap(KeyCode.LeftControl); Tap(KeyCode.D); }
                    if (_frame == 63) { Release(KeyCode.D); Release(KeyCode.LeftControl); }
                    if (_frame == 66) Tap(KeyCode.R); if (_frame == 67) Release(KeyCode.R);
                    // 5) entity mode: place a lamp post on the new brush
                    if (_frame == 70) Tap(KeyCode.Tab); if (_frame == 71) Release(KeyCode.Tab);
                    if (_frame == 72) Tap(KeyCode.Alpha8); if (_frame == 73) Release(KeyCode.Alpha8);
                    if (_frame == 75) Input.SimulateMousePosition(Px(0.60f, 0.50f));
                    if (_frame == 76) Input.SimulateMouseButton(0, true);
                    if (_frame == 77) Input.SimulateMouseButton(0, false);
                    // 6) undo once, redo once, save
                    if (_frame == 80) { Tap(KeyCode.LeftControl); Tap(KeyCode.Z); }
                    if (_frame == 81) { Release(KeyCode.Z); Release(KeyCode.LeftControl); }
                    if (_frame == 84) { Tap(KeyCode.LeftControl); Tap(KeyCode.Y); }
                    if (_frame == 85) { Release(KeyCode.Y); Release(KeyCode.LeftControl); }
                    if (_frame == 88) { Tap(KeyCode.LeftControl); Tap(KeyCode.S); }
                    if (_frame == 89) { Release(KeyCode.S); Release(KeyCode.LeftControl); }
                    break;
            }
        }
    }
}
