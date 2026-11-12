using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs.Combat
{
    // ================================================================= components

    /// <summary>
    /// Hit points and armour of anything damageable — the player, the enemies, later the props. Written only by
    /// <see cref="HealthSystem"/> (damage) and the handful of direct givers (healing, armour pickups, save load);
    /// everything that *deals* damage posts a <see cref="DamageEvent"/> instead of touching this.
    /// </summary>
    public struct Health : IComponent
    {
        public float Current, Max;
        public float Armor, MaxArmor;
        /// <summary>Fraction of a hit the armour absorbs while it lasts (0.75 for enemies, 1 for the player).</summary>
        public float ArmorResistance;
    }

    public sealed class HealthConfig : ComponentConfig<Health>
    {
        public float max = 100f, armor, maxArmor = 100f, armorResistance = 0.75f;
        public override Health Bake() => new Health
        {
            Current = max, Max = max,
            Armor = armor, MaxArmor = maxArmor,
            ArmorResistance = armorResistance,
        };
    }

    /// <summary>
    /// Tag: this entity's health reached zero. Added by <see cref="HealthSystem"/> in the frame of the killing blow
    /// and never removed by it — death latches, unlike the legacy <c>EntityState</c>, which called <c>Die()</c> again
    /// for every further lethal hit and left the guard to its subclasses.
    /// </summary>
    [SparseComponent] public struct Dead : IComponent { }
    public sealed class DeadConfig : ComponentConfig<Dead> { public override Dead Bake() => default; }

    /// <summary>
    /// Where this entity can be hurt: weapons that resolve a hit on this entity judge headshots against the capsule's
    /// top part, exactly as the legacy <c>AIState.IsHeadshot</c> did against the 2.25 m sprite. Runtime-only (it names
    /// an entity), like <c>PhysicsHandle</c>: configs cannot reference runtime entities.
    /// </summary>
    public struct Hitbox : IComponent
    {
        /// <summary>The entity whose <see cref="Health"/> a hit on this collider damages (itself for a one-entity rig).</summary>
        public Entity Owner;
        /// <summary>Full height of the figure, centred on the entity (the rifleman sprite is 2.25 m).</summary>
        public float Height;
        /// <summary>Fraction of <see cref="Height"/> below the head line (hits above it count double).</summary>
        public float HeadFraction;

        /// <summary>Is a world-space hit at <paramref name="point"/> a headshot, given the entity's centre?</summary>
        public bool IsHeadshot(in LocalToWorld pose, Vector3 point)
        {
            float bottom = pose.Position.y - 0.5f * Height;
            return point.y > bottom + Height * HeadFraction;
        }
    }

    // ================================================================= events

    /// <summary>
    /// A blow about to land: posted by whatever deals damage, consumed by <see cref="HealthSystem"/> at the end of the
    /// frame. Carries the circumstances the death effects need (the ragdoll slicer throws the piece that was hit) —
    /// the payload the legacy static <c>DamageInfo</c> hand-off used to smuggle into <c>TakeDamage</c>.
    /// </summary>
    public struct DamageEvent : IPostProcessEvent
    {
        public Entity Target;
        /// <summary>Who dealt it (<see cref="Entity.Null"/> = the player / the world; AI instigators come with layer 6).</summary>
        public Entity Instigator;
        public float Amount;
        /// <summary>Overrides the target's <see cref="Health.ArmorResistance"/> when &gt; 0 (the legacy optional parameter).</summary>
        public float ArmorResistance;
        public Vector3 Point, Direction;
        /// <summary>Impulse the blow carries (N·s) — what the ragdoll piece is thrown with.</summary>
        public float Impulse;
        public bool Explosive;
        public float ExplosionRadius;
        public bool Headshot;
    }

    /// <summary>A blow landed (posted by <see cref="HealthSystem"/> in the same frame): feedback — crosshair, rage, style, hit flash.</summary>
    public struct HitEvent : IPostProcessEvent
    {
        public Entity Target, Instigator;
        /// <summary>Damage asked for (before armour), as the style / rage bookkeeping counts it.</summary>
        public float Amount;
        public Vector3 Point;
        public bool Headshot;
        /// <summary>This was the blow that emptied the health.</summary>
        public bool Killed;
    }

    /// <summary>An entity's health reached zero (posted once, by the killing blow, with its circumstances).</summary>
    public struct DeathEvent : IPostProcessEvent
    {
        public Entity Entity, Instigator;
        public Vector3 Point, Direction;
        public float Impulse;
        public bool Explosive;
        public float ExplosionRadius;
        public bool Headshot;
    }

    // ================================================================= the one place damage is applied

    /// <summary>
    /// Consumes every <see cref="DamageEvent"/> of the frame: the legacy armour arithmetic (armour absorbs
    /// <c>damage * resistance</c>, but never more than it has; the rest lands on health), clamped at zero, death
    /// latched with <see cref="Dead"/>. Posts a <see cref="HitEvent"/> per applied blow and a <see cref="DeathEvent"/>
    /// for the one that crossed zero — both delivered in a further round of the same frame.
    /// </summary>
    [Reads(typeof(Dead)), Writes(typeof(Health))]
    public sealed class HealthSystem : SystemBase, IConsumes<DamageEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<DamageEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Target) || !w.Has<Health>(e.Target) || w.Has<Dead>(e.Target)) continue;
                ref var h = ref w.Get<Health>(e.Target);
                if (h.Current <= 0f) continue; // killed earlier in this same span: the tag is not added until playback

                float amount = e.Amount;
                if (h.Armor > 0f)
                {
                    float resistance = e.ArmorResistance > 0f ? e.ArmorResistance : h.ArmorResistance;
                    float absorbed = amount * resistance;
                    if (absorbed > h.Armor) absorbed = h.Armor; // armour absorbs no more than it has
                    amount -= absorbed;
                    h.Armor = Mathf.Clamp(h.Armor - absorbed, 0f, h.MaxArmor);
                }
                h.Current = Mathf.Clamp(h.Current - amount, 0f, h.Max);

                bool killed = h.Current <= 0f;
                ctx.Post(new HitEvent
                {
                    Target = e.Target, Instigator = e.Instigator, Amount = e.Amount,
                    Point = e.Point, Headshot = e.Headshot, Killed = killed,
                });
                if (killed)
                {
                    ctx.Commands.Add(e.Target, new Dead());
                    ctx.Post(new DeathEvent
                    {
                        Entity = e.Target, Instigator = e.Instigator,
                        Point = e.Point, Direction = e.Direction, Impulse = e.Impulse,
                        Explosive = e.Explosive, ExplosionRadius = e.ExplosionRadius, Headshot = e.Headshot,
                    });
                }
            }
        }
    }
}
