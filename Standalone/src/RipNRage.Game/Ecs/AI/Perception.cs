using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// What the enemy knows about its target: who, where it was last seen or heard, whether it is in sight right now,
    /// and for how long it has not been. Written by <see cref="SightSystem"/> and <see cref="HearingSystem"/>, read by
    /// the brain. <see cref="Target"/> is <see cref="Entity.Null"/> while unaware.
    /// </summary>
    public struct Perception : IComponent
    {
        public Entity Target;
        public Vector3 LastKnown;
        public bool Seen;
        public float SinceSeen;
    }

    /// <summary>
    /// A sound travelling outward from where it was made. The front (<see cref="Radius"/>) advances at
    /// <see cref="Noises.Speed"/> every tick; a listener hears it the tick the front crosses it, unless a wall in the
    /// way muffles it below hearing. The entity goes when the front passes <see cref="Loudness"/>.
    /// </summary>
    public struct Noise : IComponent
    {
        public Vector3 Origin;
        /// <summary>Who made it: the thing a listener turns on, or — for an enemy's own shot — whose target they adopt.</summary>
        public Entity Source;
        /// <summary>Metres the sound carries in the open.</summary>
        public float Loudness;
        public float Radius;
    }

    public static class Noises
    {
        /// <summary>Metres per second the front advances: a shot 30 m away is heard a third of a second later.</summary>
        public const float Speed = 80f;
        /// <summary>A wall between the sound and the ear halves how far it carries.</summary>
        public const float WallMuffle = 0.5f;
        /// <summary>How far a footstep carries.</summary>
        public const float Footstep = 8f;
        /// <summary>What muffles a sound: the level, not people or pickups.</summary>
        public const int WallMask = (1 << Layers.Default) | (1 << Layers.Wall) | (1 << Layers.Walkable) | (1 << Layers.OnlyGroundCollision);

        /// <summary>Makes a sound. Through the command buffer: the entity is in the world at the next sync point.</summary>
        public static void Emit(CommandBuffer cmd, Vector3 origin, Entity source, float loudness)
        {
            if (loudness <= 0f) return;
            var e = cmd.CreateEntity();
            cmd.Add(e, new Noise { Origin = origin, Source = source, Loudness = loudness });
        }

        /// <summary>True when nothing in <see cref="WallMask"/> stands between the two points.</summary>
        public static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            return len < 1e-3f || !PhysicsApi.Raycast(from, d / len, len, WallMask);
        }
    }

    /// <summary>
    /// Sight: range, then the field of view about the body's facing, then one line-of-sight ray. Also the keeper of
    /// the target: a dead or gone one is dropped at once, an unseen one after <c>EnemyData.memory</c> seconds. Runs
    /// alone (physics queries).
    /// </summary>
    [UpdateBefore(typeof(AiBrainSystem))]
    public sealed class SightSystem : SystemBase, ITick
    {
        /// <summary>Eyes and chest heights above the body centre.</summary>
        public const float EyeHeight = 0.6f;
        private Query _eyes, _players;

        public override void OnCreate()
        {
            _eyes = World.Query().With<Perception>().WithReadOnly<Enemy>().WithReadOnly<Heading>().WithReadOnly<LocalToWorld>().Without<Dead>().Build();
            _players = World.Query().With<PlayerTag>().WithReadOnly<LocalToWorld>().Without<Dead>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            foreach (var chunk in _eyes)
            {
                var ps = chunk.Get<Perception>(); var kinds = chunk.Get<Enemy>(); var heads = chunk.Get<Heading>(); var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var p = ref ps[i];
                    var def = Enemies.All[kinds[i].Kind];

                    if (!p.Target.IsNull && (!World.IsAlive(p.Target) || World.Has<Dead>(p.Target))) p = default;

                    p.Seen = false;
                    foreach (var pc in _players)
                    {
                        var pl = pc.Get<LocalToWorld>(); var pe = pc.Entities;
                        for (int j = 0; j < pc.Length; j++)
                        {
                            if (!pc.Includes(j)) continue;
                            if (!CanSee(ltw[i].Position, heads[i].Forward, pl[j].Position, def)) continue;
                            p.Target = pe[j]; p.LastKnown = pl[j].Position; p.Seen = true; p.SinceSeen = 0f;
                        }
                    }

                    if (p.Seen || p.Target.IsNull) continue;
                    p.SinceSeen += dt;
                    if (p.SinceSeen > def.memory) p = default;
                }
            }
        }

        /// <summary>Range → cone about the facing (flattened) → nothing of the level between the eyes and the target.</summary>
        public static bool CanSee(Vector3 self, Vector3 forward, Vector3 target, EnemyData def)
        {
            Vector3 to = target - self;
            if (to.sqrMagnitude > def.sightRange * def.sightRange) return false;
            Vector3 flat = new Vector3(to.x, 0f, to.z); forward.y = 0f;
            if (flat.sqrMagnitude > 1e-6f && forward.sqrMagnitude > 1e-6f
                && Vector3.Angle(forward, flat) > def.sightFov * 0.5f) return false;
            return Noises.Clear(self + Vector3.up * EyeHeight, target);
        }
    }

    /// <summary>
    /// Hearing: advances every <see cref="Noise"/>'s front and tells the listeners it crosses. A sound made by the
    /// player turns the listener on the player; one made by another enemy passes on that enemy's target (they are
    /// shooting at someone). Runs alone (physics queries).
    /// </summary>
    [UpdateBefore(typeof(SightSystem))]
    public sealed class HearingSystem : SystemBase, ITick
    {
        private Query _noises, _ears;

        public override void OnCreate()
        {
            _noises = World.Query().With<Noise>().Build();
            _ears = World.Query().With<Perception>().WithReadOnly<LocalToWorld>().Without<Dead>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float step = Noises.Speed * ctx.DeltaTime;
            foreach (var chunk in _noises)
            {
                var noises = chunk.Get<Noise>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var n = ref noises[i];
                    float front = n.Radius;
                    n.Radius += step;
                    var target = TargetOf(in n);
                    if (!target.IsNull) Reach(in n, front, target);
                    if (n.Radius >= n.Loudness) ctx.Commands.DestroyEntity(ents[i]);
                }
            }
        }

        /// <summary>Whom a listener turns on: the source itself, or — if the source is an enemy — its target.</summary>
        private Entity TargetOf(in Noise n)
        {
            if (!World.IsAlive(n.Source) || World.Has<Dead>(n.Source)) return Entity.Null;
            if (!World.Has<Enemy>(n.Source)) return n.Source;
            if (!World.Has<Perception>(n.Source)) return Entity.Null;
            var t = World.Get<Perception>(n.Source).Target;
            return World.IsAlive(t) && !World.Has<Dead>(t) ? t : Entity.Null;
        }

        private void Reach(in Noise n, float front, Entity target)
        {
            foreach (var chunk in _ears)
            {
                var ps = chunk.Get<Perception>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || ents[i] == n.Source) continue;
                    float d = Vector3.Distance(n.Origin, ltw[i].Position);
                    if (d <= front || d > n.Radius || d > n.Loudness) continue; // the front crosses the ear this tick, or never
                    if (d > n.Loudness * Noises.WallMuffle && !Noises.Clear(n.Origin, ltw[i].Position + Vector3.up * SightSystem.EyeHeight)) continue;
                    ref var p = ref ps[i];
                    p.Target = target; p.LastKnown = n.Origin; p.SinceSeen = 0f;
                }
            }
        }
    }
}
