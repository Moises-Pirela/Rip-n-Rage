using System;

namespace RipNRage.Engine.Ecs.Transforms
{
    /// <summary>
    /// Computes <see cref="LocalToWorld"/> from <see cref="LocalTransform"/> and the <see cref="Parent"/> chain.
    /// Roots (no parent) are independent and run as a parallel chunk job; children are resolved after, parents
    /// first, each at most once per pass (a frame stamp memoises the walk up the chain).
    /// Runs in LateTick, after gameplay has moved things and before the frame is rendered.
    /// <para>Also guarantees every entity with a LocalTransform has a LocalToWorld (added on creation, seeded from
    /// the local pose — exact for a root, and a child is resolved properly in the same frame's pass — so a system
    /// that reads a fresh entity's world pose during Tick never sees it standing at the origin).</para>
    /// </summary>
    [Reads(typeof(LocalTransform), typeof(Parent)), Writes(typeof(LocalToWorld))]
    public sealed class TransformSystem : SystemBase, ILateTick, IOnEntityCreated
    {
        private Query _roots, _children, _missing;
        private long _pass;
        public const int MaxDepth = 64;

        public override void OnCreate()
        {
            _roots = World.Query().WithReadOnly<LocalTransform>().With<LocalToWorld>().Without<Parent>().Build();
            _children = World.Query().WithReadOnly<LocalTransform>().WithReadOnly<Parent>().With<LocalToWorld>().Build();
            _missing = World.Query().WithReadOnly<LocalTransform>().Without<LocalToWorld>().Build();
        }

        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            if (ctx.World.Has<LocalTransform>(e) && !ctx.World.Has<LocalToWorld>(e))
                ctx.Commands.Add(e, Seed(in ctx.World.Get<LocalTransform>(e)));
        }

        private static LocalToWorld Seed(in LocalTransform l) =>
            new LocalToWorld { Value = l.Matrix, Position = l.Position, Rotation = l.Rotation, Scale = l.Scale };

        private struct RootJob : IChunkJob
        {
            public long Pass;
            public void Execute(in Chunk chunk, int worker)
            {
                var local = chunk.Get<LocalTransform>(); var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    ref readonly var l = ref local[i];
                    ltw[i] = new LocalToWorld { Value = l.Matrix, Position = l.Position, Rotation = l.Rotation, Scale = l.Scale, Frame = Pass };
                }
            }
        }

        public void LateTick(SystemContext ctx) => Update(ctx.World, ctx);

        /// <summary>Runs the propagation now (also usable outside the frame, e.g. right after spawning a scene).</summary>
        public void Update(World world, SystemContext ctx = null)
        {
            _pass++;
            // entities created with a transform but no world pose yet (outside the creation event path)
            if (ctx != null)
                foreach (var c in _missing)
                {
                    var local = c.Get<LocalTransform>(); var ents = c.Entities;
                    for (int i = 0; i < c.Length; i++) ctx.Commands.Add(ents[i], Seed(in local[i]));
                }

            var job = new RootJob { Pass = _pass };
            _roots.ScheduleParallel(ref job, 1024);

            foreach (var c in _children)
            {
                var ents = c.Entities;
                for (int i = 0; i < c.Length; i++) Resolve(world, ents[i], 0);
            }
        }

        /// <summary>World pose of a child: resolves its parent chain first (memoised per pass).</summary>
        private void Resolve(World world, Entity e, int depth)
        {
            ref var ltw = ref world.Get<LocalToWorld>(e);
            if (ltw.Frame == _pass) return;
            ref readonly var local = ref world.Get<LocalTransform>(e);
            var parent = world.Get<Parent>(e).Value;
            if (depth > MaxDepth || !world.IsAlive(parent) || !world.Has<LocalToWorld>(parent))
            {
                // broken / cyclic / dangling chain: treat as a root rather than propagate garbage
                ltw = new LocalToWorld { Value = local.Matrix, Position = local.Position, Rotation = local.Rotation, Scale = local.Scale, Frame = _pass };
                return;
            }
            if (world.Has<Parent>(parent)) Resolve(world, parent, depth + 1);
            ref readonly var p = ref world.Get<LocalToWorld>(parent);
            var m = p.Value * local.Matrix;
            ltw = new LocalToWorld
            {
                Value = m,
                Position = p.Value.MultiplyPoint3x4(local.Position),
                Rotation = p.Rotation * local.Rotation,
                Scale = Vector3.Scale(p.Scale, local.Scale),
                Frame = _pass,
            };
        }
    }

    /// <summary>Destroying an entity destroys everything parented to it (recursively, through later sync rounds).</summary>
    [Reads(typeof(Parent))]
    public sealed class HierarchySystem : SystemBase, IOnEntityDestroyed
    {
        private Query _parented;
        public override void OnCreate() => _parented = World.Query().WithReadOnly<Parent>().Build();

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            // scans only entities that have a Parent (typically a small set)
            foreach (var c in _parented)
            {
                var parents = c.Get<Parent>(); var ents = c.Entities;
                for (int i = 0; i < c.Length; i++) if (parents[i].Value == e) ctx.Commands.DestroyEntity(ents[i]);
            }
        }
    }
}
