using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;

// Layer 10.2: authoring data for the game components a level places — one IComponentConfig per component, kept
// together because they are one subject (what a .map entity may say) and they are read as a set when a class moves
// onto a prefab in 10.3. Components written only by systems (AiIntent's per-tick fields, Dead, Flow, the view rig)
// stay runtime-only; the registry allows a component without a config.
//
// Three rules the bakes follow, so a prefab reproduces what the code spawns did:
//   * the instance pose is the map origin — AiBrain's and Bob's seeds are taken from it (placement seeds, 7.5), and
//     water's box is measured from it;
//   * a facing comes from the instance rotation (the .map "angle"), not from a field of its own;
//   * an enemy kind is a classname, resolved through Enemies.KindByClassname, never a table index in the file.

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>Hit points. <c>current</c> &lt; 0 = full.</summary>
    public sealed class HealthConfig : ComponentConfig<Health>
    {
        [InspectRange(1, 100000, 1)]
        public float max = 100f;
        [InspectTip("-1 = full")]
        public float current = -1f;
        public override Health Bake()
        {
            if (!(max > 0f) || !float.IsFinite(max)) throw new ArgumentOutOfRangeException(nameof(max), max, "health needs a finite max > 0");
            return new Health { Current = current < 0f ? max : current, Max = max };
        }
    }

    /// <summary>Armour. Absent = none at all (the grunt's 0): a prefab without this component is unarmoured.</summary>
    public sealed class ArmorConfig : ComponentConfig<Armor>
    {
        [InspectRange(1, 100000, 1)]
        public float max = 100f;
        [InspectTip("-1 = full")]
        public float current = -1f;
        /// <summary>Fraction of a hit the armour takes (0.75 for enemies, 1 for the player).</summary>
        [InspectRange(0, 1, 0.01f), InspectTip("the fraction of a hit the armour takes while it lasts")]
        public float resistance = 0.75f;
        public override Armor Bake()
        {
            if (!(max > 0f) || !float.IsFinite(max)) throw new ArgumentOutOfRangeException(nameof(max), max, "armour needs a finite max > 0");
            return new Armor { Current = current < 0f ? max : current, Max = max, Resistance = resistance };
        }
    }

    /// <summary>Where a figure can be hurt: the defaults are the rifleman's sprite (2.25 m, head above 0.76).</summary>
    public sealed class HitboxConfig : ComponentConfig<Hitbox>
    {
        [InspectRange(0.1f, 10, 0.01f), InspectTip("the figure's full height, metres")]
        public float height = 2.25f;
        [InspectRange(0, 1, 0.01f), InspectTip("hits above this fraction of the height are headshots")]
        public float headFraction = 0.76f;
        public override Hitbox Bake() => new Hitbox { Height = height, HeadFraction = headFraction };
    }
}

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>Which enemy this is, by classname (<c>monster_rifleman</c>) — the file never names a table index.</summary>
    public sealed class EnemyConfig : ComponentConfig<Enemy>
    {
        [InspectAsset(AssetKind.Classname), InspectTip("the enemy class, by classname (monster_rifleman)")]
        public string kind;
        public override Enemy Bake() => new Enemy { Kind = KindOf(kind) };

        /// <summary>The kind a classname means. Unknown while the table is loaded (the checks, an editor before
        /// GameAssets) resolves to 0; unknown with a table present is a mistake in the file.</summary>
        internal static int KindOf(string classname)
        {
            if (string.IsNullOrEmpty(classname) || Enemies.All.Length == 0) return 0;
            int k = Enemies.KindByClassname(classname);
            if (k < 0) throw new ArgumentException($"no enemy class '{classname}'", nameof(classname));
            return k;
        }
    }

    /// <summary>Facing: the instance rotation (the map's <c>angle</c>) plus an optional offset.</summary>
    public sealed class HeadingConfig : ComponentConfig<Heading>
    {
        [InspectRange(-360, 360, 1), InspectTip("added to the instance's facing (the map's angle)")]
        public float yaw;
        public override Heading Bake() => new Heading { Yaw = yaw };
        public override Heading Bake(in BakeContext ctx) => new Heading { Yaw = yaw + (ctx.HasEntity ? ctx.Rotation.eulerAngles.y : 0f) };
    }

    /// <summary>What the enemy knows: nothing, at the start of a life.</summary>
    public sealed class PerceptionConfig : ComponentConfig<Perception> { public override Perception Bake() => default; }

    /// <summary>What the brain wants: rewritten every tick, so it starts empty.</summary>
    public sealed class AiIntentConfig : ComponentConfig<AiIntent> { public override AiIntent Bake() => default; }

    /// <summary>The gun's clocks: idle at the start of a life.</summary>
    public sealed class EnemyWeaponConfig : ComponentConfig<EnemyWeapon> { public override EnemyWeapon Bake() => default; }

    /// <summary>
    /// The mind, with its dice seeded from <b>where it was placed</b> (7.5): the same placement rolls the same run,
    /// whatever else the level holds. <see cref="monster"/> salts the seed exactly as the kind did in the code spawn.
    /// </summary>
    public sealed class AiBrainConfig : ComponentConfig<AiBrain>
    {
        public AiState state = AiState.Idle;
        /// <summary>Classname salting the seed (the enemy's own kind; empty = 0, the first kind).</summary>
        [InspectAsset(AssetKind.Classname), InspectTip("salts the placement seed, exactly as the kind did")]
        public string monster;
        public override AiBrain Bake() => new AiBrain { State = state };
        public override AiBrain Bake(in BakeContext ctx) =>
            new AiBrain { State = state, Rng = ctx.HasEntity ? AiBrains.Seed(ctx.Position, EnemyConfig.KindOf(monster)) : 0u };
    }

    /// <summary>The look: which entity carries the sprite, and alive (<c>DeadTime</c> &lt; 0).</summary>
    public sealed class EnemyVisualConfig : ComponentConfig<EnemyVisual>
    {
        /// <summary>The sprite child of the tree (10.1 reference by name).</summary>
        public EntityRef sprite;
        public override EnemyVisual Bake() => new EnemyVisual { DeadTime = -1f };
        public override EnemyVisual Bake(in BakeContext ctx) => new EnemyVisual { Sprite = sprite.Resolve(ctx), DeadTime = -1f };
    }

    /// <summary>A wave spawner; <c>monster</c> is a classname, the facing comes from the instance rotation.</summary>
    public sealed class SpawnerConfig : ComponentConfig<Spawner>
    {
        [InspectAsset(AssetKind.Classname), InspectTip("what it spawns")]
        public string monster = "monster_rifleman";
        [InspectRange(0, 200, 1), InspectTip("monsters this spawner ever makes")]
        public int count = 3;
        [InspectRange(0, 120, 0.1f), InspectTip("seconds between spawns")]
        public float interval = 5f;
        [InspectRange(0, 50, 0.1f), InspectTip("scatter around the origin, metres")]
        public float radius = 2f;
        [InspectRange(0, 500, 1), InspectTip("0 = starts at once; else waits for the player within this distance")]
        public float activateRange;
        [InspectRange(0, 100, 1), InspectTip("standing at once (corpses count until they dissolve)")]
        public int maxAlive = 4;
        [InspectRange(-360, 360, 1), InspectTip("added to the instance's facing (the map's angle)")]
        public float yaw;
        public override Spawner Bake() => Make(yaw);
        public override Spawner Bake(in BakeContext ctx) => Make(yaw + (ctx.HasEntity ? ctx.Rotation.eulerAngles.y : 0f));

        private Spawner Make(float facing) => new Spawner
        {
            Kind = EnemyConfig.KindOf(monster), Count = count, Interval = interval, Radius = radius,
            ActivateRange = activateRange, MaxAlive = maxAlive, Yaw = facing,
        };
    }

    /// <summary>Where this entity came from in the level file; the loader stamps the index it gets back.</summary>
    public sealed class LevelRefConfig : ComponentConfig<LevelRef>
    {
        public override LevelRef Bake() => new LevelRef { Index = -1, SpawnerIndex = -1 };
    }
}

