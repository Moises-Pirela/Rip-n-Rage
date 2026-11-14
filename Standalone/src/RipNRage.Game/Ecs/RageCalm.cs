using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The Calm meter's sources: evasion <b>under threat</b>. Being passive is not standing still and not shooting —
    /// it is moving while something is trying to kill you, so every gain here is gated on <see cref="Rage.Threat"/>:
    /// at least one living enemy has the player as its target and can see them, or an attack aimed at the player is
    /// in progress. Running laps around an empty level pays nothing.
    ///
    /// <para>Gains are posted as <see cref="RageGainEvent"/>s rather than written to the component, so they pass
    /// through <see cref="Rages.Step"/> like every other gain and are dropped while a state runs. The numbers are in
    /// <see cref="Content.RageDef"/> (tunables.cfg <c>[rage]</c>).</para>
    /// </summary>
    public static class Calms
    {
        /// <summary>
        /// Where a bullet is measured against: the middle of the player's capsule, which is about where the enemies
        /// aim (their <c>Aim</c> is the last known body position). Measuring at head height would make a well-aimed
        /// shot look like a near miss.
        /// </summary>
        public static readonly Vector3 PlayerCentre = new Vector3(0f, PlayerLink.CentreY, 0f);

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
    /// dashing / airborne, dodging a live attack, and shaking a pursuer off. The evasive moves are counted on the
    /// motor (<see cref="PlayerMotor.Evasions"/>) and the sight break is flagged by the AI
    /// (<see cref="Perception.LostSight"/>), so this system remembers nothing between ticks.
    /// </summary>
    [Reads(typeof(EnemyAttack), typeof(Parent), typeof(Perception))] // the attacks aimed at the player, and who has them
    public sealed class RageCalmSystem : SystemBase, ITick
    {
        private Query _players, _enemies, _attacks;

        public override void OnCreate()
        {
            _players = World.Query().With<Rage>().WithReadOnly<PlayerTag>().WithReadOnly<PlayerMotor>()
                .WithReadOnly<CharacterMotor>().Build();
            _enemies = World.Query().WithReadOnly<Enemy>().WithReadOnly<Perception>().Without<Dead>().Build();
            _attacks = World.Query().WithReadOnly<EnemyAttack>().WithReadOnly<Parent>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            var w = ctx.World;
            var def = Rages.Def;

            foreach (var chunk in _players)
            {
                var rages = chunk.Get<Rage>(); var motors = chunk.Get<PlayerMotor>();
                var characters = chunk.Get<CharacterMotor>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var rage = ref rages[i];
                    var motor = motors[i];

                    // one pass over the enemies answers both questions: who has the player, and who just lost them
                    Hunters(ents[i], motor.CurrentSpeed > def.movingSpeed, out bool hunted, out int lost);
                    bool attacked = AttackAimedAtPlayer(w, ents[i]);
                    rage.Threat = hunted || attacked;

                    float calm = lost * def.breakLineOfSight;
                    if (rage.Threat)
                    {
                        if (motor.IsWallRunning) calm += def.wallRunPerSecond * dt;
                        else if (motor.IsSliding || motor.IsDashing || !characters[i].IsGrounded) calm += def.evadePerSecond * dt;
                    }

                    // the dodge: the evasive moves themselves, while something is actually shooting at the player
                    int dodges = motor.Evasions - rage.EvasionsSeen;
                    rage.EvasionsSeen = motor.Evasions;
                    if (attacked && dodges > 0) calm += dodges * def.dodge;

                    if (calm > 0f) ctx.Post(new RageGainEvent { Calm = calm });
                }
            }
        }

        /// <summary>
        /// One pass over the living enemies: whether any has the player in sight, and how many lost sight of them
        /// this tick while the player was moving (shaken off, which is the player's doing — a dead pursuer has left
        /// this query entirely, so it can never be counted).
        /// </summary>
        private void Hunters(Entity player, bool moving, out bool hunted, out int lost)
        {
            hunted = false; lost = 0;
            foreach (var chunk in _enemies)
            {
                var senses = chunk.Get<Perception>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || senses[i].Target != player) continue;
                    if (senses[i].Seen) hunted = true;
                    else if (senses[i].LostSight && moving) lost++;
                }
            }
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
            var def = Rages.Def;

            foreach (var chunk in _bullets)
            {
                var bullets = chunk.Get<Projectile>(); var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var b = ref bullets[i];
                    Vector3 from = b.LastPosition, now = ltw[i].Position;
                    b.LastPosition = now; // Launch seeds it with the muzzle, so the first segment is a real one

                    if (b.NearMissPaid) continue;
                    if (!w.IsAlive(b.Instigator) || !w.Has<Enemy>(b.Instigator)) continue; // the player's own shots are not evasion

                    float d = Calms.DistanceToSegment(player, from, now);
                    if (d < def.nearMissMin || d > def.nearMissMax) continue;
                    b.NearMissPaid = true;
                    ctx.Post(new RageGainEvent { Calm = def.nearMiss });
                }
            }
        }

        private bool TryPlayer(out Vector3 position)
        {
            foreach (var chunk in _players)
            {
                var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) { position = ltw[i].Position + Calms.PlayerCentre; return true; }
            }
            position = default;
            return false;
        }
    }
}
