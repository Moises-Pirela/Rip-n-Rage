using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 10.2: the configs for the game components a level places. Each bake is judged against what the code spawn
/// puts on the entity today — the pickups and the water against an actual <c>Spawn</c>, the rest against the values
/// those spawns use — because 10.3 swaps the code for prefab files and the level manifest must not move.
/// </summary>
internal static class Layer10Checks
{
    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var savedEnemies = Enemies.All;
        Enemies.All = new[]
        {
            new EnemyData { name = "TestGrunt", classname = "monster_rifleman", maxHealth = 1000f, maxArmor = 0f },
            new EnemyData { name = "TestHeavy", classname = "monster_heavy", maxHealth = 50f, maxArmor = 40f },
        };
        try { Checks(True, Equal); }
        finally { Enemies.All = savedEnemies; }
    }

    /// <summary>The repo's prefabs/, found by walking up from the test binary (the game walks up from its assets).</summary>
    private static string RepoPrefabs()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string c = System.IO.Path.Combine(d.FullName, "prefabs");
            if (System.IO.Directory.Exists(c)) return c;
            d = d.Parent;
        }
        return null;
    }

    private static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); return false; } catch (T) { return true; }
    }

    private static void Checks(Action<bool, string> True, Action<float, float, string> Equal)
    {
        using var w = new World(0);

        // ---- combat: health, armour, the hitbox
        var hp = new HealthConfig { max = 1000f }.Bake();
        Equal(1000f, hp.Current, "health bakes full by default");
        Equal(1000f, hp.Max, "health keeps its max");
        Equal(30f, new HealthConfig { max = 100f, current = 30f }.Bake().Current, "a wounded start is a field");
        True(Throws<ArgumentOutOfRangeException>(() => new HealthConfig { max = 0f }.Bake()), "health without a max is refused");
        var armor = new ArmorConfig { max = 40f }.Bake();
        True(armor.Current == 40f && armor.Resistance == 0.75f, "armour bakes full, at the enemies' resistance (the player's prefab says 1)");
        var hb = new HitboxConfig().Bake();
        True(hb.Height == Enemies.Height && hb.HeadFraction == Enemies.HeadFraction, "the hitbox defaults are the rifleman sprite's own numbers");

        // ---- the enemy's kind is a classname, never a table index
        Equal(1f, new EnemyConfig { kind = "monster_heavy" }.Bake().Kind, "an enemy kind resolves from its classname");
        Equal(0f, new EnemyConfig().Bake().Kind, "no classname is the first kind");
        True(Throws<ArgumentException>(() => new EnemyConfig { kind = "monster_nope" }.Bake()), "an unknown classname is refused");

        // ---- the pose-dependent bakes, through a prefab spawn (a bare Bake has no context)
        var lib = new PrefabLibrary();
        lib.AddJson(@"{ ""name"": ""grunt"",
            ""components"": { ""Enemy"": { ""kind"": ""monster_rifleman"" }, ""Heading"": { }, ""AiBrain"": { ""monster"": ""monster_rifleman"" },
                              ""AiIntent"": { }, ""Perception"": { }, ""EnemyWeapon"": { }, ""Health"": { ""max"": 1000 },
                              ""Hitbox"": { }, ""EnemyVisual"": { ""sprite"": ""sprite"" }, ""LevelRef"": { } },
            ""children"": { ""sprite"": { ""components"": { ""SpriteRender"": { ""billboard"": true, ""lit"": true }, ""SpriteFx"": { } } } } }");
        var floor = new Vector3(-5f, 0.91f, -16f);
        var grunt = lib.Get("grunt").Spawn(w, floor, Quaternion.Euler(0f, 180f, 0f));
        Equal(180f, w.Get<Heading>(grunt.Root).Yaw, "a facing comes from the instance rotation (the map's angle)");
        True(w.Get<AiBrain>(grunt.Root).Rng == AiBrains.Seed(floor, 0), "the brain's dice are seeded from where the prefab was placed, salted by its kind");
        True(w.Get<EnemyVisual>(grunt.Root).Sprite == grunt["sprite"] && w.Get<EnemyVisual>(grunt.Root).DeadTime == -1f, "the look points at the sprite child and starts alive");
        True(w.Get<LevelRef>(grunt.Root).Index == -1 && w.Get<LevelRef>(grunt.Root).SpawnerIndex == -1, "a level reference starts unstamped");
        var spawned = Enemies.Spawn(w, 0, floor, 180f);
        True(w.Get<AiBrain>(spawned).Rng == w.Get<AiBrain>(grunt.Root).Rng, "the prefab's brain is seeded exactly as Enemies.Spawn seeds it");
        Equal(w.Get<Heading>(spawned).Yaw, w.Get<Heading>(grunt.Root).Yaw, "and faces the same way");
        Equal(w.Get<Health>(spawned).Max, w.Get<Health>(grunt.Root).Max, "with the same health");
        Equal(w.Get<Hitbox>(spawned).Height, w.Get<Hitbox>(grunt.Root).Height, "and the same hitbox");

        // ---- the spawner: a classname, a facing from the rotation, the rest its own fields
        lib.AddJson(@"{ ""name"": ""wave"", ""components"": { ""Spawner"": { ""monster"": ""monster_heavy"", ""count"": 5, ""interval"": 2.5, ""radius"": 3, ""activateRange"": 25, ""maxAlive"": 2 } } }");
        var sp = w.Get<Spawner>(lib.Get("wave").Spawn(w, Vector3.zero, Quaternion.Euler(0f, 90f, 0f)).Root);
        True(sp.Kind == 1 && sp.Count == 5 && sp.MaxAlive == 2, "a spawner bakes its monster (by classname), its count and its ceiling");
        Equal(2.5f, sp.Interval, "its interval");
        Equal(3f, sp.Radius, "its scatter");
        Equal(25f, sp.ActivateRange, "its activation range");
        Equal(90f, sp.Yaw, "and the facing it hands its monsters, from the instance rotation");

        // ---- pickups: the prefab tree against Pickups.Spawn, field for field
        // no atlases here: both sides carry no sprite, which is beside the point — the placement-seeded hover is not
        lib.AddJson(@"{ ""name"": ""ammo_shell"",
            ""components"": { ""LocalTransform"": { ""position"": [0, 0.9, 0] }, ""ColliderShape"": { ""kind"": ""Sphere"", ""radius"": 1.5, ""isStatic"": true },
                              ""CollisionFilter"": { ""layer"": 16, ""useLayerMatrix"": true }, ""Trigger"": { },
                              ""Pickup"": { ""ammo"": ""SHELL"" }, ""LevelRef"": { } },
            ""children"": { ""sprite"": { ""components"": { ""LocalTransform"": { ""scale"": [0.75, 0.75, 1] },
                                                           ""SpriteRender"": { ""billboard"": true, ""lit"": true },
                                                           ""Bob"": { ""ammo"": ""SHELL"" } } } } }");
        var pickFloor = new Vector3(-3f, 0.91f, -20f);
        var fromPrefab = lib.Get("ammo_shell").Spawn(w, pickFloor, Quaternion.identity);
        var fromCode = Pickups.Spawn(w, AmmoType.SHELL, pickFloor, default);
        var codeSprite = w.Get<LocalTransform>(fromPrefab["sprite"]); // shape check below uses both children
        True(w.Get<Pickup>(fromPrefab.Root).Ammo == w.Get<Pickup>(fromCode).Ammo && w.Get<Pickup>(fromPrefab.Root).Amount == w.Get<Pickup>(fromCode).Amount,
            "the pickup's type and amount match the code spawn (the amount from the table)");
        True((w.Get<LocalTransform>(fromPrefab.Root).Position - w.Get<LocalTransform>(fromCode).Position).magnitude < 1E-4f,
            "the prefab's lift above the floor is the code spawn's");
        Entity codeChild = Entity.Null;
        foreach (var e in w.AllEntities()) if (w.Has<Parent>(e) && w.Get<Parent>(e).Value == fromCode) codeChild = e;
        True(!codeChild.IsNull && w.Get<Bob>(fromPrefab["sprite"]).Phase == w.Get<Bob>(codeChild).Phase && codeSprite.Scale == w.Get<LocalTransform>(codeChild).Scale,
            "the hover's phase is seeded from the same placement and the sprite child has the same scale");
        True(w.Get<ColliderShape>(fromPrefab.Root).Radius == Pickups.TriggerRadius && w.Has<Trigger>(fromPrefab.Root),
            "the trigger sphere is the prefab's own, at the pickup radius");
        Equal(16f, new PickupConfig { ammo = AmmoType.SHELL }.Bake().Amount, "SHELL gives what its table row says");
        Equal(7f, new PickupConfig { ammo = AmmoType.SHELL, amount = 7 }.Bake().Amount, "a file may override the amount");

        // ---- water: the box is measured from the instance origin, as Waters.Spawn measures it
        var surface = new Vector3(474.6f, -0.5f, 0f);
        lib.AddJson(@"{ ""name"": ""water"", ""components"": { ""WaterVolume"": { ""size"": [1732, 100, 1000] } } }");
        var waterPrefab = w.Get<WaterVolume>(lib.Get("water").Spawn(w, surface, Quaternion.identity).Root);
        var waterCode = w.Get<WaterVolume>(Waters.Spawn(w, surface, new Vector3(1732f, 100f, 1000f)));
        True((waterPrefab.Min - waterCode.Min).magnitude < 1E-3f && (waterPrefab.Max - waterCode.Max).magnitude < 1E-3f,
            "the water box bakes to the same corners the code spawn computes");

        // ---- surfaces and checkpoints
        Equal(Surfaces.Id(Surfaces.Wood), new ImpactSurfaceConfig { surface = "wood" }.Bake().Id, "a surface bakes to its interned id");
        True(!new CheckpointConfig().Bake().Used, "a checkpoint starts unused");

        // ---- the preview leaves out what simulates or acts
        var preview = lib.Get("ammo_shell").SpawnPreview(w, pickFloor, Quaternion.identity);
        True(!w.Has<Pickup>(preview.Root) && !w.Has<Trigger>(preview.Root) && !w.Has<ColliderShape>(preview.Root) && !w.Has<LevelRef>(preview.Root),
            "the pickup's preview carries no pickup, trigger, collider or level slot");
        True(w.Has<SpriteRender>(preview["sprite"]) && w.Has<Bob>(preview["sprite"]), "but it still looks like one (the sprite child and its hover stay)");
        var gruntPreview = lib.Get("grunt").SpawnPreview(w, floor, Quaternion.identity);
        True(!w.Has<AiBrain>(gruntPreview.Root) && !w.Has<Perception>(gruntPreview.Root) && !w.Has<EnemyWeapon>(gruntPreview.Root)
             && w.Has<Health>(gruntPreview.Root) && w.Has<EnemyVisual>(gruntPreview.Root),
            "an enemy preview has no mind, senses or gun, and keeps what it looks like");

        // ---- the real file: prefabs/level/monster_rifleman.json builds what Enemies.Spawn builds (10.3c)
        GamePrefabs.Reset();
        GamePrefabs.Directory = RepoPrefabs();
        True(GamePrefabs.Directory != null && GamePrefabs.Library.Contains("monster_rifleman"), "the level prefabs are on disk where the game looks for them");
        var file = GamePrefabs.Library.Get("monster_rifleman").Spawn(w, floor, Quaternion.Euler(0f, 180f, 0f));
        var fileSprite = file["sprite"];
        True((w.Get<LocalTransform>(file.Root).Position - w.Get<LocalTransform>(spawned).Position).magnitude < 1E-4f, "the file puts the body where the code puts it (the ride-height lift)");
        var fs = w.Get<ColliderShape>(file.Root); var cs = w.Get<ColliderShape>(spawned);
        True(fs.Kind == cs.Kind && MathF.Abs(fs.Radius - cs.Radius) < 1E-5f && MathF.Abs(fs.Height - cs.Height) < 1E-5f && (fs.Center - cs.Center).magnitude < 1E-5f,
            "with the same capsule (radius, height and centre all follow the Enemy profile's ride height)");
        var fb = w.Get<RipNRage.Engine.Ecs.Physics.Body>(file.Root); var cb = w.Get<RipNRage.Engine.Ecs.Physics.Body>(spawned);
        True(fb.Mass == cb.Mass && fb.Interpolate == cb.Interpolate && fb.FreezeRotation == cb.FreezeRotation && fb.CanSleep == cb.CanSleep, "the same body");
        True(w.Get<CharacterMotor>(file.Root).RideHeight == w.Get<CharacterMotor>(spawned).RideHeight && w.Get<CharacterMotor>(file.Root).Profile == w.Get<CharacterMotor>(spawned).Profile, "the same motor");
        True(w.Get<Health>(file.Root).Max == w.Get<Health>(spawned).Max, "the same health (the file repeats EnemyData's number: this is the pin)");
        Equal(w.Get<Hitbox>(spawned).Height, w.Get<Hitbox>(file.Root).Height, "the same hitbox");
        True(w.Get<AiBrain>(file.Root).Rng == w.Get<AiBrain>(spawned).Rng, "the same placement seed");
        Equal(w.Get<Heading>(spawned).Yaw, w.Get<Heading>(file.Root).Yaw, "the same facing (the file reads it off the instance rotation, so it is a quaternion round trip)");
        True(w.Get<EnemyVisual>(file.Root).Sprite == fileSprite && w.Get<CollisionFilter>(file.Root).Layer == w.Get<CollisionFilter>(spawned).Layer, "the look points at its own sprite child, on the enemy layer");
        True(w.Get<LocalTransform>(fileSprite).Position == new Vector3(0f, -0.35f, 0f) && w.Has<SpriteFx>(fileSprite) && w.Get<SpriteRender>(fileSprite).Billboard,
            "and the sprite child hangs where the code hangs it, billboarded, with its effects slot");

        // ---- a prefab file changed on disk is picked up (10.7)
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rnr-prefab-watch-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(tmp, "level"));
            string watched = System.IO.Path.Combine(tmp, "level", "watch_me.json");
            System.IO.File.WriteAllText(watched, @"{ ""name"": ""watch_me"", ""components"": { ""Health"": { ""max"": 10 } } }");
            var savedDir = GamePrefabs.Directory;
            try
            {
                GamePrefabs.Reset(); GamePrefabs.Directory = tmp;
                True(GamePrefabs.Library.Get("watch_me").Get<HealthConfig>().max == 10f, "a prefab folder loads");
                string reloaded = null;
                GamePrefabs.OnReloaded = n => reloaded = n;
                System.IO.File.WriteAllText(watched, @"{ ""name"": ""watch_me"", ""components"": { ""Health"": { ""max"": 99 } } }");
                System.IO.File.SetLastWriteTimeUtc(watched, DateTime.UtcNow.AddSeconds(1));
                int n = GamePrefabs.PollReload(0f);
                True(n == 1 && reloaded == "watch_me", "a changed file is noticed and reported by name");
                True(GamePrefabs.Library.Get("watch_me").Get<HealthConfig>().max == 99f, "and what it spawns follows the file");
                System.IO.File.WriteAllText(watched, "{ not json");
                System.IO.File.SetLastWriteTimeUtc(watched, DateTime.UtcNow.AddSeconds(2));
                GamePrefabs.PollReload(0f);
                True(GamePrefabs.Library.Get("watch_me").Get<HealthConfig>().max == 99f, "a file that does not parse is a warning, not a lost prefab");
            }
            finally
            {
                GamePrefabs.OnReloaded = null; GamePrefabs.Reset(); GamePrefabs.Directory = savedDir;
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        // ---- every component a level places can be authored (what 10.3 relies on)
        foreach (var t in new[]
        {
            typeof(Health), typeof(Armor), typeof(Hitbox), typeof(Enemy), typeof(Heading), typeof(Perception),
            typeof(AiBrain), typeof(AiIntent), typeof(EnemyWeapon), typeof(EnemyVisual), typeof(SpriteFx),
            typeof(Spawner), typeof(Pickup), typeof(Bob), typeof(Checkpoint), typeof(WaterVolume),
            typeof(ImpactSurface), typeof(LevelRef),
        })
            True(ComponentRegistry.Get(t).ConfigType != null, $"{t.Name} has a config");
    }
}
