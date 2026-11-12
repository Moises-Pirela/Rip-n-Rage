using System;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;

/// <summary>
/// Layer 5 (weapons, combat, damage). Step 5.0 pins what the port has to reproduce — the legacy damage / armour
/// arithmetic, which is the one piece of combat maths that is not obvious — and proves the game assembly's ECS
/// systems are discoverable and run, which nothing asserted before (the animation editor shipped for two layers
/// without registering them, and its clip-event flashes were silently dead as a result).
/// </summary>
internal static class Layer5Checks
{
    /// <summary>A damageable with no scene dependencies: exercises EntityState's own arithmetic.</summary>
    private sealed class Dummy : EntityState
    {
        public int Deaths;
        public int HealthChanges, ArmorChanges;
        public override void Init() { }
        public override void Die() => Deaths++;
    }

    private static Dummy NewDummy(float health, float armor)
    {
        var d = new GameObject("dummy").AddComponent<Dummy>();
        d.InitializeHealth(health);
        d.InitializeArmor(armor);
        d.OnHealthChanged += () => d.HealthChanges++;
        d.OnArmorChanged += () => d.ArmorChanges++;
        return d;
    }

    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        Damage(True, Equal);
        GameSystems(True);
    }

    // ================================================================= the damage model layer 5 has to preserve

    private static void Damage(Action<bool, string> True, Action<float, float, string> Equal)
    {
        // Armour absorbs `damage * resistance` and takes that much itself; the rest goes to health. The default
        // resistance is 0.75 for everything except the player (PlayerState passes 1, so armour eats the whole hit).
        var d = NewDummy(100f, 100f);
        d.TakeDamage(40f);
        Equal(70f, d.Armor, "armour absorbs damage * resistance (40 * 0.75 = 30 of 100)");
        Equal(90f, d.Health, "and health takes only the remainder (40 - 30)");

        // Armour thinner than the absorption absorbs only what it has — the clamp in the middle of TakeDamage, which
        // reads like a no-op (`armorAbsorption -= armorDelta`) but is what stops armour going negative.
        d = NewDummy(100f, 10f);
        d.TakeDamage(40f);
        Equal(0f, d.Armor, "armour thinner than the absorption is spent exactly");
        Equal(70f, d.Health, "and health takes the whole rest of the hit (40 - 10)");

        // No armour: the hit lands in full.
        d = NewDummy(100f, 0f);
        d.TakeDamage(25f);
        Equal(0f, d.Armor, "no armour stays no armour");
        Equal(75f, d.Health, "an unarmoured hit lands in full");

        // Resistance 1 (the player): armour eats the hit one for one.
        d = NewDummy(100f, 100f);
        d.TakeDamage(40f, 1f);
        Equal(60f, d.Armor, "resistance 1 puts the whole hit on the armour");
        Equal(100f, d.Health, "and health is untouched while armour holds");

        // Health is clamped at zero and death fires once, from the hit that crosses it.
        d = NewDummy(30f, 0f);
        d.TakeDamage(100f);
        Equal(0f, d.Health, "health never goes below zero");
        True(d.Deaths == 1, "the hit that empties health reports a death");
        d.TakeDamage(100f);
        True(d.Deaths == 2, "EntityState itself does not latch death (PlayerState / AIState guard that)");

        // Healing and armour pickups are clamped to the maxima captured at initialisation.
        d = NewDummy(100f, 100f);
        d.TakeDamage(50f);
        d.Heal(1000f);
        Equal(100f, d.Health, "healing is capped at the maximum health");
        d.ApplyArmor(1000f);
        Equal(100f, d.Armor, "armour is capped at the maximum armour");
        True(d.HealthChanges > 0 && d.ArmorChanges > 0, "health and armour changes notify their listeners");
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
