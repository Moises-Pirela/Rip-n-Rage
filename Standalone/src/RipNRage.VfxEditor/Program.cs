using System;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;

namespace RipNRage.VfxEditor
{
    /// <summary>
    /// Rip-n-Rage particle effect editor (separate executable).
    ///   RipNRageVfxEditor [--vfx dir] [--effect name] [--assets dir] [--width W --height H]
    ///                     [--frames N --fixed-dt s --screenshot out.png --script name]
    /// Edits the JSON effects in vfx/ (created from the code-built defaults when the folder is empty); the game
    /// watches that folder and reloads saved changes live.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions { Title = "Rip-n-Rage VFX Editor", Width = 1280, Height = 720, Mute = true };
            string effectName = null, script = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                switch (a)
                {
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--vfx": GameAssets.VfxPath = Next(); break;
                    case "--animations": GameAssets.AnimationsPath = Next(); break;
                    case "--effect": effectName = Next(); break;
                    case "--width": options.Width = int.Parse(Next()); break;
                    case "--height": options.Height = int.Parse(Next()); break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--shot-interval": options.ScreenshotInterval = int.Parse(Next()); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--shaders": options.ShaderDir = Next(); break;
                    case "--script": script = Next(); break;
                    case "--no-postfx": options.DisablePostFx = true; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--help": case "-h": Console.WriteLine("RipNRageVfxEditor [--vfx dir] [--effect name]"); return 0;
                    default:
                        if (a.StartsWith("--")) { Console.WriteLine("Unknown option " + a); return 1; }
                        effectName = a; break;
                }
            }

            try
            {
                using var app = new Application(options);
                GameAssets.Load();      // atlases (incl. the procedural Particles sheet), default effects, vfx/ folder
                Materials.Load();

                var rig = BuildPreviewScene();
                var edGo = new GameObject("VfxEditor");
                var editor = edGo.AddComponent<VfxEditor>();
                editor.rig = rig;
                editor.startEffect = effectName;
                if (script != null) edGo.AddComponent<VfxEditorScript>().scriptName = script;
                app.Run();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Fatal: " + e);
                return 2;
            }
        }

        /// <summary>Plank floor, a concrete wall facing the camera, a wooden crate and a metal post; all with colliders so particles bounce and clicks place the effect.</summary>
        private static PreviewRig BuildPreviewScene()
        {
            var rig = new PreviewRig();
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.position = new Vector3(0f, 1.6f, -2.5f);
            rig.camera = camGo.AddComponent<Camera>();
            rig.camera.fieldOfView = 60f; rig.camera.nearClipPlane = 0.03f; rig.camera.farClipPlane = 400f;
            Camera.main = rig.camera;

            var floor = new GameObject("Floor");
            floor.layer = Layers.Walkable;
            var fmr = floor.AddComponent<MeshRenderer>();
            fmr.mesh = Mesh.CreatePlane(Vector3.zero, 40f, 40f, 0.25f);
            fmr.texture = ProceduralTextures.WoodPlanks();
            fmr.occluder = true;
            var fcol = floor.AddComponent<BoxCollider>(); fcol.size = new Vector3(40f, 1f, 40f); fcol.center = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood;

            var wall = new GameObject("Wall");
            wall.layer = Layers.Walkable;
            var wmr = wall.AddComponent<MeshRenderer>();
            wmr.mesh = Mesh.CreateBox(new Vector3(0f, 2f, 4.5f), new Vector3(14f, 4f, 1f), 0.5f);
            wmr.texture = GameAssets.Texture13;
            wmr.color = new Color(0.8f, 0.8f, 0.82f, 1f);
            wmr.occluder = true;
            var wcol = wall.AddComponent<BoxCollider>(); wcol.center = new Vector3(0f, 2f, 4.5f); wcol.size = new Vector3(14f, 4f, 1f);
            wall.AddComponent<SurfaceMaterial>().surface = Surfaces.Concrete;

            var crate = new GameObject("Crate");
            crate.layer = Layers.Walkable;
            crate.transform.position = new Vector3(2.6f, 0.6f, 2.2f);
            var cmr = crate.AddComponent<MeshRenderer>();
            cmr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one * 1.2f, 1f / 1.2f);
            cmr.texture = GameAssets.OrangeProto;
            var ccol = crate.AddComponent<BoxCollider>(); ccol.size = Vector3.one * 1.2f;
            crate.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood;

            var post = new GameObject("Post");
            post.layer = Layers.Walkable;
            post.transform.position = new Vector3(-2.4f, 1.2f, 2.6f);
            var pmr = post.AddComponent<MeshRenderer>();
            pmr.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.3f, 2.4f, 0.3f), 1f);
            pmr.texture = GameAssets.Texture13; pmr.color = new Color(0.35f, 0.35f, 0.38f, 1f);
            var pcol = post.AddComponent<BoxCollider>(); pcol.size = new Vector3(0.3f, 2.4f, 0.3f);
            post.AddComponent<SurfaceMaterial>().surface = Surfaces.Metal;

            // dummy "projectile" that flies a circle for trail-type (open ended) effects
            var dummy = new GameObject("TrailDummy");
            rig.trailDummy = dummy.AddComponent<CircleFlyer>();
            dummy.SetActive(false);

            RenderSettings.drawSky = true;
            return rig;
        }
    }

    /// <summary>Scene objects the editor drives.</summary>
    public sealed class PreviewRig
    {
        public Camera camera;
        public CircleFlyer trailDummy;
    }

    /// <summary>Moves its object around a horizontal circle (stand-in for a rocket when previewing trails).</summary>
    public sealed class CircleFlyer : Behaviour
    {
        public Vector3 center = new Vector3(0f, 1.6f, 1.5f);
        public float radius = 1.8f;
        public float speed = 5f; // m/s
        private float _angle;

        protected override void Update()
        {
            _angle += speed / Mathf.Max(0.1f, radius) * Time.deltaTime;
            Vector3 p = center + new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * radius;
            Vector3 tangent = new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
            transform.position = p;
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }
    }

    /// <summary>Scripted sessions for screenshots / smoke tests.</summary>
    public sealed class VfxEditorScript : Behaviour
    {
        public string scriptName = "";
        private int _frame;
        private VfxEditor _ed;

        protected override void Update()
        {
            _frame++;
            _ed ??= GetComponent<VfxEditor>();
            if (_ed == null) return;
            switch (scriptName)
            {
                case "tour":
                    // cycle through a few effects, one every 90 frames
                    if (_frame == 5) _ed.Select("impact_concrete");
                    if (_frame == 95) _ed.Select("blood_hit");
                    if (_frame == 185) _ed.Select("impact_metal");
                    if (_frame == 275) _ed.Select("explosion_rocket");
                    break;
                case "explosion":
                    if (_frame == 5) _ed.Select("explosion_rocket");
                    if (_frame == 8) _ed.SetSurfaceMode(1);
                    break;
                case "trail":
                    if (_frame == 5) _ed.Select("rocket_trail");
                    break;
                case "tracer":
                    if (_frame == 5) _ed.Select("tracer_heavy");
                    break;
                case "help":
                    if (_frame == 3) Input.SimulateKey(KeyCode.F1, true);
                    if (_frame == 4) Input.SimulateKey(KeyCode.F1, false);
                    break;
            }
        }
    }
}
