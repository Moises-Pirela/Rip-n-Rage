using System.Collections.Generic;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The Calm meter's sources: evasion <b>under threat</b>. Being passive is not standing still and not shooting —
    /// it is moving while something is trying to kill you, so every gain here is gated on
    /// <see cref="Rage.Threat"/>: at least one living enemy has the player as its target and can see them, or an
    /// attack aimed at the player is in progress. Running laps around an empty level pays nothing.
    ///
    /// <para>Gains are posted as <see cref="RageGainEvent"/>s rather than written to the component, so they pass
    /// through <see cref="Rages.Step"/> like every other gain and are dropped while a state runs.</para>
    /// </summary>
    public static class Calms
    {
        /// <summary>Per second, while under threat.</summary>
        public const float WallRunPerSecond = 0.06f;
        /// <summary>Per second, while under threat: sliding, dashing or off the ground.</summary>
        public const float EvadePerSecond = 0.03f;
        /// <summary>A dash, a slide or a wall jump while an attack aimed at the player is live.</summary>
        public const float Dodge = 0.12f;
        /// <summary>An enemy that had the player loses sight of them while the player is moving.</summary>
        public const float BreakLineOfSight = 0.05f;
        /// <summary>An enemy bullet that passed by without hitting.</summary>
        public const float NearMiss = 0.08f;
        /// <summary>Taking a blow is the opposite of evading (Fury is not penalised).</summary>
        public const float DamagePenalty = 0.10f;

        /// <summary>
        /// The near-miss band: closer than <see cref="NearMissMin"/> and the bullet would have hit the capsule
        /// (that blow pays <see cref="DamagePenalty"/> instead), further than <see cref="NearMissMax"/> and it was
        /// never a threat. Measured as the distance from the player to the segment the bullet flew this tick.
        /// </summary>
        public const float NearMissMin = 0.6f, NearMissMax = 1.5f;

        /// <summary>The player counts as moving (so a break of line of sight was earned) above this speed.</summary>
        public const float MovingSpeed = 1f;

        /// <summary>Distance from <paramref name="point"/> to the segment a → b.</summary>
        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 1E-06f) return (point - a).magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / len);
            return (point - (a + ab * t)).magnitude;
        }
    }

    /// <summary>
    /// Writes <see cref="Rage.Threat"/> and pays the Calm meter for evading under it: wall running, sliding /
    /// dashing / airborne, dodging a live attack, and shaking a pursuer off. The dodge and the wall jump are edges
    /// (a flag going up, the wall-jump count moving), the rest are per-second.
    /// </summary>
    [Reads(typeof(EnemyAttack), typeof(Parent), typeof(Perception), typeof(Dead))] // the attacks aimed at the player, and who can see them
    public sealed class RageCalmSystem : SystemBase, ITick
    {
        private Query _players, _enemies, _attacks;
        /// <summary>Last tick's edges, per player entity (one in practice; keyed so a respawn starts clean).</summary>
        private readonly Dictionary<Entity, Edges> _edges = new Dictionary<Entity, Edges>();
        /// <summary>Which enemies could see the player last tick (the line-of-sight break is an edge).</summary>
        private readonly HashSet<Entity> _sawPlayer = new HashSet<Entity>();
        private readonly HashSet<Entity> _seeingNow = new HashSet<Entity>();

        private struct Edges { public bool Dashing, Sliding; public int WallJumps; }

        public override void OnCreate()
        {
            _players = World.Query().With<Rage>().WithReadOnly<PlayerTag>().WithReadOnly<PlayerMotor>()
                .WithReadOnly<CharacterMotor>().WithReadOnly<LocalToWorld>().Build();
            _enemies = World.Query().WithReadOnly<Enemy>().WithReadOnly<Perception>().Build();
            _attacks = World.Query().WithReadOnly<EnemyAttack>().WithReadOnly<Parent>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            var w = ctx.World;

            foreach (var chunk in _players)
            {
                var rages = chunk.Get<Rage>(); var motors = chunk.Get<PlayerMotor>();
                var characters = chunk.Get<CharacterMotor>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    Entity player = ents[i];
                    ref var rage = ref rages[i];
                    var motor = motors[i];

                    bool attacked = AttackAimedAtPlayer(w, player);
                    rage.Threat = attacked || Hunted(w, player, out _);

                    float calm = LineOfSightBreaks(w, player, motor.CurrentSpeed);
                    if (rage.Threat)
                    {
                        if (motor.IsWallRunning) calm += Calms.WallRunPerSecond * dt;
                        else if (motor.IsSliding || motor.IsDashing || !characters[i].IsGrounded) calm += Calms.EvadePerSecond * dt;
                    }

                    // the dodge: the evasive move itself, while something is actually shooting at the player
                    _edges.TryGetValue(player, out var was);
                    bool dodged = (motor.IsDashing && !was.Dashing) || (motor.IsSliding && !was.Sliding) || motor.WallJumps > was.WallJumps;
                    if (dodged && attacked) calm += Calms.Dodge;
                    _edges[player] = new Edges { Dashing = motor.IsDashing, Sliding = motor.IsSliding, WallJumps = motor.WallJumps };

                    if (calm > 0f) ctx.Post(new RageGainEvent { Calm = calm });
                }
            }
        }

        /// <summary>Living enemies that have the player and can see them right now.</summary>
        private bool Hunted(World w, Entity player, out int count)
        {
            count = 0;
            foreach (var chunk in _enemies)
            {
                var senses = chunk.Get<Perception>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || w.Has<Dead>(ents[i])) continue;
                    if (senses[i].Target == player && senses[i].Seen) count++;
                }
            }
            return count > 0;
        }

        /// <summary>An <see cref="EnemyAttack"/> entity whose enemy is after the player, still winding up or firing.</summary>
        private bool AttackAimedAtPlayer(World w, Entity player)
        {
            foreach (var chunk in _attacks)
            {
                var attacks = chunk.Get<EnemyAttack>(); var parents = chunk.Get<Parent>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    if (attacks[i].WindupLeft <= 0f && attacks[i].ShotsLeft <= 0) continue;
                    Entity enemy = parents[i].Value;
                    if (w.IsAlive(enemy) && w.Has<Perception>(enemy) && w.Get<Perception>(enemy).Target == player) return true;
                }
            }
            return false;
        }

        /// <summary>Pursuers that could see the player last tick and cannot now, while the player is moving.</summary>
        private float LineOfSightBreaks(World w, Entity player, float speed)
        {
            _seeingNow.Clear();
            foreach (var chunk in _enemies)
            {
                var senses = chunk.Get<Perception>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || w.Has<Dead>(ents[i])) continue;
                    if (senses[i].Target == player && senses[i].Seen) _seeingNow.Add(ents[i]);
                }
            }

            float calm = 0f;
            bool moving = speed > Calms.MovingSpeed;
            foreach (var e in _sawPlayer)
                if (!_seeingNow.Contains(e) && moving && w.IsAlive(e) && !w.Has<Dead>(e))
                    calm += Calms.BreakLineOfSight; // shaken off, not killed: a dead pursuer is gone from the world

            _sawPlayer.Clear();
            foreach (var e in _seeingNow) _sawPlayer.Add(e);
            return calm;
        }

    }

    /// <summary>
    /// Near misses: an enemy bullet that passed the player close enough to flinch at and did not hit. Measured on
    /// the segment the bullet flew this tick, so a fast round cannot skip past between frames, and paid once.
    /// </summary>
    [Reads(typeof(Enemy))] // whose bullet it is
    public sealed class NearMissSystem : SystemBase, ITick
    {
        private Query _bullets, _players;

        public override void OnCreate()
        {
            _bullets = World.Query().With<Projectile>().WithReadOnly<LocalToWorld>().Build();
            _players = World.Query().WithReadOnly<PlayerTag>().WithReadOnly<LocalToWorld>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            if (!TryPlayer(out var player)) return;
            var w = ctx.World;

            foreach (var chunk in _bullets)
            {
                var bullets = chunk.Get<Projectile>(); var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var b = ref bullets[i];
                    Vector3 now = ltw[i].Position;
                    Vector3 from = b.LastPosition;
                    b.LastPosition = now;
                    if (from == Vector3.zero) continue; // its first tick: no segment yet

                    if (b.NearMissPaid) continue;
                    if (!w.IsAlive(b.Instigator) || !w.Has<Enemy>(b.Instigator)) continue; // the player's own shots are not evasion

                    float d = Calms.DistanceToSegment(player, from, now);
                    if (d < Calms.NearMissMin || d > Calms.NearMissMax) continue;
                    b.NearMissPaid = true;
                    ctx.Post(new RageGainEvent { Calm = Calms.NearMiss });
                }
            }
        }

        private bool TryPlayer(out Vector3 position)
        {
            foreach (var chunk in _players)
            {
                var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) { position = ltw[i].Position + new Vector3(0f, 0.9f, 0f); return true; } // mid-capsule
            }
            position = default;
            return false;
        }
    }
}
