using System.Collections.Generic;
using System.IO;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Game.Content.Level;
using RipNRage.Engine.Ecs;
using EcsPhysics = RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Rebuilds the TestArea scene: the dock level (the TrenchBroom export <c>dock.obj</c> that the
    /// scene's Dock.blend came from, imported like Unity does: X mirrored, 0.25 units per map unit,
    /// placed at y = -3.09), the sand/water planes, the ---DungeonMaster--- managers, the sky rotator,
    /// ammo pickups and a few AR_Grunt riflemen.
    /// </summary>
    public static class TestArea
    {
        public const float MapScale = 0.25f;
        public static readonly Vector3 DockOffset = new Vector3(0f, -3.09f, 0f);

        /// <summary>ProjectSettings/DynamicsManager.asset m_LayerCollisionMatrix (row = layer, bit = other layer).</summary>
        private static readonly uint[] LayerCollisionMatrix =
        {
            0xffffffff, 0xffff3fbf, 0xffff3fbf, 0xffff7bf7, 0xffff3fff, 0xffff3fbf, 0xffff1119, 0xffff303f,
            0xffff3f7f, 0xffff3f3f, 0xffff3f37, 0xffff3f3f, 0xffff3fff, 0xffff5fbf, 0xffff2009, 0xffff0001,
        };

        /// <summary>
        /// Builds the scene from a .map level; with no level the legacy hard-coded arena is used.
        /// With <paramref name="spawnPlayer"/> false no DungeonMaster (and therefore no player) is created;
        /// instead a <see cref="MenuCamera"/> looks over the level from the player start (main menu backdrop).
        /// </summary>
        public static void Build(string levelPath = null, bool spawnPlayer = true)
        {
            GameAssets.Load();
            Materials.Load();
            Physics.SetCollisionMatrix(LayerCollisionMatrix);
            // sun, sky, fog and shadows come from tunables.cfg ([lighting] / [sky] / [fog] / [shadows] / [volumetric_fog])

            if (levelPath != null)
            {
                var level = LevelLoader.Load(levelPath);
                Debug.LogWarning($"Level: {levelPath} ({level.document.Brushes.Count} brushes, {level.document.entities.Count} entities)");
                new GameObject("SkyRotator").AddComponent<SkyboxRotator>();
                FallRespawn.KillY = level.killY;
                FallRespawn.SpawnPosition = level.playerStart;
                FallRespawn.SpawnRotation = level.playerRotation;
                if (level.fallMode.HasValue) FallRespawn.Mode = level.fallMode.Value;
                if (spawnPlayer) BuildDungeonMaster(level.playerStart, level.playerRotation);
                else MenuCamera.Create(level.playerStart, level.playerRotation);
                return;
            }

            BuildEnvironment();
            BuildProps();
            FallRespawn.KillY = -1.5f; // the legacy arena's water surface is at -0.5
            FallRespawn.SpawnPosition = new Vector3(0f, 2.6f, 8f);
            FallRespawn.SpawnRotation = Quaternion.Euler(0f, 180f, 0f);
            if (spawnPlayer) BuildDungeonMaster(new Vector3(0f, 2.6f, 8f), Quaternion.Euler(0f, 180f, 0f));
            else MenuCamera.Create(new Vector3(0f, 2.6f, 8f), Quaternion.Euler(0f, 180f, 0f));
            BuildPickups();
            BuildEnemies();
        }

        // ------------------------------------------------------------------ environment

        private static void BuildEnvironment()
        {
            var env = new GameObject("---ENV---");

            // DOCK (Dock.blend instance at (0, -3.09, 0))
            string objPath = Application.AssetPath("Levels/dock.obj");
            var dock = new GameObject("DOCK");
            dock.transform.SetParent(env.transform, false);
            dock.transform.localPosition = DockOffset;
            dock.layer = Layers.Walkable;
            dock.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood; // the box colliders below inherit it (GetComponentInParent)
            var mr = dock.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.LoadObj(objPath, MapScale, 1f / 64f, mirrorX: true);
            mr.texture = ProceduralTextures.WoodPlanks(); // Dock.blend's materials were not exported; generated planks instead
            mr.uvScale = new Vector2(2f, 2f);             // one 64-unit brush face = 2 texture tiles (16 planks)
            mr.color = Color.white;
            BuildDockCollision(mr.mesh);

            // SkyRotator
            new GameObject("SkyRotator").AddComponent<SkyboxRotator>();

            // WaterPlane (x 474.6, y -0.5) scale 173.21 x 100 of Unity's 10x10 plane, SandPlane 15 below.
            var water = new GameObject("WaterPlane");
            water.transform.SetParent(env.transform, false);
            water.transform.localPosition = new Vector3(474.6f, -0.5f, 0f);
            water.layer = Layers.Water;
            var waterMr = water.AddComponent<MeshRenderer>();
            waterMr.mesh = Mesh.CreatePlane(Vector3.zero, 1732.1f, 1000f, 1f / 16f);
            waterMr.texture = Texture2D.White;
            waterMr.color = Color.white;
            waterMr.transparent = true;
            waterMr.castsShadow = false;
            waterMr.material = Materials.Water; // animated water shader (shaders/water.glsl)
            // the water volume is an ECS trigger; TriggerVolume relays its overlaps to EnterWaterCollision
            var waterVolume = EcsPhysics.PhysicsEntities.StaticBox(World, water.transform.position + new Vector3(0, -50f, 0), Quaternion.identity, new Vector3(1732.1f, 100f, 1000f), Layers.Water, trigger: true);
            RipNRage.Game.Ecs.TriggerVolume.Attach(water, waterVolume);
            water.AddComponent<EnterWaterCollision>();

            var sand = new GameObject("SandPlane");
            sand.transform.SetParent(water.transform, false);
            sand.transform.localPosition = new Vector3(0f, -15f, 0f);
            sand.layer = Layers.Walkable;
            sand.AddComponent<SurfaceMaterial>().surface = Surfaces.Sand;
            var sandMr = sand.AddComponent<MeshRenderer>();
            sandMr.mesh = Mesh.CreatePlane(Vector3.zero, 1732.1f, 1000f, 1f / 4f);
            sandMr.texture = ProceduralTextures.Sand();
            sandMr.color = Color.white;
            sandMr.castsShadow = false;
            EcsPhysics.PhysicsEntities.StaticBox(World, sand.transform.position + new Vector3(0, -1f, 0), Quaternion.identity, new Vector3(1732.1f, 2f, 1000f), Layers.Walkable);
        }

        /// <summary>
        /// The dock collides as a triangle mesh (the rendered <c>dock.obj</c> itself), so the slanted pier and every
        /// rotated brush are exact — the old grid of small boxes that approximated them is gone.
        /// </summary>
        private static void BuildDockCollision(Mesh mesh)
        {
            var tris = EcsPhysics.PhysicsAssets.AddMesh("dock", mesh.vertices, mesh.triangles);
            EcsPhysics.PhysicsEntities.StaticMesh(World, tris, LocalTransform.At(DockOffset), Layers.Walkable);
        }

        private static World World => Application.Current.World;

        // ------------------------------------------------------------------ props & lights

        /// <summary>Lamp posts (point lights) and crates along the pier — shadow casters and light sources for the new lighting.</summary>
        private static void BuildProps()
        {
            float floor = DockOffset.y + 16f * MapScale; // 0.91
            var props = new GameObject("---PROPS---");
            foreach (float z in new[] { -28f, -62f, -96f })
                foreach (float x in new[] { -13.5f, 13.5f })
                    LampPost(props, new Vector3(x, floor, z));

            var crateTex = GameAssets.OrangeProto;
            Crate(props, new Vector3(6f, floor, -34f), 2.2f, crateTex);
            Crate(props, new Vector3(8.4f, floor, -35.5f), 1.6f, crateTex);
            Crate(props, new Vector3(7.2f, floor + 2.2f, -34.6f), 1.4f, crateTex);
            Crate(props, new Vector3(-9f, floor, -52f), 2.6f, GameAssets.GreenProto);
            Crate(props, new Vector3(-5f, floor, -80f), 2f, GameAssets.PurpleProto);
            Crate(props, new Vector3(10f, floor, -100f), 2.4f, GameAssets.RedProto);

            // rigid bodies: a stack of loose crates and a couple of barrels near the start — push them, shoot them, rocket them
            var loose = GameAssets.OrangeProto;
            PhysicsCrate(props, new Vector3(-3f, floor + 0.5f, -6f), 1f, 12f, loose);
            PhysicsCrate(props, new Vector3(-3f, floor + 1.55f, -6f), 1f, 12f, loose);
            PhysicsCrate(props, new Vector3(-3f, floor + 2.6f, -6f), 1f, 12f, loose);
            PhysicsCrate(props, new Vector3(-1.6f, floor + 0.4f, -7.2f), 0.8f, 6f, GameAssets.GreenProto);
            PhysicsCrate(props, new Vector3(3.5f, floor + 0.6f, -9f), 1.2f, 25f, GameAssets.PurpleProto);
            PhysicsBall(props, new Vector3(1f, floor + 1.2f, -12f), 0.45f, 8f, GameAssets.RedProto);
            PhysicsBall(props, new Vector3(-6f, floor + 1.2f, -16f), 0.35f, 4f, GameAssets.RedProto);

            // the joint playground (door, pendulum, seesaw...) returns as ECS prefabs with joints (layer 2.5)
        }

        /// <summary>A crate that is an ECS rigid body (renders through the ECS too).</summary>
        public static Entity PhysicsCrate(GameObject parent, Vector3 center, float size, float mass, Texture2D tex) =>
            DynamicProp(center, new EcsPhysics.ColliderShape { Kind = EcsPhysics.ShapeKind.Box, Size = Vector3.one * size }, mass, 0.55f, 0.05f,
                Mesh.CreateBox(Vector3.zero, Vector3.one * size, 1f / size), tex, Materials.Crate, CollisionDetectionMode.Continuous);

        /// <summary>A ball that is an ECS rigid body (sphere collider), rendered as a small box mesh.</summary>
        public static Entity PhysicsBall(GameObject parent, Vector3 center, float radius, float mass, Texture2D tex) =>
            DynamicProp(center, new EcsPhysics.ColliderShape { Kind = EcsPhysics.ShapeKind.Sphere, Radius = radius }, mass, 0.4f, 0.45f,
                Mesh.CreateBox(Vector3.zero, Vector3.one * (radius * 1.6f), 1f / radius), tex, null, CollisionDetectionMode.ContinuousDynamic);

        private static Entity DynamicProp(Vector3 center, EcsPhysics.ColliderShape shape, float mass, float friction, float restitution, Mesh mesh, Texture2D tex, Material material, CollisionDetectionMode ccd)
        {
            var world = World;
            var e = EcsPhysics.PhysicsEntities.Dynamic(world, LocalTransform.At(center), shape, mass);
            var cmd = world.Commands;
            var body = new EcsPhysics.BodyConfig { mass = mass, collisionDetection = ccd }.Bake();
            cmd.Add(e, body);
            cmd.Add(e, new EcsPhysics.SurfaceMaterial { StaticFriction = friction, DynamicFriction = friction, Restitution = restitution, FrictionCombine = CombineMode.Average, RestitutionCombine = CombineMode.Maximum });
            cmd.Add(e, new MeshRender
            {
                Mesh = RenderAssets.Meshes.Intern(mesh), Texture = RenderAssets.Textures.Intern(tex ?? Texture2D.White),
                Material = RenderAssets.Materials.Intern(material), Color = Color.white, UvScale = Vector2.one, CastsShadow = true, Layer = RenderLayer.World,
            });
            return e;
        }

        private static void LampPost(GameObject parent, Vector3 basePos)
        {
            var post = new GameObject("LampPost");
            post.transform.SetParent(parent.transform, false);
            post.transform.position = basePos;
            post.layer = Layers.Walkable;
            post.AddComponent<SurfaceMaterial>().surface = Surfaces.Metal;
            var pole = post.AddComponent<MeshRenderer>();
            pole.mesh = Mesh.CreateBox(new Vector3(0, 2.6f, 0), new Vector3(0.28f, 5.2f, 0.28f), 1f);
            pole.texture = GameAssets.Texture13;
            pole.color = new Color(0.35f, 0.35f, 0.38f, 1f);
            EcsPhysics.PhysicsEntities.StaticBox(World, basePos + new Vector3(0, 2.6f, 0), Quaternion.identity, new Vector3(0.4f, 5.2f, 0.4f), Layers.Walkable);

            var head = new GameObject("LampHead");
            head.transform.SetParent(post.transform, false);
            head.transform.localPosition = new Vector3(0, 5.4f, 0);
            var headMr = head.AddComponent<MeshRenderer>();
            headMr.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f), 1f);
            headMr.texture = Texture2D.White;
            headMr.color = new Color(1f, 0.85f, 0.55f, 1f);
            headMr.emissive = 0f;
            headMr.castsShadow = false;
            headMr.material = Materials.Lamp; // pulsing emissive (shaders/mesh_emissive_pulse.glsl)
            var light = head.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.5f, 1f);
            light.intensity = 1.4f;
            light.range = 24f;
        }

        private static void Crate(GameObject parent, Vector3 basePos, float size, Texture2D tex)
        {
            var go = new GameObject("Crate");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos + new Vector3(0, size * 0.5f, 0);
            go.layer = Layers.Walkable;
            go.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one * size, 1f / size);
            mr.texture = tex;
            mr.material = Materials.Crate;
            EcsPhysics.PhysicsEntities.StaticBox(World, go.transform.position, Quaternion.identity, Vector3.one * size, Layers.Walkable);
        }

        // ------------------------------------------------------------------ managers

        private static void BuildDungeonMaster(Vector3 start, Quaternion rotation)
        {
            var root = new GameObject("---DungeonMaster---");
            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = start;
            spawn.transform.rotation = rotation;

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var lmGo = new GameObject("LevelManager");
            lmGo.transform.SetParent(gmGo.transform, false);
            lmGo.AddComponent<LevelManager>().playerStart = spawn.transform;
            // GameManager.Awake starts the init coroutine, so the children must exist first.
            var gm = gmGo.AddComponent<GameManager>();
            gm.playerPrefab = PlayerPrefab.Player;
        }

        // ------------------------------------------------------------------ pickups & enemies

        private static void BuildPickups()
        {
            float y = DockOffset.y + 16f * MapScale + 0.9f;
            Prefabs.ShellAmmoPickup.Instantiate(new Vector3(-3f, y, -20f), Quaternion.identity);
            Prefabs.HeavyAmmoPickup.Instantiate(new Vector3(3f, y, -40f), Quaternion.identity);
            Prefabs.LightAmmoPickup.Instantiate(new Vector3(-4f, y, -60f), Quaternion.identity);
            Prefabs.ExplosiveAmmoPickup.Instantiate(new Vector3(4f, y, -90f), Quaternion.identity);
        }

        private static void BuildEnemies()
        {
            float y = DockOffset.y + 16f * MapScale;
            Prefabs.SpawnRifleman(new Vector3(-5f, y, -16f), Quaternion.Euler(0, 0, 0));
            Prefabs.SpawnRifleman(new Vector3(2f, y, -75f), Quaternion.Euler(0, 180, 0));
            Prefabs.SpawnRifleman(new Vector3(-6f, y, -110f), Quaternion.Euler(0, 180, 0));
            Prefabs.SpawnRifleman(new Vector3(60f, y, -105f), Quaternion.Euler(0, 90, 0));
        }
    }
}
