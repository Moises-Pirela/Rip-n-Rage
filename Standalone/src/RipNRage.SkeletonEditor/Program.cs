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

                BuildPreviewScene(out var cam, out var lens);
                var editor = RipNRage.Engine.Graphics.UiScreens.Add(new SkeletonEditor
                    { cam = cam, lens = lens, anchorPosition = Vector3.zero, startSkeleton = skeletonName });
                editor.Start();
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
        private static void BuildPreviewScene(out RipNRage.Engine.Graphics.CameraView cam, out RipNRage.Engine.Ecs.Entity lens)
        {
            var world = Application.Current.World;
            cam = new RipNRage.Engine.Graphics.CameraView { Position = new Vector3(0f, 1.6f, -5f), fieldOfView = 60f, nearClipPlane = 0.05f, farClipPlane = 300f };
            lens = world.CreateEntity();
            world.Add(lens, RipNRage.Engine.Ecs.Transforms.LocalTransform.At(cam.Position));
            world.Add(lens, new RipNRage.Engine.Ecs.Rendering.CameraLens { FieldOfView = 60f, Near = 0.05f, Far = 300f });

            void StaticMesh(Mesh mesh, Texture2D tex, Vector3 position, bool occluder)
            {
                var e = world.CreateEntity();
                world.Add(e, RipNRage.Engine.Ecs.Transforms.LocalTransform.At(position));
                world.Add(e, new RipNRage.Engine.Ecs.Rendering.MeshRender
                {
                    Mesh = RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Intern(mesh),
                    Texture = RipNRage.Engine.Ecs.Rendering.RenderAssets.Textures.Intern(tex),
                    UvScale = Vector2.one, Color = Color.white, CastsShadow = true, Occluder = occluder,
                    Layer = RipNRage.Engine.Graphics.RenderLayer.World,
                });
            }
            StaticMesh(Mesh.CreatePlane(Vector3.zero, 40f, 40f, 0.25f), ProceduralTextures.WoodPlanks(), Vector3.zero, true);
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(world, new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(40f, 1f, 40f), Layers.Walkable);
            StaticMesh(Mesh.CreateBox(new Vector3(0f, 2.5f, 6f), new Vector3(30f, 5f, 1f), 0.25f), GameAssets.Texture13, Vector3.zero, false);
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(world, new Vector3(0f, 2.5f, 6f), Quaternion.identity, new Vector3(30f, 5f, 1f), Layers.Walkable);
            StaticMesh(Mesh.CreateBox(Vector3.zero, Vector3.one, 1f), GameAssets.OrangeProto, new Vector3(2.5f, 0.5f, 1.5f), false);
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(world, new Vector3(2.5f, 0.5f, 1.5f), Quaternion.identity, Vector3.one, Layers.Walkable);
            RenderSettings.drawSky = true;
        }
    }
}
