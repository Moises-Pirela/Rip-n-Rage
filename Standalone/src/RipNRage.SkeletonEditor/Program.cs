using System;
using RipNRage.Engine;
using RipNRage.Engine.Destruction;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;

namespace RipNRage.SkeletonEditor
{
    /// <summary>
    /// Rip-n-Rage sprite destruction skeleton editor (separate executable).
    ///   RipNRageSkeletonEditor [--skeletons dir] [--skeleton name] [--assets dir] [--width W --height H]
    ///                          [--frames N --fixed-dt s --screenshot out.png]
    /// Edits skeletons/*.json: the parts a sprite is sliced into when it is destroyed (polygons, pivots, parent
    /// tree, masses, break impulses) with a live physics preview. The game watches that folder and reloads saved
    /// changes immediately.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions { Title = "Rip-n-Rage Skeleton Editor", Width = 1280, Height = 720, Mute = true };
            string skeletonName = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                switch (a)
                {
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--skeletons": GameAssets.SkeletonsPath = Next(); break;
                    case "--animations": GameAssets.AnimationsPath = Next(); break;
                    case "--skeleton": skeletonName = Next(); break;
                    case "--width": options.Width = int.Parse(Next()); break;
                    case "--height": options.Height = int.Parse(Next()); break;
                    case "--frames": options.MaxFrames = int.Parse(Next()); break;
                    case "--screenshot": options.ScreenshotPath = Next(); break;
                    case "--shot-interval": options.ScreenshotInterval = int.Parse(Next()); break;
                    case "--fixed-dt": options.FixedFrameDelta = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--no-postfx": options.DisablePostFx = true; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--help": case "-h": Console.WriteLine("RipNRageSkeletonEditor [--skeletons dir] [--skeleton name]"); return 0;
                    default:
                        if (a.StartsWith("--")) { Console.WriteLine("Unknown option " + a); return 1; }
                        skeletonName = a; break;
                }
            }

            try
            {
                using var app = new Application(options);
                GameAssets.Load();
                Materials.Load();

                BuildPreviewScene(out var cam, out var anchor);
                var edGo = new GameObject("SkeletonEditor");
                var editor = edGo.AddComponent<SkeletonEditor>();
                editor.cam = cam; editor.anchor = anchor;
                editor.startSkeleton = skeletonName;
                app.Run();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Fatal: " + e);
                return 2;
            }
        }

        /// <summary>A plank floor with collision, a back wall and a camera: the physics preview runs here.</summary>
        private static void BuildPreviewScene(out Camera cam, out Transform anchor)
        {
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.position = new Vector3(0f, 1.6f, -5f);
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
            Camera.main = cam;

            var anchorGo = new GameObject("Anchor");
            anchorGo.transform.position = new Vector3(0f, 0f, 0f);
            anchor = anchorGo.transform;

            var floor = new GameObject("Floor");
            floor.layer = Layers.Walkable;
            var fmr = floor.AddComponent<MeshRenderer>();
            fmr.mesh = Mesh.CreatePlane(Vector3.zero, 40f, 40f, 0.25f);
            fmr.texture = ProceduralTextures.WoodPlanks();
            fmr.occluder = true;
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World, floor.transform.position + new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(40f, 1f, 40f), Layers.Walkable);

            var wall = new GameObject("BackWall");
            wall.layer = Layers.Walkable;
            var wmr = wall.AddComponent<MeshRenderer>();
            wmr.mesh = Mesh.CreateBox(new Vector3(0f, 2.5f, 6f), new Vector3(30f, 5f, 1f), 0.25f);
            wmr.texture = GameAssets.Texture13;
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World, wall.transform.position + new Vector3(0f, 2.5f, 6f), Quaternion.identity, new Vector3(30f, 5f, 1f), Layers.Walkable);

            var crate = new GameObject("Crate");
            crate.layer = Layers.Walkable;
            crate.transform.position = new Vector3(2.5f, 0.5f, 1.5f);
            var cmr = crate.AddComponent<MeshRenderer>();
            cmr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one, 1f);
            cmr.texture = GameAssets.OrangeProto;
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World, crate.transform.position + Vector3.zero, Quaternion.identity, Vector3.one, Layers.Walkable);
            RenderSettings.drawSky = true;
        }
    }
}
