using System;
using System.IO;
using RipNRage.Engine;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;

namespace RipNRage.AnimEditor
{
    /// <summary>
    /// Rip-n-Rage sprite animation editor (separate executable).
    ///   RipNRageAnimEditor [--animations dir] [--clip name | --controller name] [--assets dir] [--width W --height H]
    ///                      [--frames N --fixed-dt s --screenshot out.png --script name]
    /// Edits the JSON clips/controllers in animations/ (created from the code-built defaults when missing);
    /// the game watches that folder and reloads saved changes live.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions { Title = "Rip-n-Rage Animation Editor", Width = 1280, Height = 720, Mute = true };
            string animDir = null, clipName = null, controllerName = null, script = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                switch (a)
                {
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--animations": animDir = Next(); break;
                    case "--clip": clipName = Next(); break;
                    case "--controller": controllerName = Next(); break;
                    case "--width": options.Width = int.Parse(Next()); break;
                    case "--height": options.Height = int.Parse(Next()); break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--shot-interval": options.ScreenshotInterval = int.Parse(Next()); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--script": script = Next(); break;
                    case "--no-postfx": options.DisablePostFx = true; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--help": case "-h": Console.WriteLine("RipNRageAnimEditor [--animations dir] [--clip name] [--controller name]"); return 0;
                    default:
                        if (a.StartsWith("--")) { Console.WriteLine("Unknown option " + a); return 1; }
                        clipName = a; break;
                }
            }

            try
            {
                using var app = new Application(options);
                if (animDir != null) GameAssets.AnimationsPath = animDir;
                GameAssets.Load();
                Materials.Load();
                if (AnimationLibrary.Directory == null)
                {
                    // first run: materialise the code-built clips so there is something to edit and save to
                    string dir = animDir ?? Path.Combine(Directory.GetCurrentDirectory(), "animations");
                    GameAssets.ExportAnimations(dir);
                }

                BuildPreviewScene(out var cam, out var arms, out var anchor);
                var editor = RipNRage.Engine.Graphics.UiScreens.Add(new AnimEditor { cam = cam, arms = arms, worldAnchor = anchor });
                editor.Start();
                if (script != null) RipNRage.Engine.Graphics.UiScreens.Add(new AnimEditorScript { scriptName = script, Editor = editor });

                // the --clip / --controller selection, after Start
                if (clipName != null) editor.SelectClip(clipName);
                if (controllerName != null) editor.SelectController(controllerName);

                app.Run();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Fatal: " + e);
                return 2;
            }
        }

        /// <summary>Camera at eye height over a plank floor with a few crates for depth; PlayerArms mirrors the player prefab.</summary>
        private static void BuildPreviewScene(out RipNRage.Engine.Graphics.CameraView cam,
            out RipNRage.Engine.Ecs.Entity arms, out RipNRage.Engine.Ecs.Entity anchor)
        {
            var world = Application.Current.World;
            cam = new RipNRage.Engine.Graphics.CameraView { Position = new Vector3(0f, 1.7f, 0f), fieldOfView = 77f, nearClipPlane = 0.05f, farClipPlane = 500f };
            var lens = world.CreateEntity();
            world.Add(lens, RipNRage.Engine.Ecs.Transforms.LocalTransform.At(cam.Position));
            world.Add(lens, new RipNRage.Engine.Ecs.Rendering.CameraLens { FieldOfView = 77f, Near = 0.05f, Far = 500f });

            arms = world.CreateEntity();
            world.Add(arms, RipNRage.Engine.Ecs.Transforms.LocalTransform.At(AnimEditor.ArmsHome));
            world.Add(arms, new RipNRage.Engine.Ecs.Transforms.Parent { Value = lens });

            anchor = world.CreateEntity();
            world.Add(anchor, RipNRage.Engine.Ecs.Transforms.LocalTransform.At(new Vector3(0f, 5f, 20f)));

            void StaticMesh(Mesh mesh, Texture2D tex, bool occluder)
            {
                var e = world.CreateEntity();
                world.Add(e, RipNRage.Engine.Ecs.Transforms.LocalTransform.Identity);
                world.Add(e, new RipNRage.Engine.Ecs.Rendering.MeshRender
                {
                    Mesh = RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Intern(mesh),
                    Texture = RipNRage.Engine.Ecs.Rendering.RenderAssets.Textures.Intern(tex),
                    UvScale = Vector2.one, Color = Color.white, CastsShadow = true, Occluder = occluder,
                    Layer = RipNRage.Engine.Graphics.RenderLayer.World,
                });
            }
            StaticMesh(Mesh.CreatePlane(Vector3.zero, 80f, 80f, 0.25f), ProceduralTextures.WoodPlanks(), true);
            for (int i = 0; i < 5; i++)
            {
                float size = 1f + (i % 3) * 0.5f;
                StaticMesh(Mesh.CreateBox(new Vector3(-6f + i * 3f, size * 0.5f, 9f + (i % 2) * 3f), Vector3.one * size, 1f),
                           i % 2 == 0 ? GameAssets.OrangeProto : GameAssets.GreenProto, false);
            }
            StaticMesh(Mesh.CreateBox(new Vector3(0f, 3f, 22f), new Vector3(40f, 6f, 1f), 0.25f), GameAssets.Texture13, true);
            RenderSettings.drawSky = true;
        }
    }


    /// <summary>Scripted sessions for screenshots/tests (drives the real input paths).</summary>
    public sealed class AnimEditorScript : RipNRage.Engine.Graphics.IUiScreen
    {
        public string scriptName = "";
        public AnimEditor Editor;
        private int _frame;
        public void DrawUi(UiBatch ui) { }
        public void Close() { }
        private AnimEditor _ed;

        private static Vector2 Px(float nx, float ny)
        {
            var w = Application.Current.Window;
            return new Vector2(nx * w.Width, ny * w.Height);
        }
        private static void Tap(KeyCode k) => Input.SimulateKey(k, true);
        private static void Release(KeyCode k) => Input.SimulateKey(k, false);
        private static void Click(Vector2 p, int f, int frame, int button = 0)
        {
            if (frame == f) Input.SimulateMousePosition(p);
            if (frame == f + 1) Input.SimulateMouseButton(button, true);
            if (frame == f + 2) Input.SimulateMouseButton(button, false);
        }
        private static void Type(string text, int f, int frame)
        {
            if (frame != f) return;
            foreach (char c in text) Input.SimulateChar(c);
        }

        public void Update()
        {
            _frame++;
            _ed ??= Editor;
            switch (scriptName)
            {
                case "play":
                    if (_frame == 3) _ed.SelectClip("M1887_Fire");
                    if (_frame == 5) Tap(KeyCode.Space); if (_frame == 6) Release(KeyCode.Space);
                    break;
                case "edit":
                    // open GlockFire, step to frame 3, click a palette sprite (inserts a key), add an event, extend the length, save as a copy
                    if (_frame == 3) _ed.SelectClip("GlockFire");
                    if (_frame == 5 || _frame == 7 || _frame == 9) Tap(KeyCode.RightArrow);
                    if (_frame == 6 || _frame == 8 || _frame == 10) Release(KeyCode.RightArrow);
                    Click(Px(0.80f, 0.70f), 12, _frame);                       // first palette sprite
                    if (_frame == 16) Tap(KeyCode.RightBracket); if (_frame == 17) Release(KeyCode.RightBracket);
                    if (_frame == 18) Tap(KeyCode.RightBracket); if (_frame == 19) Release(KeyCode.RightBracket);
                    if (_frame == 22) Tap(KeyCode.E); if (_frame == 23) Release(KeyCode.E);
                    Type("Reload", 25, _frame);
                    if (_frame == 27) Tap(KeyCode.Return); if (_frame == 28) Release(KeyCode.Return);
                    if (_frame == 32) { Tap(KeyCode.LeftControl); Tap(KeyCode.D); }
                    if (_frame == 33) { Release(KeyCode.D); Release(KeyCode.LeftControl); }
                    if (_frame == 36) Tap(KeyCode.Return); if (_frame == 37) Release(KeyCode.Return);   // accept "GlockFire_copy"
                    if (_frame == 40) { Tap(KeyCode.LeftControl); Tap(KeyCode.S); }
                    if (_frame == 41) { Release(KeyCode.S); Release(KeyCode.LeftControl); }
                    break;
                case "controller":
                    if (_frame == 3) _ed.SelectController("LeftFist");
                    if (_frame == 4) Tap(KeyCode.Tab); if (_frame == 5) Release(KeyCode.Tab);
                    if (_frame == 8) Tap(KeyCode.P); if (_frame == 9) Release(KeyCode.P);
                    // fire the Jab trigger by clicking its value cell (first parameter row)
                    Click(Px(0.27f, 0.815f), 20, _frame);
                    break;
                case "undo":
                    // enabled key, event, delete the event, undo twice, redo once → expect 1 enabled key, 0 events... then redo → 1 event
                    if (_frame == 3) _ed.SelectClip("MG_Fire");
                    if (_frame == 5) Tap(KeyCode.N); if (_frame == 6) Release(KeyCode.N);
                    if (_frame == 8) Tap(KeyCode.E); if (_frame == 9) Release(KeyCode.E);
                    if (_frame == 12) Tap(KeyCode.Return); if (_frame == 13) Release(KeyCode.Return);
                    if (_frame == 16) Tap(KeyCode.Delete); if (_frame == 17) Release(KeyCode.Delete);
                    if (_frame == 20) Debug.LogWarning($"[test] after delete: enabled={_ed.Clip.enabledKeys.Count} events={_ed.Clip.events.Count}");
                    if (_frame == 22 || _frame == 26) { Tap(KeyCode.LeftControl); Tap(KeyCode.Z); }
                    if (_frame == 23 || _frame == 27) { Release(KeyCode.Z); Release(KeyCode.LeftControl); }
                    if (_frame == 30) Debug.LogWarning($"[test] after 2 undo: enabled={_ed.Clip.enabledKeys.Count} events={_ed.Clip.events.Count}");
                    if (_frame == 32) { Tap(KeyCode.LeftControl); Tap(KeyCode.Y); }
                    if (_frame == 33) { Release(KeyCode.Y); Release(KeyCode.LeftControl); }
                    if (_frame == 36) Debug.LogWarning($"[test] after redo: enabled={_ed.Clip.enabledKeys.Count} events={_ed.Clip.events.Count} dirty={_ed.Dirty}");
                    break;
                case "help":
                    if (_frame == 3) Tap(KeyCode.F1); if (_frame == 4) Release(KeyCode.F1);
                    break;
                case "drag":
                    // drag the second sprite key of GlockFire two frames to the right
                    if (_frame == 3) _ed.SelectClip("GlockFire");
                    if (_frame == 10) Input.SimulateMousePosition(Px(0.22f, 0.20f));
                    if (_frame == 11) Input.SimulateMouseButton(0, true);
                    if (_frame >= 12 && _frame <= 30) Input.SimulateMousePosition(Px(0.22f + (_frame - 12) * 0.004f, 0.20f));
                    if (_frame == 31) Input.SimulateMouseButton(0, false);
                    break;
            }
        }
    }
}
