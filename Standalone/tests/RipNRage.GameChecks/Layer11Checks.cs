using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs.Levels;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Game.Ecs.AI;

/// <summary>
/// Layer 11.1: the grid is baked for a particular body. These pin every derived limit to the thing it is derived
/// from — change the Enemy profile or the grunt's capsule and one of these fails, rather than the grid quietly
/// describing a body that no longer exists.
/// </summary>
internal static class Layer11Checks
{
    public static void Run(Action<bool, string> True)
    {
        var profile = CharacterProfiles.Enemy;

        True(NavProfile.Radius == Enemies.CapsuleRadius,
             "nav profile: the clearance is the grunt's capsule radius");
        True(NavProfile.Height == Enemies.Height - profile.rideHeight,
             "nav profile: the headroom is the capsule height, the hover gap taken off");
        True(NavProfile.StepUp == profile.rideHeight + profile.groundTolerance,
             "nav profile: the step is what the ride spring swallows (ride + ground tolerance)");
        True(NavProfile.MaxSlopeDeg == profile.maxSlopeAngle,
             "nav profile: the slope limit is the profile's");

        // the numbers themselves, so a silent change to either source is visible in the diff
        True(Mathf.Abs(NavProfile.Radius - 0.3375f) < 1e-4f, $"nav profile: the clearance is 0.3375 m (got {NavProfile.Radius})");
        True(Mathf.Abs(NavProfile.Height - 1.9f) < 1e-4f, $"nav profile: the headroom is 1.90 m (got {NavProfile.Height})");
        True(Mathf.Abs(NavProfile.StepUp - 0.6f) < 1e-4f, $"nav profile: the step is 0.60 m (got {NavProfile.StepUp})");

        // the three map keys, and what they fall back to when a level does not say
        var ws = new WorldspawnConfig().Bake();
        var s = NavProfile.For(in ws);
        True(s.CellSize == 0.5f && s.CellHeight == 0.2f && s.MaxDrop == 4f,
             "nav profile: a level that says nothing gets the default cell size, cell height and drop");

        var given = new WorldspawnConfig { navCellSize = 0.25f, navCellHeight = 0.1f, navMaxDrop = 4f }.Bake();
        var s2 = NavProfile.For(in given);
        True(s2.CellSize == 0.25f && s2.CellHeight == 0.1f && s2.MaxDrop == 4f,
             "nav profile: a level's nav_cell_size / nav_cell_height / nav_max_drop are used");
        True(s2.Radius == NavProfile.Radius && s2.StepUp == NavProfile.StepUp,
             "nav profile: the body's limits are not settings - a map cannot override them");

        // 11.3: the serving order is the placement seed, never the entity index
        True(NavPathSystem.CompareAskers(10u, 999, 20u, 1) < 0, "nav paths: the lower placement seed is served first, whatever the entity index");
        True(NavPathSystem.CompareAskers(20u, 1, 10u, 999) > 0, "nav paths: and the higher one second");
        True(NavPathSystem.CompareAskers(7u, 3, 7u, 9) < 0, "nav paths: two identical placements fall back to the entity index");
        True(NavPathSystem.CompareAskers(7u, 4, 7u, 4) == 0, "nav paths: the same asker ties with itself");

        // 11.4: the drop rule is at least the ledge guard's reach, or navigation refuses what blind walking allowed
        True(NavProfile.For(in ws).MaxDrop >= Steering.Drop,
             $"nav profile: the drop rule covers the ledge guard's reach ({NavProfile.For(in ws).MaxDrop} vs {Steering.Drop})");
    }
}
