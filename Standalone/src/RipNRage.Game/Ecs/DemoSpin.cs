using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.Physics;

namespace RipNRage.Game.Ecs
{
    /// <summary>Constant rotation in local space (degrees per second around X / Y / Z). Used by the --ecs-demo scene.</summary>
    public struct Spin : IComponent { public Vector3 DegreesPerSecond; }

    public sealed class SpinConfig : ComponentConfig<Spin>
    {
        public float[] degreesPerSecond = { 0, 90, 0 };
        public override Spin Bake() => new Spin { DegreesPerSecond = new Vector3(degreesPerSecond[0], degreesPerSecond[1], degreesPerSecond[2]) };
    }

    [Reads(typeof(Spin)), Writes(typeof(LocalTransform))]
    public sealed class SpinSystem : SystemBase, ITick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().WithReadOnly<Spin>().With<LocalTransform>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var c in _q)
            {
                var spin = c.Get<Spin>(); var lt = c.Get<LocalTransform>();
                for (int i = 0; i < c.Length; i++)
                    lt[i].Rotation = lt[i].Rotation * Quaternion.Euler(spin[i].DegreesPerSecond * ctx.DeltaTime);
            }
        }
    }

    /// <summary>--ecs-demo: spawns the prefabs/demo scene into the running level, entirely through the ECS.</summary>
    public static class EcsDemo
    {
        public static void Spawn(World world, string prefabDir)
        {
            var lib = new PrefabLibrary();
            lib.LoadDirectory(prefabDir);
            var cmd = world.Commands;
            lib.Get("ecs_demo_camera").Instantiate(world);
            lib.Get("ecs_demo_light").Instantiate(world);
            var spinner = lib.Get("ecs_demo_spinner").Instantiate(world);
            var moon = lib.Get("ecs_demo_moon").Instantiate(world);
            cmd.Add(moon, new Parent { Value = spinner });
            var pickup = lib.Get("ecs_demo_pickup").Instantiate(world);
            cmd.Add(pickup, new Parent { Value = moon });
            var crates = new Entity[4]; // a small stack: lands on the dock's legacy geometry via the one simulation
            bool hasCrate = lib.Contains("ecs_demo_crate");
            if (hasCrate) for (int i = 0; i < crates.Length; i++) crates[i] = lib.Get("ecs_demo_crate").Instantiate(world);
            world.Sync();
            if (hasCrate) for (int i = 0; i < crates.Length; i++)
                world.Get<LocalTransform>(crates[i]) = new LocalTransform
                { Position = new Vector3(2f + 0.05f * i, 2.5f + 0.75f * i, 6f), Rotation = Quaternion.Euler(0, 12f * i, 0), Scale = new Vector3(0.6f, 0.6f, 0.6f) };
            if (lib.Contains("ecs_demo_prop")) SpawnPlayground(world, lib);
            Debug.LogWarning($"[ecs-demo] {world.EntityCount} entities in {world.Archetypes.Count} archetypes from {prefabDir}");
        }

        /// <summary>
        /// The joint playground (what PhysicsPlayground built on legacy joints): a sprung door, a pendulum, a bungee
        /// crate, a seesaw with a crate dropped on one end, a motorised windmill, a patrolling piston, a hanging chain
        /// and a breakable shelf with a crate dropped on it. Machines are prefabs; the chain and the patrol need
        /// entity references, so they are wired here.
        /// </summary>
        private static void SpawnPlayground(World world, PrefabLibrary lib)
        {
            foreach (var name in new[] { "ecs_demo_door", "ecs_demo_pendulum", "ecs_demo_bungee", "ecs_demo_seesaw", "ecs_demo_windmill", "ecs_demo_shelf" })
                lib.Get(name).Instantiate(world);
            var piston = lib.Get("ecs_demo_piston").Instantiate(world);
            var seesawLoad = lib.Get("ecs_demo_crate").Instantiate(world);
            var shelfLoad = lib.Get("ecs_demo_crate").Instantiate(world);
            var links = new Entity[5];
            for (int i = 0; i < links.Length; i++) links[i] = lib.Get("ecs_demo_link").Instantiate(world);
            world.Sync();

            var pj = world.Get<Joint>(piston);
            Vector3 pistonAt = world.Get<LocalTransform>(piston).Position;
            world.Add(piston, new SliderPatrol { Origin = pistonAt, Axis = Vector3.right, Lower = pj.LowerLimit, Upper = pj.UpperLimit });
            world.Get<LocalTransform>(seesawLoad).Position = new Vector3(3.6f, 4f, 12f);
            world.Get<LocalTransform>(shelfLoad).Position = new Vector3(5f, 3.8f, 16.5f);

            // chain: links 0.6 m long hanging from a hook at (-4.5, 5.6, 16), each ball-jointed to the one above
            Vector3 hook = new Vector3(-4.5f, 5.6f, 16f);
            for (int i = 0; i < links.Length; i++)
            {
                var centre = hook + Vector3.down * (0.3f + 0.62f * i);
                world.Get<LocalTransform>(links[i]).Position = centre;
                var j = new JointConfig { kind = JointKind.Ball, anchor = new[] { 0f, 0.5f, 0f }, breakForce = 2000f }.Bake(); // anchor = top of the (scaled) link
                if (i == 0) { j.AutoConnectedAnchor = false; j.ConnectedAnchor = hook; }
                else j.Connected = links[i - 1];
                world.Add(links[i], j);
            }
            world.Get<BodyVelocity>(links[links.Length - 1]) = new BodyVelocity { Linear = new Vector3(3f, 0f, 0f) }; // set it swinging
        }
    }
}