namespace RipNRage.Game.Ecs
{
    /// <summary>An ammo pickup: the type by name, the amount from the table unless the file overrides it.</summary>
    public sealed class PickupConfig : ComponentConfig<Pickup>
    {
        public AmmoType ammo = AmmoType.LIGHT;
        /// <summary>0 = what the type gives (<see cref="Pickups.ByType"/>).</summary>
        [InspectTip("0 = what the type gives (the table)")]
        public int amount;
        public override Pickup Bake() => new Pickup { Ammo = ammo, Amount = amount > 0 ? amount : Pickups.ByType[(int)ammo].Amount };
    }

    /// <summary>The hover, with its phase seeded from where the pickup was placed (7.5) and salted by the ammo type.</summary>
    public sealed class BobConfig : ComponentConfig<Bob>
    {
        [InspectTip("the local height it hovers around")]
        public float baseY;
        /// <summary>Salts the phase; the pickups use their own type, as the code spawn did.</summary>
        public AmmoType ammo = AmmoType.LIGHT;
        public override Bob Bake() => new Bob { BaseY = baseY };
        public override Bob Bake(in BakeContext ctx) =>
            new Bob { BaseY = baseY, Phase = ctx.HasEntity ? (AI.AiBrains.Seed(ctx.Position, (int)ammo) % 628u) / 100f : 0f };
    }

    /// <summary>A checkpoint volume, unused until someone walks into it.</summary>
    public sealed class CheckpointConfig : ComponentConfig<Checkpoint> { public override Checkpoint Bake() => default; }

    /// <summary>
    /// A water box measured from the instance origin — the middle of its top face, as <c>Waters.Spawn</c> takes it:
    /// the box hangs <c>size.y</c> below the surface and spreads half of x / z each way.
    /// </summary>
    public sealed class WaterVolumeConfig : ComponentConfig<WaterVolume>
    {
        /// <summary>"w h l", or "w l" with <see cref="depth"/> between them (the plane classes' form).</summary>
        public float[] size = { 200f, 100f, 200f };
        [InspectRange(0, 2000, 1), InspectTip("how far the water reaches down when size is \"w l\"")]
        public float depth = 100f;
        public override WaterVolume Bake() => From(Vector3.zero);
        public override WaterVolume Bake(in BakeContext ctx) => From(ctx.HasEntity ? ctx.Position : Vector3.zero);

        private WaterVolume From(Vector3 surface)
        {
            if (size == null || size.Length < 2) throw new ArgumentException("water size is \"w h l\" or \"w l\"", nameof(size));
            float w = size[0], h = size.Length >= 3 ? size[1] : depth, l = size.Length >= 3 ? size[2] : size[1];
            return new WaterVolume
            {
                Min = surface - new Vector3(w * 0.5f, h, l * 0.5f),
                Max = surface + new Vector3(w * 0.5f, 0f, l * 0.5f),
            };
        }
    }

    /// <summary>What a bullet kicks up off this collider, by surface name (the VFX bindings' spelling).</summary>
    public sealed class ImpactSurfaceConfig : ComponentConfig<ImpactSurface>
    {
        [InspectAsset(AssetKind.Surface), InspectTip("what a bullet kicks up (the VFX bindings' spelling)")]
        public string surface = Surfaces.Default;
        public override ImpactSurface Bake() => new ImpactSurface { Id = Surfaces.Id(surface) };
    }
}
