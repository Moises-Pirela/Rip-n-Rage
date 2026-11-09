using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;

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
            Debug.LogWarning($"[ecs-demo] {world.EntityCount} entities in {world.Archetypes.Count} archetypes from {prefabDir}");
        }
    }
}
