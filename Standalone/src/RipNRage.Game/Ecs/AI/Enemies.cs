using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// Where this enemy came from: its slot in the level file (<see cref="Index"/>, -1 = spawned at runtime) and
    /// the spawner that made it (<see cref="SpawnerIndex"/>, -1 = placed directly). The save system keys monsters
    /// by it — the save's key for every level-placed entity.
    /// </summary>
    public struct LevelRef : IComponent
    {
        public int Index;
        public int SpawnerIndex;
    }

    /// <summary>The enemy definitions, by kind — shared immutable data, the weapons / projectiles pattern.</summary>
    public static class Enemies
    {
        public static EnemyData[] All = System.Array.Empty<EnemyData>();

        public static int KindOf(EnemyData data) => System.Array.IndexOf(All, data);

        /// <summary>The kind whose definition claims <paramref name="classname"/> (-1 = unknown).</summary>
        public static int KindByClassname(string classname)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].classname, classname, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The legacy capsule, from the pre-2.L prefab: radius 0.15 * 2.25, height 2.25 minus the hover gap,
        /// centred half the ride height below the body-centre transform.</summary>
        public const float Height = 2.25f, CapsuleRadius = 0.15f * Height;
        /// <summary>Hits above this fraction of the sprite height are headshots (the old IsHeadshot).</summary>
        public const float HeadFraction = 0.76f;

        /// <summary>
        /// Spawns an enemy with its feet on <paramref name="floorPosition"/> (the body floats its ride height above
        /// the floor), facing <paramref name="yaw"/>. One entity, whole: the body (capsule + spring + motor), the
        /// health, the sprite-child look, the senses, the mind and the gun. Definitions in, an entity out.
        /// </summary>
        public static Entity Spawn(World w, int kind, Vector3 floorPosition, float yaw)
        {
            var def = All[kind];
            float rideHeight = CharacterProfiles.Enemy.rideHeight;
            var position = floorPosition + Vector3.up * (0.5f * Height + rideHeight);

            var e = w.CreateEntity();
            w.Add(e, new LocalTransform { Position = position, Rotation = Quaternion.Euler(0f, yaw, 0f), Scale = Vector3.one });
            w.Add(e, new ColliderShape
            {
                Kind = ShapeKind.Capsule, Radius = CapsuleRadius, Height = Height - rideHeight,
                Center = new Vector3(0f, -rideHeight * 0.5f, 0f),
            });
            w.Add(e, new CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
            w.Add(e, new BodyConfig { mass = CharacterProfiles.Enemy.mass, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
            w.Add(e, new BodyVelocity());
            w.Add(e, new CharacterMotorConfig { profile = CharacterProfileKind.Enemy, rideHeight = rideHeight }.Bake());
            w.Add(e, new Health { Current = def.maxHealth, Max = def.maxHealth });
            if (def.maxArmor > 0f) w.Add(e, new Armor { Current = def.maxArmor, Max = def.maxArmor, Resistance = 0.75f });
            w.Add(e, new Hitbox { Height = Height, HeadFraction = HeadFraction });

            // the look: an 8-sided sheet on a sprite child (feet on the ground: the old SpriteHolder offset and scale)
            var sprite = w.CreateEntity();
            w.Add(sprite, new LocalTransform
            {
                Position = new Vector3(0f, -rideHeight, 0f), Rotation = Quaternion.identity,
                Scale = Vector3.one * (Height / 5.12f), // 512 px tiles @100 ppu, ~2.25 units tall
            });
            w.Add(sprite, new Parent { Value = e });
            w.Add(sprite, new SpriteRender { Color = Color.white, Billboard = true, Lit = true, Layer = RenderLayer.World, CastsShadow = true });
            w.Add(sprite, new SpriteFx());
            w.Add(e, new EnemyVisual { Sprite = sprite, DeadTime = -1f });

            // the mind (6.2), the senses (6.3), the body (6.4) and the gun (6.5)
            w.Add(e, new Enemy { Kind = kind });
            w.Add(e, new Heading { Yaw = yaw }); // the body never turns (6.4): facing is data
            w.Add(e, new Perception());
            w.Add(e, new AiBrain { Rng = AiBrains.Seed(floorPosition, kind) }); // seeded by the placement: the same run every time
            w.Add(e, new AiIntent());
            w.Add(e, new EnemyWeapon());
            w.Add(e, new LevelRef { Index = -1, SpawnerIndex = -1 });
            return e;
        }

        /// <summary>
        /// The buffered twin of <see cref="Spawn(World,int,Vector3,float)"/>, for systems (the immediate structural
        /// API is disabled while a phase runs): the enemy materialises whole at the next sync point, stamped with
        /// <paramref name="spawnerIndex"/>. Keep the recipe in lockstep with the twin — a gameplay check compares
        /// the archetypes they build.
        /// </summary>
        public static Entity Spawn(CommandBuffer cmd, int kind, Vector3 floorPosition, float yaw, int spawnerIndex)
        {
            var def = All[kind];
            float rideHeight = CharacterProfiles.Enemy.rideHeight;
            var position = floorPosition + Vector3.up * (0.5f * Height + rideHeight);

            var e = cmd.CreateEntity();
            cmd.Add(e, new LocalTransform { Position = position, Rotation = Quaternion.Euler(0f, yaw, 0f), Scale = Vector3.one });
            cmd.Add(e, new ColliderShape
            {
                Kind = ShapeKind.Capsule, Radius = CapsuleRadius, Height = Height - rideHeight,
                Center = new Vector3(0f, -rideHeight * 0.5f, 0f),
            });
            cmd.Add(e, new CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
            cmd.Add(e, new BodyConfig { mass = CharacterProfiles.Enemy.mass, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
            cmd.Add(e, new BodyVelocity());
            cmd.Add(e, new CharacterMotorConfig { profile = CharacterProfileKind.Enemy, rideHeight = rideHeight }.Bake());
            cmd.Add(e, new Health { Current = def.maxHealth, Max = def.maxHealth });
            if (def.maxArmor > 0f) cmd.Add(e, new Armor { Current = def.maxArmor, Max = def.maxArmor, Resistance = 0.75f });
            cmd.Add(e, new Hitbox { Height = Height, HeadFraction = HeadFraction });

            var sprite = cmd.CreateEntity();
            cmd.Add(sprite, new LocalTransform
            {
                Position = new Vector3(0f, -rideHeight, 0f), Rotation = Quaternion.identity,
                Scale = Vector3.one * (Height / 5.12f),
            });
            cmd.Add(sprite, new Parent { Value = e });
            cmd.Add(sprite, new SpriteRender { Color = Color.white, Billboard = true, Lit = true, Layer = RenderLayer.World, CastsShadow = true });
            cmd.Add(sprite, new SpriteFx());
            cmd.Add(e, new EnemyVisual { Sprite = sprite, DeadTime = -1f });

            cmd.Add(e, new Enemy { Kind = kind });
            cmd.Add(e, new Heading { Yaw = yaw });
            cmd.Add(e, new Perception());
            cmd.Add(e, new AiBrain { Rng = AiBrains.Seed(floorPosition, kind) }); // a deferred entity's index is not final
            cmd.Add(e, new AiIntent());
            cmd.Add(e, new EnemyWeapon());
            cmd.Add(e, new LevelRef { Index = -1, SpawnerIndex = spawnerIndex });
            return e;
        }

        // ---------------------------------------------------------------- ad-hoc iteration (probes, saves)

        private static World _queryWorld;
        private static Query _query;

        /// <summary>The live-enemies query for code outside a system (queries register with their world for good,
        /// so this caches one per world instead of building per call).</summary>
        public static Query Of(World w)
        {
            if (!ReferenceEquals(_queryWorld, w)) { _queryWorld = w; _query = w.Query().With<Enemy>().Build(); }
            return _query;
        }

        /// <summary>Every live enemy, into <paramref name="results"/> (cleared first).</summary>
        public static void Collect(World w, System.Collections.Generic.List<Entity> results)
        {
            results.Clear();
            foreach (var chunk in Of(w))
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) results.Add(chunk.Entities[i]);
        }

        /// <summary>Pulses the enemy's sprite in <paramref name="color"/> (attack wind-up, alert).</summary>
        public static void Telegraph(World w, Entity enemy, float duration, Color color)
        {
            if (!w.IsAlive(enemy) || !w.Has<EnemyVisual>(enemy)) return;
            ref var v = ref w.Get<EnemyVisual>(enemy);
            v.TelegraphLeft = v.TelegraphDuration = Mathf.Max(0.01f, duration);
            v.TelegraphColor = color;
        }

        public static void SetAnim(World w, Entity enemy, AnimationState state)
        {
            if (w.IsAlive(enemy) && w.Has<EnemyVisual>(enemy)) w.Get<EnemyVisual>(enemy).State = state;
        }
    }
}
