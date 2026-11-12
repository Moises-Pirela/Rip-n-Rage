using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

/// <summary>Collects the combat events of a frame so the assertions can look at them.</summary>
[DisableAutoCreation]
internal sealed class CombatLog : SystemBase, IConsumes<HitEvent>, IConsumes<DeathEvent>
{
    public readonly List<HitEvent> Hits = new List<HitEvent>();
    public readonly List<DeathEvent> Deaths = new List<DeathEvent>();
    public void Consume(SystemContext ctx, ReadOnlySpan<HitEvent> e) { foreach (var x in e) Hits.Add(x); }
    public void Consume(SystemContext ctx, ReadOnlySpan<DeathEvent> e) { foreach (var x in e) Deaths.Add(x); }
}

/// <summary>
/// Layer 5 (weapons, combat, damage). 5.0 pinned the legacy damage arithmetic against the old synchronous
/// EntityState; 5.1 moved it into HealthSystem (DamageEvent in, HitEvent / DeathEvent out), so the same numbers are
/// asserted here through the pipeline. One behaviour changed on purpose and is pinned the new way below: death
/// latches (the Dead tag) — the legacy base class called Die() again on every further lethal hit and left the guard
/// to its subclasses.
/// </summary>
internal static class Layer5Checks
{
    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        Damage(True, Equal);
        Headshots(True);
        EnemyHitbox(True);
        Firing(True, Equal);
        GameSystems(True);
    }

    // ================================================================= the fire pipeline, end to end

    /// <summary>killenemy in vitro: held fire input + a Weapon component → rays → DamageEvents → a dead enemy.</summary>
    private static void Firing(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var saved = Weapons.All;
        Weapons.All = new[] { new WeaponData
        {
            name = "TestGun", ammoType = AmmoType.LIGHT,
            weaponStats = new WeaponStats { bulletsPerShot = 1, fireRate = 0.1f, damagePerShot = 60f, weaponSpread = 0f },
        }};
        try
        {
            using var w = new World(0);
            w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
            w.FixedDeltaTime = 1f / 60f;
            w.DriveFixedInternally = true;
            var log = w.AddSystem(new CombatLog());

            // a shooter: an eye entity looking down +z, and the Weapon / Inventory / rig components
            var eye = w.CreateEntity();
            w.Add(eye, LocalTransform.At(new Vector3(200f, 1.1f, -5f)));
            var shooter = w.CreateEntity();
            w.Add(shooter, LocalTransform.At(new Vector3(200f, 1.1f, -5f)));
            w.Add(shooter, new Weapon());
            var inv = new Inventory(); inv.Ammo[(int)AmmoType.LIGHT] = 3;
            w.Add(shooter, inv);
            w.Add(shooter, new RipNRage.Game.Ecs.PlayerViewRig { Camera = eye });

            // the target: the enemy hitbox recipe, 100 hp, in the line of fire
            float ride = RipNRage.Engine.CharacterProfiles.Enemy.rideHeight;
            var enemy = w.CreateEntity();
            w.Add(enemy, LocalTransform.At(new Vector3(200f, 1.125f, 0f)));
            w.Add(enemy, new RipNRage.Engine.Ecs.Physics.ColliderShape
            {
                Kind = RipNRage.Engine.Ecs.Physics.ShapeKind.Capsule,
                Radius = AIState.CapsuleRadius, Height = AIState.SpriteHeight - ride, Center = new Vector3(0f, -ride * 0.5f, 0f),
            });
            w.Add(enemy, new RipNRage.Engine.Ecs.Physics.CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
            w.Add(enemy, new Health { Current = 100f, Max = 100f });
            w.Add(enemy, new Hitbox { Height = AIState.SpriteHeight, HeadFraction = AIState.HeadFraction });
            w.Update(1f / 60f); // register the collider

            RipNRage.Engine.Ecs.Input.InputOverride.Fire = true; // the automation path; the input system rewrites the singleton every tick
            w.Update(1f / 60f);
            Equal(40f, w.Get<Health>(enemy).Current, "one held-fire frame lands one 60-damage ray (100 - 60)");
            True(w.Get<Inventory>(shooter).Ammo[(int)AmmoType.LIGHT] == 2, "and spends one round");
            w.Update(1f / 60f);
            Equal(40f, w.Get<Health>(enemy).Current, "the refire timer holds the second frame's trigger");

            // let the refire window pass, keep holding: the next shot kills
            for (int i = 0; i < 8; i++) w.Update(1f / 60f);
            True(w.Has<Dead>(enemy) && log.Deaths.Count == 1, "holding fire through the refire window kills the target");
            True(w.Get<Inventory>(shooter).Ammo[(int)AmmoType.LIGHT] == 1, "two rounds spent in total");

            // a dead target soaks nothing and fire keeps consuming ammo into the world instead
            for (int i = 0; i < 8; i++) w.Update(1f / 60f);
            True(log.Deaths.Count == 1, "a dead enemy dies once");
            w.Get<Weapon>(shooter).Holstered = true;
            float ammoBefore = w.Get<Inventory>(shooter).Ammo[(int)AmmoType.LIGHT];
            for (int i = 0; i < 8; i++) w.Update(1f / 60f);
            True(w.Get<Inventory>(shooter).Ammo[(int)AmmoType.LIGHT] == (int)ammoBefore, "a holstered gun does not fire (the jab / death gate)");

            w.DestroyAllEntities();
            w.Update(1f / 60f);
        }
        finally { Weapons.All = saved; RipNRage.Engine.Ecs.Input.InputOverride.Clear(); }  // geometry cleaned by Dispose-scope DestroyAllEntities below
    }

    // ================================================================= the enemy hitbox is hittable

    /// <summary>The capsule recipe AIState gives its entity (the restored pre-2.L collider) stops a ray on the
    /// Enemies layer and names the entity — what the weapons will resolve damage through in 5.3.</summary>
    private static void EnemyHitbox(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        w.DriveFixedInternally = true;

        float ride = RipNRage.Engine.CharacterProfiles.Enemy.rideHeight;
        var enemy = w.CreateEntity();
        w.Add(enemy, LocalTransform.At(new Vector3(0f, 1.125f, 0f))); // body centre: feet on y = 0
        w.Add(enemy, new RipNRage.Engine.Ecs.Physics.ColliderShape
        {
            Kind = RipNRage.Engine.Ecs.Physics.ShapeKind.Capsule,
            Radius = AIState.CapsuleRadius, Height = AIState.SpriteHeight - ride,
            Center = new Vector3(0f, -ride * 0.5f, 0f),
        });
        w.Add(enemy, new RipNRage.Engine.Ecs.Physics.CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
        w.Add(enemy, new Health { Current = 1000f, Max = 1000f });
        w.Add(enemy, new Hitbox { Height = AIState.SpriteHeight, HeadFraction = AIState.HeadFraction });
        w.Update(1f / 60f); // one step so the sync registers the collider

        // chest-height ray from 5 m out, masked to the enemy layer like the weapons' 6421 mask
        bool hit = RipNRage.Engine.Physics.Raycast(new Vector3(0f, 1.1f, -5f), Vector3.forward,
            out var rayHit, 100f, 1 << Layers.Enemies);
        True(hit && rayHit.entity == enemy, "a ray on the Enemies layer stops at the capsule and names the entity");
        True(w.Has<Hitbox>(rayHit.entity) && w.Has<Health>(rayHit.entity),
            "and the hit entity carries the Hitbox and the Health the damage lands on");
        True(!w.Get<Hitbox>(rayHit.entity).IsHeadshot(w.Get<LocalToWorld>(rayHit.entity), rayHit.point),
            "a chest hit through the real ray is not a headshot");
        // the head band is narrow: the head line sits at 1.71 m but the capsule itself tops out at 1.9 m
        // (sprite height minus the hover gap), so anything higher misses entirely — as it did pre-2.L
        bool head = RipNRage.Engine.Physics.Raycast(new Vector3(0f, 1.8f, -5f), Vector3.forward,
            out var headHit, 100f, 1 << Layers.Enemies);
        True(head && w.Get<Hitbox>(headHit.entity).IsHeadshot(w.Get<LocalToWorld>(headHit.entity), headHit.point),
            "a 1.8 m hit through the real ray is a headshot");
        True(!RipNRage.Engine.Physics.Raycast(new Vector3(0f, 2.0f, -5f), Vector3.forward, out _, 100f, 1 << Layers.Enemies),
            "and 2.0 m clears the capsule top (1.9 m): the top of the sprite is not hittable, as before 2.L");

        // the global physics world outlives this ECS world: take the colliders out before disposing
        w.DestroyAllEntities();
        w.Update(1f / 60f);
    }

    // ================================================================= the damage model, through the pipeline

    private static void Damage(Action<bool, string> True, Action<float, float, string> Equal)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        var log = w.AddSystem(new CombatLog());

        Entity Damageable(float health, float armor, float resistance = 0.75f)
        {
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(Vector3.zero));
            w.Add(e, new Health { Current = health, Max = health });
            if (armor > 0f) w.Add(e, new Armor { Current = armor, Max = 100f, Resistance = resistance });
            return e;
        }
        void Hit(Entity e, float amount) => w.Post(new DamageEvent { Target = e, Amount = amount });

        // Armour absorbs damage * resistance and takes that much itself; the rest lands on health (the 5.0 pins).
        var d = Damageable(100f, 100f);
        Hit(d, 40f);
        w.Update(1f / 60f);
        Equal(70f, w.Get<Armor>(d).Current, "armour absorbs damage * resistance (40 * 0.75 = 30 of 100)");
        Equal(90f, w.Get<Health>(d).Current, "and health takes only the remainder (40 - 30)");
        True(log.Hits.Count == 1 && !log.Hits[0].Killed, "the applied blow is reported as a HitEvent");

        // Armour thinner than the absorption absorbs only what it has.
        d = Damageable(100f, 10f);
        Hit(d, 40f);
        w.Update(1f / 60f);
        Equal(0f, w.Get<Armor>(d).Current, "armour thinner than the absorption is spent exactly");
        Equal(70f, w.Get<Health>(d).Current, "and health takes the whole rest of the hit (40 - 10)");

        // No armour: the hit lands in full.
        d = Damageable(100f, 0f);
        Hit(d, 25f);
        w.Update(1f / 60f);
        Equal(75f, w.Get<Health>(d).Current, "an unarmoured hit lands in full");

        // Resistance 1 (the player's armour): the whole hit lands on the armour.
        d = Damageable(100f, 100f, resistance: 1f);
        Hit(d, 40f);
        w.Update(1f / 60f);
        Equal(60f, w.Get<Armor>(d).Current, "resistance 1 puts the whole hit on the armour");
        Equal(100f, w.Get<Health>(d).Current, "and health is untouched while armour holds");

        // Death: clamped at zero, latched with Dead, reported exactly once.
        log.Hits.Clear(); log.Deaths.Clear();
        d = Damageable(30f, 0f);
        Hit(d, 100f);
        w.Update(1f / 60f);
        Equal(0f, w.Get<Health>(d).Current, "health never goes below zero");
        True(w.Has<Dead>(d), "the killing blow latches the Dead tag");
        True(log.Deaths.Count == 1 && log.Hits.Count == 1 && log.Hits[0].Killed,
            "one DeathEvent, and the HitEvent is marked as the kill");
        Hit(d, 100f);
        w.Update(1f / 60f);
        True(log.Deaths.Count == 1 && log.Hits.Count == 1,
            "a lethal blow on a dead target is dropped whole (death latches — deliberate change from the legacy base class)");

        // Two lethal blows arriving in the same frame kill once: the span guard, before the tag exists.
        log.Deaths.Clear();
        d = Damageable(30f, 0f);
        Hit(d, 100f);
        Hit(d, 100f);
        w.Update(1f / 60f);
        True(log.Deaths.Count == 1, "two lethal blows in one frame report one death");

        // The death carries the blow's circumstances (what the ragdoll slicer throws the piece with).
        log.Deaths.Clear();
        d = Damageable(10f, 0f);
        w.Post(new DamageEvent
        {
            Target = d, Amount = 50f, Point = new Vector3(1, 2, 3), Direction = Vector3.right,
            Impulse = 60f, Explosive = true, ExplosionRadius = 6f,
        });
        w.Update(1f / 60f);
        True(log.Deaths.Count == 1 && log.Deaths[0].Explosive && log.Deaths[0].Impulse == 60f
             && log.Deaths[0].Point == new Vector3(1, 2, 3),
            "the DeathEvent carries the killing blow's point / impulse / explosion");

        // Overkill healing stays clamped (direct writes go through the same component).
        d = Damageable(100f, 0f);
        Hit(d, 50f);
        w.Update(1f / 60f);
        ref var h = ref w.Get<Health>(d);
        h.Current = Mathf.Clamp(h.Current + 1000f, 0f, h.Max);
        Equal(100f, w.Get<Health>(d).Current, "healing is capped at the maximum health");
    }

    // ================================================================= headshots

    private static void Headshots(Action<bool, string> True)
    {
        // the rifleman numbers: 2.25 m tall, head above 76% of that, judged from the body-centre pose
        var hb = new Hitbox { Height = 2.25f, HeadFraction = 0.76f };
        var pose = new LocalToWorld { Position = new Vector3(0f, 1.125f, 0f) }; // centre: feet at y = 0
        True(hb.IsHeadshot(in pose, new Vector3(0f, 2.0f, 0f)), "a hit at 2.0 m on a 2.25 m figure is a headshot");
        True(!hb.IsHeadshot(in pose, new Vector3(0f, 1.5f, 0f)), "a chest hit at 1.5 m is not");
        True(!hb.IsHeadshot(in pose, new Vector3(0f, 1.71f - 0.02f, 0f)) && hb.IsHeadshot(in pose, new Vector3(0f, 1.71f + 0.02f, 0f)),
            "the head line sits at 76% of the height (1.71 m)");
    }

    // ================================================================= the game assembly's ECS systems

    private static void GameSystems(Action<bool, string> True)
    {
        // Every ISystem in the game assembly must be constructible by the scheduler: a system with no parameterless
        // constructor, or one that throws in OnCreate, would only show up when a level loads.
        var gameSystems = typeof(RipNRage.Game.Ecs.PlayerLink).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ISystem).IsAssignableFrom(t))
            .ToList();
        True(gameSystems.Count > 0, "the game assembly declares ECS systems");

        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;

        foreach (var t in gameSystems)
        {
            bool optOut = t.GetCustomAttributes(typeof(DisableAutoCreationAttribute), false).Length > 0;
            True(optOut || w.GetSystem(t) != null, $"the scheduler created {t.Name}");
        }

        // and the whole set survives a frame with nothing in the world (no player, no weapons, no level)
        for (int i = 0; i < 3; i++) w.Update(1f / 60f);
        True(true, "a world with every game system steps with an empty world");
    }
}
