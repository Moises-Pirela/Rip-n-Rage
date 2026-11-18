using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs.Levels;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Game.Ecs.AI;
using RipNRage.Engine.Ecs.Nav;
using System.Collections.Generic;

/// <summary>
/// Layer 11.1: the grid is baked for a particular body. These pin every derived limit to the thing it is derived
/// from — change the Enemy profile or the grunt's capsule and one of these fails, rather than the grid quietly
/// describing a body that no longer exists.
/// </summary>
internal static class Layer11Checks
{
    /// <summary>The repo's enemies/ folder, from wherever the checks run.</summary>
    private static string FindEnemiesFolder()
    {
        var d = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (d != null) { var c = System.IO.Path.Combine(d.FullName, "enemies"); if (System.IO.Directory.Exists(c)) return c; d = d.Parent; }
        d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var c = System.IO.Path.Combine(d.FullName, "enemies"); if (System.IO.Directory.Exists(c)) return c; d = d.Parent; }
        return null;
    }

    /// <summary>A one-tile grid from a character map ('.' floor, '#' nothing), linked by the real builder — the engine suite's helper, kept small here.</summary>
    private static NavWorld HandGrid(string[] rows)
    {
        int depth = rows.Length, width = rows[0].Length;
        var settings = new NavSettings { CellSize = 1f, CellHeight = 0.2f, MaxDrop = 2.5f, Radius = 0.3f, Height = 1.8f, StepUp = 0.6f, MaxSlopeDeg = 50f };
        var nav = new NavWorld { Settings = settings, TileSize = 32f, OriginX = 0f, OriginZ = 0f, TilesX = 1, TilesZ = 1, BakeId = NavWorld.NextBakeId() };
        var tile = new NavTile { X = 0, Z = 0, MinX = 0f, MinZ = 0f, Width = width, Depth = depth, MinY = -10f, MaxY = 10f, Columns = new NavColumn[width * depth] };
        var spans = new List<NavSpan>();
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                int first = spans.Count;
                if (rows[z][x] != '#') spans.Add(new NavSpan { Floor = 0f, Headroom = 2f, Flags = NavSpanFlags.Walkable });
                tile.Columns[z * width + x] = new NavColumn { First = first, Count = spans.Count - first };
            }
        tile.Spans = spans.ToArray();
        nav.Tiles = new[] { tile };
        NavBake.Link(nav);
        return nav;
    }

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

        // 11.15: the enemy definitions are files, and the rifleman's bakes to what GameAssets used to build
        {
            var defs = RipNRage.Game.Content.EnemyDefinitions.Load(_ => null, FindEnemiesFolder());
            True(defs.Length >= 2, $"enemies: at least the rifleman and the brawler are files ({defs.Length})");
            EnemyData r = null, b = null;
            foreach (var d in defs) { if (d.classname == "monster_rifleman") r = d; if (d.classname == "monster_brawler") b = d; }
            True(r != null && b != null, "enemies: monster_rifleman and monster_brawler load by classname");
            // the numbers the code block carried (GameAssets, before 11.15), field by field
            True(r.movementSpeed == 11f && r.attackRange == 10f && r.meleeAttackRange == 2f && r.turnSpeed == 3600f && Mathf.Abs(r.fireRate - 1.1f) < 1e-5f,
                 "enemies: the rifleman's movement and engagement numbers are the code's");
            True(Mathf.Abs(r.attackWindup - 0.35f) < 1e-5f && r.burstCount == 3 && Mathf.Abs(r.burstInterval - 0.09f) < 1e-5f && Mathf.Abs(r.staggerTime - 0.25f) < 1e-5f && Mathf.Abs(r.alertDelay - 0.2f) < 1e-5f,
                 "enemies: the rifleman's attack readability numbers are the code's");
            True(r.wanderWait == 1f && r.wanderRadius == 10f && r.wanderMinDistance == 4f && r.arriveDistance == 2f,
                 "enemies: the rifleman's state tree numbers are the code's (as retuned at 11.8 / 11.11)");
            True(r.sightRange == 10f && r.sightFov == 180f && r.memory == 5f && r.noise == 30f, "enemies: the rifleman's senses are the code's");
            True(r.maxHealth == 1000f && r.maxArmor == 0f && r.skeleton == "rifleman" && !r.melee, "enemies: the rifleman's status and skeleton are the code's, and it shoots");
            True(Mathf.Abs(r.muzzleOffset.y - 0.225f) < 1e-5f && Mathf.Abs(r.muzzleOffset.z - 0.45f) < 1e-5f, "enemies: the muzzle offset is the prefab's, scene-scaled");
            True(r.enemyAnimationData.MoveAnimation.AnimationFrames == 4 && r.enemyAnimationData.MoveAnimation.Sides == 4 && r.enemyAnimationData.IdleAnimation.Sides == 4,
                 "enemies: the sheets' frame and side counts are the code's");
            True(b.melee && b.meleeDamage > 0f && b.attackRange <= b.meleeAttackRange && b.movementSpeed > r.movementSpeed,
                 "enemies: the brawler swings, hurts, closes to arm's length and is quicker on its feet");
        }

        // 11.13: local avoidance as a pure function
        {
            var a = new RipNRage.Engine.Ecs.Entity(1, 1); var b = new RipNRage.Engine.Ecs.Entity(2, 1);
            var others = new[]
            {
                new Steering.Neighbour { Position = new Vector3(0, 0, 0), Desired = new Vector3(0, 0, 5), Entity = a },
                new Steering.Neighbour { Position = new Vector3(0, 0, 1.5f), Desired = new Vector3(0, 0, -5), Entity = b },
            };
            var va = Steering.Avoid(others[0].Position, others[0].Desired, 5f, a, others, 2);
            var vb = Steering.Avoid(others[1].Position, others[1].Desired, 5f, b, others, 2);
            True(va != others[0].Desired && vb != others[1].Desired, "avoid: two bodies head-on both change course");
            True(Mathf.Abs(va.x) > 0.1f && Mathf.Abs(vb.x) > 0.1f && Mathf.Sign(va.x) != Mathf.Sign(vb.x), $"avoid: they step to opposite sides ({va.x:0.00} vs {vb.x:0.00})");
            True(va.z > 0f && vb.z < 0f, "avoid: neither turns back - a corner is perturbed, not abandoned");
            True(va.magnitude <= 5f + 1e-3f && vb.magnitude <= 5f + 1e-3f, "avoid: the result is capped at the body's speed");
            var va2 = Steering.Avoid(others[0].Position, others[0].Desired, 5f, a, others, 2);
            True(va == va2, "avoid: the same snapshot gives the same answer");

            var far = new[] { others[0], new Steering.Neighbour { Position = new Vector3(0, 0, 8f), Desired = Vector3.zero, Entity = b } };
            True(Steering.Avoid(far[0].Position, far[0].Desired, 5f, a, far, 2) == far[0].Desired, "avoid: a body outside the radius is ignored");
            var behind = new[] { others[0], new Steering.Neighbour { Position = new Vector3(0, 0, -1.5f), Desired = Vector3.zero, Entity = b } };
            True(Steering.Avoid(behind[0].Position, behind[0].Desired, 5f, a, behind, 2) == behind[0].Desired, "avoid: a body we are walking away from is ignored");
        }

        // 11.11: wander picks a span about Home, never a hole, never far from home however far the grunt has got
        {
            // a 20 x 20 m floor with a 6 x 6 m hole in the middle (cell 1 m); home at the floor's centre-left
            var rows = new string[20];
            for (int z = 0; z < 20; z++) { var r = new char[20]; for (int x = 0; x < 20; x++) r[x] = (x >= 7 && x < 13 && z >= 7 && z < 13) ? '#' : '.'; rows[z] = new string(r); }
            var nav = HandGrid(rows);
            Navigation.Current = nav;
            var def = new EnemyData { wanderRadius = 8f, wanderMinDistance = 1f };
            var brain = new AiBrain { Rng = 12345u, Home = new Vector3(5f, 0f, 10f) };
            int picks = 0, inHole = 0, farFromHome = 0;
            var self = new Vector3(5f, 0f, 10f);
            for (int i = 0; i < 1000; i++)
            {
                if (!AiBrains.PickWander(ref brain, def, self, out var p)) continue;
                picks++;
                if (p.x >= 7f && p.x < 13f && p.z >= 7f && p.z < 13f) inHole++;
                var d = p - brain.Home; d.y = 0f;
                if (d.magnitude > def.wanderRadius + 0.75f) farFromHome++; // + half a cell: the pick is snapped to a span centre
            }
            True(picks > 500, $"wander: most rolls find a span ({picks} of 1000)");
            True(inHole == 0, $"wander: no pick lands in the hole ({inHole})");
            True(farFromHome == 0, $"wander: no pick is outside the radius about home ({farFromHome})");

            // displaced 15 m: the picks still gather about Home, not about where the grunt is
            var away = new Vector3(18f, 0f, 2f);
            int nearSelf = 0, total = 0;
            for (int i = 0; i < 300; i++)
            {
                if (!AiBrains.PickWander(ref brain, def, away, out var p)) continue;
                total++;
                var dh = p - brain.Home; dh.y = 0f; var ds = p - away; ds.y = 0f;
                if (ds.magnitude < dh.magnitude) nearSelf++;
            }
            True(total > 0 && nearSelf < total / 4, $"wander: a displaced grunt's picks are about home, not about itself ({nearSelf} of {total} nearer itself)");

            // the same dice, the same picks: the run is repeatable
            var a = new AiBrain { Rng = 777u, Home = self }; var b = new AiBrain { Rng = 777u, Home = self };
            AiBrains.PickWander(ref a, def, self, out var pa); AiBrains.PickWander(ref b, def, self, out var pb);
            True(pa == pb, "wander: the same seed rolls the same pick");
            Navigation.Current = null;
        }
    }
}
