using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Effects;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Game.Content.Vfx;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>A projectile in flight: a slot into <see cref="Projectiles.All"/> and who fired it.</summary>
    public struct Projectile : IComponent
    {
        public int Kind;
        public Entity Instigator;
    }

    /// <summary>One projectile definition — shared immutable data, like the weapons.</summary>
    public sealed class ProjectileDef
    {
        public float Speed, Damage, Range;
        /// <summary>Who it can run into. The old per-class ShouldCollide, as data: enemy bullets exclude the
        /// Enemies layer (they pass through other enemies); the rocket excludes only its shooter.</summary>
        public int CollisionMask;
        public string BulletKey = "enemy";
        /// <summary>vfx bindings "trails" key for a following effect; null = none.</summary>
        public string Trail;
        /// <summary>vfx bindings "explosions" key: set = this detonates (blast damage + impulses) instead of hitting one thing.</summary>
        public string Explosion;
        public float ExplosionImpulse, ExplosionRadius;
        // the glowing slug: a billboard sprite and its light, on the projectile entity itself
        public Sprite Sprite;
        public Color Color;
        public float Scale;
        public bool FullBright;
        public Color LightColor;
        public float LightRange, LightIntensity;
    }

    /// <summary>The projectile definitions and the launch function. State lives in the components.</summary>
    public static class Projectiles
    {
        public const int Rocket = 0, EnemyBullet = 1;

        /// <summary>By kind (set once by GameAssets, next to the weapon data it belongs to).</summary>
        public static ProjectileDef[] All = System.Array.Empty<ProjectileDef>();

        /// <summary>What projectiles fly against (the legacy Projectile.HitMask, triggers excluded: water detonation
        /// returns with the water rework in layer 7).</summary>
        public const int HitMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision) |
                                   (1 << Layers.Player) | (1 << Layers.Enemies) | (1 << Layers.Water);

        /// <summary>
        /// Fires one projectile: a swept Stop-mode mover that cannot tunnel at any speed, with its sprite, light and
        /// trail on the same entity, expiring after range / speed. Fire-and-forget, so it always goes through the
        /// command buffer — the entity materialises whole at the next sync point, and nothing reads it before then.
        /// </summary>
        public static void Launch(World w, int kind, Vector3 origin, Vector3 direction, Entity instigator)
        {
            var def = All[kind];
            direction = direction.normalized;
            var cmd = w.Commands;

            var e = cmd.CreateEntity();
            cmd.Add(e, new LocalTransform { Position = origin, Rotation = Quaternion.LookRotation(direction), Scale = Vector3.one * def.Scale });
            cmd.Add(e, new Projectile { Kind = kind, Instigator = instigator });
            cmd.Add(e, new Lifetime { Left = def.Range / def.Speed });
            cmd.Add(e, new ColliderShape { Kind = ShapeKind.Sphere, Radius = 0.08f });
            cmd.Add(e, new CollisionFilter { Layer = Layers.Projectile, Mask = def.CollisionMask, UseLayerMatrix = false });
            cmd.Add(e, new KinematicMover
            {
                Velocity = direction * def.Speed,
                Mode = MotionMode.Stop, UseGravity = false, TerminalSpeed = def.Speed,
                CollisionMask = def.CollisionMask, UseLayerMatrix = false, Ignore = instigator,
                PushMass = 10f, MaxSlides = 1, GroundNormal = Vector3.up,
            });
            cmd.Add(e, new ReportCollisions());
            cmd.Add(e, new SpriteRender
            {
                Sprite = RenderAssets.Sprites.Intern(def.Sprite), Color = def.Color,
                Billboard = true, FullBright = def.FullBright, Layer = RenderLayer.World, CastsShadow = false,
            });
            cmd.Add(e, new LightSource { Type = LightType.Point, Color = def.LightColor, Range = def.LightRange, Intensity = def.LightIntensity });
            if (def.Trail != null && GameVfx.Bindings.trails.TryGetValue(def.Trail, out var fx) && !string.IsNullOrEmpty(fx))
                cmd.Add(e, new EffectEmitter { Effect = fx, Backwards = true });
        }
    }

    /// <summary>
    /// Resolves projectile contacts (the mover's first <see cref="CollisionEvent"/>, Stop mode reports exactly one):
    /// bullets put a <see cref="DamageEvent"/> on whatever they ran into; a detonating kind blasts instead — damage
    /// and rage over one cached overlap query (the legacy path scanned every GameObject in the scene per rocket),
    /// impulses on rigid bodies, trauma on the player's camera, and the rocket-jump as motor intent. Replaces the
    /// Projectile / RocketProjectile behaviours. Runs alone (physics queries).
    /// </summary>
    public sealed class ProjectileSystem : SystemBase, IConsumes<CollisionEvent>
    {
        private readonly List<Entity> _blast = new List<Entity>(64);

        public void Consume(SystemContext ctx, System.ReadOnlySpan<CollisionEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Self) || !w.Has<Projectile>(e.Self)) continue;
                var p = w.Get<Projectile>(e.Self);
                var def = Projectiles.All[p.Kind];

                if (def.Explosion != null) Detonate(ctx, def, in e, p.Instigator);
                else HitOne(ctx, def, in e, p.Instigator);

                ctx.Commands.DestroyEntity(e.Self);
            }
        }

        private static void HitOne(SystemContext ctx, ProjectileDef def, in CollisionEvent e, Entity instigator)
        {
            var w = ctx.World;
            bool flesh = w.IsAlive(e.Other) && w.Has<Health>(e.Other) && !w.Has<Dead>(e.Other);
            if (flesh && e.Other != instigator)
            {
                Vector3 dir = w.Has<LocalTransform>(e.Self) ? w.Get<LocalTransform>(e.Self).Forward : -e.Normal;
                ctx.Post(new DamageEvent { Target = e.Other, Amount = def.Damage, Point = e.Point, Direction = dir, Impulse = 5f });
            }
            GameVfx.ImpactOn(flesh ? Surfaces.Flesh : Surfaces.Default, e.Point, e.Normal, def.BulletKey);
        }

        private void Detonate(SystemContext ctx, ProjectileDef def, in CollisionEvent e, Entity instigator)
        {
            var w = ctx.World;
            Vector3 point = e.Point;
            GameVfx.Explosion(def.Explosion, point, e.Normal);

            // the near miss everyone feels: camera trauma with distance falloff, and bodies thrown by the blast
            if (PlayerCamera.Has) PlayerCamera.Shake.ShakeAt(PlayerCamera.Position, point, 22f, 1f);
            if (def.ExplosionImpulse > 0f && def.ExplosionRadius > 0f)
                PhysicsApi.AddExplosionForce(def.ExplosionImpulse, point, def.ExplosionRadius, 0.6f, ~(1 << Layers.Player));

            // the rocket jump: the player near the blast gets the tuned motor impulse, not the generic body shove
            var player = PlayerLink.PlayerEntity;
            if (w.IsAlive(player) && !w.Has<Dead>(player) && w.Has<CharacterMotor>(player) && w.Has<LocalTransform>(player))
            {
                Vector3 away = w.Get<LocalTransform>(player).Position - point;
                float dist = away.magnitude;
                const float blastRadius = 11f, maxImpulse = 40f; // cap: a mistuned blast must not fling the player into orbit
                if (dist < blastRadius)
                {
                    Vector3 impulse = (away.normalized * 13f + Vector3.up * 10f) * (1f - dist / blastRadius);
                    if (impulse.sqrMagnitude > maxImpulse * maxImpulse) impulse = impulse.normalized * maxImpulse;
                    ref var motor = ref w.Get<CharacterMotor>(player);
                    if (motor.IsDashing) motor.PendingStopDash = true;
                    motor.PendingImpulse += impulse;
                }
            }

            // blast damage by distance over one overlap query (10 m, as the legacy loop measured it)
            _blast.Clear();
            PhysicsApi.World.Overlap(CollisionShape.Sphere(point, 10f),
                new QueryFilter { layerMask = (1 << Layers.Player) | (1 << Layers.Enemies) }, _blast);
            foreach (var target in _blast)
            {
                if (!w.IsAlive(target) || !w.Has<Health>(target) || w.Has<Dead>(target)) continue;
                if (w.Has<Hitbox>(target)) PlayerState.Current?.AddToRage(0.05f); // an enemy caught in the blast feeds the rage
                // the shooter's own blast barely hurts them: rocket jumping is a movement tool, not suicide
                float dmg = target == instigator ? def.Damage * 0.12f : def.Damage;
                Vector3 blastDir = (w.Has<LocalTransform>(target) ? w.Get<LocalTransform>(target).Position : point) - point;
                blastDir.y += 0.5f;
                ctx.Post(new DamageEvent
                {
                    Target = target, Amount = dmg, Point = point, Direction = blastDir.normalized,
                    Impulse = Mathf.Max(def.ExplosionImpulse, 60f), Explosive = true,
                    ExplosionRadius = Mathf.Max(def.ExplosionRadius, 6f),
                });
            }
        }
    }
}
