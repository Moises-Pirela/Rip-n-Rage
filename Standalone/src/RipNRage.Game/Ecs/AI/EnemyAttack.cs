using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Audio;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Vfx;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// The enemy's gun between attacks: when it may fire again, the flinch a hit buys, the one-per-life alert beat,
    /// and the attack it currently has in progress.
    /// </summary>
    public struct EnemyWeapon : IComponent
    {
        /// <summary>Until the next attack may start (the definition's fireRate after a burst).</summary>
        public float Refire;
        /// <summary>A hit holds the enemy: it stops walking and the shot waits (the old stagger).</summary>
        public float StaggerLeft;
        /// <summary>The readable beat between noticing someone and engaging them.</summary>
        public float AlertLeft;
        /// <summary>The alert reaction has played (once per life).</summary>
        public bool Alerted;
        /// <summary>The attack entity in progress (<see cref="Entity.Null"/> = none).</summary>
        public Entity Attack;
    }

    /// <summary>
    /// An attack in progress, as an entity parented to the enemy — their move, and the hierarchy tears it down with
    /// them (the finisher pattern of 5.5). The telegraph runs it down, then one shot leaves per burst interval.
    /// </summary>
    public struct EnemyAttack : IComponent
    {
        public float WindupLeft;
        public int ShotsLeft;
        public float BurstTimer;
        /// <summary>Where the shots go, re-aimed from the intent every tick (the legacy kept aiming mid-attack).</summary>
        public Vector3 Aim;
    }

    /// <summary>
    /// Engagement: the alert reaction on the acquisition edge, an <see cref="EnemyAttack"/> entity spawned when the
    /// brain asks and the gun is ready, and that entity's telegraph → burst → recover. A hit staggers (which the
    /// steering reads as a brake), death cancels. Runs alone (launches projectiles, plays sound and effects).
    /// </summary>
    [UpdateAfter(typeof(SteeringSystem))]
    public sealed class EnemyAttackSystem : SystemBase, ITick, IConsumes<HitEvent>
    {
        private static readonly Color AlertColor = new Color(1f, 0.95f, 0.3f, 1f), AimColor = new Color(1f, 0.55f, 0.15f, 1f);
        private const float AlertTelegraph = 0.3f;
        private Query _enemies, _attacks;

        public override void OnCreate()
        {
            _enemies = World.Query().With<EnemyWeapon>().WithReadOnly<Enemy>().WithReadOnly<AiIntent>()
                .WithReadOnly<Perception>().WithReadOnly<LocalToWorld>().Build();
            _attacks = World.Query().With<EnemyAttack>().WithReadOnly<Parent>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            Engage(ctx, dt);
            Fire(ctx, dt);
        }

        /// <summary>The gun's own clocks, the alert, and the decision to start an attack.</summary>
        private void Engage(SystemContext ctx, float dt)
        {
            foreach (var chunk in _enemies)
            {
                var guns = chunk.Get<EnemyWeapon>(); var kinds = chunk.Get<Enemy>(); var intents = chunk.Get<AiIntent>();
                var senses = chunk.Get<Perception>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var gun = ref guns[i];
                    var self = ents[i];
                    if (World.Has<Dead>(self))
                    {
                        if (World.IsAlive(gun.Attack)) { ctx.Commands.DestroyEntity(gun.Attack); gun.Attack = Entity.Null; }
                        continue;
                    }
                    var def = Enemies.All[kinds[i].Kind];
                    gun.Refire = Mathf.Max(0f, gun.Refire - dt);
                    gun.StaggerLeft = Mathf.Max(0f, gun.StaggerLeft - dt);
                    gun.AlertLeft = Mathf.Max(0f, gun.AlertLeft - dt);

                    // noticed someone: a yellow pulse and a sting, then a beat before engaging
                    if (!gun.Alerted && !senses[i].Target.IsNull)
                    {
                        gun.Alerted = true;
                        gun.AlertLeft = def.alertDelay;
                        Enemies.Telegraph(World, self, AlertTelegraph, AlertColor);
                        Sfx.Play(ProceduralSounds.EnemyAlert, 0.8f);
                        GameVfx.Spawn("enemy_alert", ltw[i].Position + Vector3.up * 1.6f, Vector3.up);
                    }

                    if (World.IsAlive(gun.Attack))
                    {
                        World.Get<EnemyAttack>(gun.Attack).Aim = intents[i].FacePoint; // keep aiming where they are now
                        continue;
                    }
                    gun.Attack = Entity.Null;
                    if (!intents[i].Attack || gun.Refire > 0f || gun.AlertLeft > 0f) continue;

                    // the telegraph: orange pulse, a glow at the muzzle, the charge sound
                    var e = ctx.Commands.CreateEntity();
                    ctx.Commands.Add(e, new Parent { Value = self });
                    ctx.Commands.Add(e, new EnemyAttack
                    {
                        WindupLeft = def.attackWindup, ShotsLeft = Mathf.Max(1, def.burstCount), Aim = intents[i].FacePoint,
                    });
                    gun.Attack = e;
                    Enemies.SetAnim(World, self, AnimationState.ATTACKING);
                    Enemies.Telegraph(World, self, def.attackWindup, AimColor);
                    Sfx.Play(ProceduralSounds.EnemyAim, 0.7f);
                    var muzzle = Muzzle(World, self, ltw[i].Position);
                    GameVfx.Spawn("enemy_aim", muzzle, (intents[i].FacePoint - muzzle).normalized);
                }
            }
        }

        /// <summary>Each attack entity: the wind-up, then a shot per burst interval, then the recovery.</summary>
        private void Fire(SystemContext ctx, float dt)
        {
            foreach (var chunk in _attacks)
            {
                var attacks = chunk.Get<EnemyAttack>(); var parents = chunk.Get<Parent>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    var enemy = parents[i].Value;
                    if (!World.IsAlive(enemy) || World.Has<Dead>(enemy) || !World.Has<EnemyWeapon>(enemy))
                    {
                        ctx.Commands.DestroyEntity(ents[i]);
                        continue;
                    }
                    ref var gun = ref World.Get<EnemyWeapon>(enemy);
                    if (gun.StaggerLeft > 0f) continue; // a hit during the wind-up delays the shot, as it did

                    ref var a = ref attacks[i];
                    if (a.WindupLeft > 0f) { a.WindupLeft -= dt; continue; }

                    a.BurstTimer -= dt;
                    if (a.BurstTimer > 0f) continue;

                    var def = Enemies.All[World.Get<Enemy>(enemy).Kind];
                    Shoot(World, enemy, in a, def);
                    a.BurstTimer = def.burstInterval;
                    if (--a.ShotsLeft > 0) continue;

                    gun.Refire = def.fireRate;          // the recovery before the next burst
                    gun.Attack = Entity.Null;
                    Enemies.SetAnim(World, enemy, AnimationState.IDLE); // the sheet goes back to the steering
                    ctx.Commands.DestroyEntity(ents[i]);
                }
            }
        }

        private static void Shoot(World w, Entity enemy, in EnemyAttack a, EnemyData def)
        {
            Vector3 muzzle = Muzzle(w, enemy, w.Get<LocalToWorld>(enemy).Position);
            Vector3 direction = a.Aim - muzzle;
            Projectiles.Launch(w, Projectiles.EnemyBullet, muzzle, direction, enemy);
            Noises.Emit(w.Commands, muzzle, enemy, def.noise); // the others hear it
            Sfx.Play(ProceduralSounds.EnemyShot, 0.9f);
            GameVfx.Spawn("enemy_muzzle", muzzle, direction.normalized);
        }

        /// <summary>Where the barrel is: the definition's offset, turned by the enemy's heading.</summary>
        public static Vector3 Muzzle(World w, Entity enemy, Vector3 position)
        {
            var def = Enemies.All[w.Get<Enemy>(enemy).Kind];
            float yaw = w.Has<Heading>(enemy) ? w.Get<Heading>(enemy).Yaw : 0f;
            return position + Quaternion.Euler(0f, yaw, 0f) * def.muzzleOffset;
        }

        /// <summary>Being hit stops the enemy and delays its shot.</summary>
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            foreach (ref readonly var e in events)
            {
                if (!World.IsAlive(e.Target) || World.Has<Dead>(e.Target)) continue;
                if (!World.Has<EnemyWeapon>(e.Target) || !World.Has<Enemy>(e.Target)) continue;
                World.Get<EnemyWeapon>(e.Target).StaggerLeft = Enemies.All[World.Get<Enemy>(e.Target).Kind].staggerTime;
            }
        }
    }
}
