using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>Which enemy this is: a slot into <see cref="Enemies.All"/>, where the definition lives.</summary>
    public struct Enemy : IComponent
    {
        public int Kind;
    }

    /// <summary>
    /// The leaves of the state tree. Unaware: <see cref="Idle"/> (dwell) → <see cref="Wander"/> (pick a point, go).
    /// Engaged: <see cref="Chase"/> (the last known position) / <see cref="Attack"/> (in sight and in range). Which
    /// half the enemy is in is decided every tick from whether its <see cref="Perception"/> holds a target — the parent
    /// branch gets the first word, which is what makes this a tree rather than a flat machine.
    /// </summary>
    public enum AiState : byte { Idle, Wander, Chase, Attack }

    /// <summary>
    /// The enemy's mind: one leaf, how long it has been there, who it is after and where it is going. Replaces the
    /// behaviour tree (a cloned node graph + blackboard per enemy) with a value the system steps.
    /// </summary>
    [NotInPreview] public struct AiBrain : IComponent
    {
        public AiState State;
        public float StateTime;
        public Vector3 MoveTo;
        /// <summary>This brain's own dice (xorshift state; 0 reseeds itself): wander picks do not touch the shared
        /// <c>Random</c>, so a scripted run plays out the same way every time.</summary>
        public uint Rng;
        /// <summary>
        /// The placement seed as it was handed out, never rolled: a stable identity to order this brain by. Path
        /// requests are served in this order and not in entity order, which a buffered spawn reshuffles (6.7b).
        /// </summary>
        public uint Order;
        /// <summary>Where this enemy was placed. Wander picks about <b>this</b>, not about wherever the enemy has got
        /// to, so a grunt with nothing to do roams a patch instead of random-walking off down the dock (11.11).</summary>
        public Vector3 Home;
    }

    /// <summary>
    /// What the brain wants this tick — rewritten whole every tick, so a quiet brain reads as all-false. The body
    /// body side acts on it — <see cref="SteeringSystem"/> walks and turns, <see cref="EnemyAttackSystem"/> shoots —
    /// and the brain never calls anyone.
    /// </summary>
    [NotInPreview] public struct AiIntent : IComponent
    {
        public bool Move, Attack;
        public Vector3 MoveTo;
        /// <summary>Where to aim (the attack's target point).</summary>
        public Vector3 FacePoint;
    }

    /// <summary>The state tree as pure functions over the brain: data in, the next state and intent out.</summary>
    public static class AiBrains
    {
        /// <summary>Seconds a wander may take before the point is given up (a step off the dock the ledge guard refused).</summary>
        public const float WanderTimeout = 3f;

        /// <summary>
        /// A generator seed from <b>where something was placed</b> (and a salt, e.g. its kind). Not from the entity
        /// index: that is decided by how many entities happen to exist before it, so every entity a later layer adds
        /// at load (a pickup, a volume) would reshuffle every enemy's dice — and a buffered spawn's index is not even
        /// final until playback. The same placement rolls the same dice, whatever else the level holds.
        /// </summary>
        public static uint Seed(Vector3 placed, int salt)
        {
            unchecked
            {
                uint h = 2166136261u; // FNV-1a over the centimetre-rounded position and the salt
                h = (h ^ (uint)Mathf.RoundToInt(placed.x * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(placed.y * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(placed.z * 100f)) * 16777619u;
                h = (h ^ (uint)salt) * 16777619u;
                return h == 0u ? 1u : h; // xorshift must not start at zero
            }
        }

        /// <summary>
        /// The salt a kind contributes to its brains' seeds: a hash of the <b>classname</b>, not the kind's index.
        /// Kinds load in file order, so an index changes the day a file is added (11.15 made every rifleman roll
        /// differently by adding the brawler); a name does not. Pinned by a check.
        /// </summary>
        public static int Salt(string classname)
        {
            if (string.IsNullOrEmpty(classname)) return 0;
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in classname) h = (h ^ c) * 16777619u;
                return (int)h;
            }
        }

        /// <summary>The next roll in [0, 1) from the brain's own generator (xorshift32).</summary>
        public static float Roll(ref uint state)
        {
            if (state == 0) state = 2463534242u;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state >> 8) * (1f / 16777216f);
        }

        /// <summary>Within the definition's arrive distance, on the ground plane.</summary>
        public static bool Arrived(Vector3 self, Vector3 point, EnemyData def)
        {
            Vector3 d = point - self; d.y = 0f;
            return d.sqrMagnitude <= def.arriveDistance * def.arriveDistance;
        }

        /// <summary>
        /// One tick. Interrupts first, outermost branch down: no target drops the enemy back to
        /// <see cref="AiState.Idle"/>; one in sight and in range puts it in <see cref="AiState.Attack"/>, any other in
        /// <see cref="AiState.Chase"/> towards where it was last seen or heard; then the current leaf runs.
        /// </summary>
        public static void Step(ref AiBrain b, ref AiIntent intent, EnemyData def, Vector3 self, in Perception p, float dt)
        {
            intent = default;
            bool entered = b.StateTime == 0f;
            b.StateTime += dt;

            // the branch conditions (the tree's selectors), parent before child
            if (p.Target.IsNull)
            {
                if (b.State == AiState.Chase || b.State == AiState.Attack) Enter(ref b, AiState.Idle);
            }
            else
            {
                b.MoveTo = p.LastKnown;
                var want = p.Seen && Vector3.Distance(self, p.LastKnown) <= def.attackRange ? AiState.Attack : AiState.Chase;
                if (b.State != want) Enter(ref b, want);
            }

            // the leaf
            switch (b.State)
            {
                case AiState.Idle:
                    if (b.StateTime > def.wanderWait) Enter(ref b, AiState.Wander);
                    break;
                case AiState.Wander:
                    if (entered)
                    {
                        // a point too close is rejected and the pick retried next tick, as the random-location node did
                        if (!PickWander(ref b, def, self, out var point)) { b.StateTime = 0f; break; }
                        b.MoveTo = point;
                    }
                    if (Arrived(self, b.MoveTo, def) || b.StateTime > WanderTimeout) { Enter(ref b, AiState.Idle); break; }
                    intent.Move = true; intent.MoveTo = b.MoveTo;
                    break;
                case AiState.Chase:
                    if (Arrived(self, b.MoveTo, def)) break; // close enough to where they were: stand and look
                    intent.Move = true; intent.MoveTo = b.MoveTo;
                    break;
                case AiState.Attack:
                    intent.Attack = true; intent.FacePoint = b.MoveTo;
                    break;
            }
        }

        /// <summary>Rolls a wander pick may make before giving the tick up.</summary>
        public const int WanderRolls = 3;

        /// <summary>
        /// A wander point: a roll in the disc about <see cref="AiBrain.Home"/>, kept only if it lands on an open span
        /// (the grid says the body can stand there and nothing is parked on it) and is not too close to where the
        /// enemy already is. Three rolls, then this tick is given up. Without a grid the old pick stands: a point in
        /// the disc, unchecked. Same dice either way — <c>b.Rng</c> — so a scripted run plays the same.
        /// </summary>
        public static bool PickWander(ref AiBrain b, EnemyData def, Vector3 self, out Vector3 point)
        {
            var nav = RipNRage.Engine.Ecs.Nav.Navigation.Current;
            bool onGrid = nav != null && nav.TotalSpans > 0;
            Vector3 home = b.Home == default ? self : b.Home;
            for (int roll = 0; roll < WanderRolls; roll++)
            {
                float angle = Roll(ref b.Rng) * 2f * Mathf.PI, dist = Roll(ref b.Rng) * def.wanderRadius;
                point = home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                Vector3 away = point - self; away.y = 0f;
                if (away.sqrMagnitude <= def.wanderMinDistance * def.wanderMinDistance) continue;
                if (!onGrid) return true;
                if (!nav.TryFindSpan(point, out int span, nav.Settings.MaxDrop)) continue;
                if (!nav.Locate(span, out var tile, out int local) || !tile.Spans[local].Open) continue;
                point = nav.PositionOf(tile, local);
                return true;
            }
            point = default;
            return false;
        }

        private static void Enter(ref AiBrain b, AiState state)
        {
            b.State = state;
            b.StateTime = 0f;
        }
    }

    /// <summary>
    /// Steps every live enemy's brain, as a chunk job on the world's workers: a brain reads its own row and the
    /// shared definitions and writes its own row, so the rows split any way at all and the result is the same.
    /// A dead enemy's brain stops and its intent reads quiet.
    /// </summary>
    [Reads(typeof(Enemy), typeof(Perception), typeof(LocalToWorld), typeof(Dead)), Writes(typeof(AiBrain), typeof(AiIntent))]
    public sealed class AiBrainSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<AiBrain>().With<AiIntent>().WithReadOnly<Enemy>().WithReadOnly<Perception>().WithReadOnly<LocalToWorld>().Build();

        private struct BrainJob : IChunkJob
        {
            public World World; public float Dt;
            public void Execute(in Chunk chunk, int worker)
            {
                var brains = chunk.Get<AiBrain>(); var intents = chunk.Get<AiIntent>(); var ps = chunk.Get<Perception>();
                var kinds = chunk.Get<Enemy>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    if (World.Has<Dead>(ents[i])) { intents[i] = default; continue; }
                    AiBrains.Step(ref brains[i], ref intents[i], Enemies.All[kinds[i].Kind], ltw[i].Position, in ps[i], Dt);
                }
            }
        }

        public void Tick(SystemContext ctx)
        {
            var job = new BrainJob { World = World, Dt = ctx.DeltaTime };
            _q.ScheduleParallel(ref job, 64);
        }
    }
}
