using System.Collections.Generic;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>An ammo pickup: what it gives. The entity is its own trigger sphere; the sprite is a bobbing child.</summary>
    public struct Pickup : IComponent
    {
        public AmmoType Ammo;
        public int Amount;
    }

    /// <summary>A gentle hover: the entity rides up and down around <see cref="BaseY"/> (its local height).</summary>
    public struct Bob : IComponent
    {
        public float BaseY;
        /// <summary>Advances with sim time; seeded per entity, never from the shared time-seeded Random.</summary>
        public float Phase;
    }

    /// <summary>The pickup table and the spawn function (the level's <c>item_ammo_*</c> and the test area).</summary>
    public static class Pickups
    {
        /// <summary>Per <see cref="AmmoType"/>: how much a pickup gives and its sprite (BaseAmmoPickup.prefab).</summary>
        public static readonly (int Amount, string Sprite)[] ByType =
        {
            (50, "pickup_lightammo"), (25, "pickup_heavyammo"), (16, "pickup_shellammo"),
            (250, "pickup_energyammo"), (3, "pickup_explosiveammo"),
        };

        /// <summary>The prefab's trigger sphere, its sprite scale, and how high above the floor it floats.</summary>
        public const float TriggerRadius = 1.5f, SpriteScale = 0.75f, Lift = 0.9f;
        public const float BobHeight = 0.15f, BobSpeed = 2f;

        /// <summary>
        /// One pickup, whole: a static trigger sphere carrying the <see cref="Pickup"/> and the level slot, and a
        /// billboard sprite child that bobs. Immediate (level load runs outside a phase); <paramref name="floor"/>
        /// is the map origin, the pickup floats <see cref="Lift"/> above it.
        /// </summary>
        public static Entity Spawn(World w, AmmoType type, Vector3 floor) =>
            Spawn(w, type, floor, RenderAssets.Sprites.Intern(GameAssets.Pickups[ByType[(int)type].Sprite]));

        /// <summary>The same, with the sprite already resolved (the checks have no atlases to look it up in).</summary>
        public static Entity Spawn(World w, AmmoType type, Vector3 floor, AssetHandle<Sprite> sprite)
        {
            int amount = ByType[(int)type].Amount;
            // built immediately, not through PhysicsEntities (which goes through the command buffer): the level
            // loader stamps LevelRef.Index on the entity it gets back, and a deferred one is not alive yet — the
            // save would lose the pickup's key without a word
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(floor + Vector3.up * Lift));
            w.Add(e, new ColliderShape { Kind = ShapeKind.Sphere, Radius = TriggerRadius, IsStatic = true });
            w.Add(e, new CollisionFilter { Layer = Layers.Pickup, Mask = ~0, UseLayerMatrix = true });
            w.Add(e, new Trigger());
            w.Add(e, new Pickup { Ammo = type, Amount = amount });
            w.Add(e, new LevelRef { Index = -1, SpawnerIndex = -1 });

            var s = w.CreateEntity();
            w.Add(s, new LocalTransform { Position = Vector3.zero, Rotation = Quaternion.identity, Scale = new Vector3(SpriteScale, SpriteScale, 1f) });
            w.Add(s, new Parent { Value = e });
            w.Add(s, new SpriteRender
            {
                Sprite = sprite, Color = Color.white,
                Billboard = true, Lit = true, Layer = RenderLayer.World,
            });
            w.Add(s, new Bob { BaseY = 0f, Phase = (AI.AiBrains.Seed(floor, (int)type) % 628u) / 100f }); // the placement, not the index
            return e;
        }

        private static World _queryWorld;
        private static Query _query;

        /// <summary>Every pickup still standing, for the save system (one cached query per world).</summary>
        public static void Collect(World w, List<Entity> results)
        {
            if (!ReferenceEquals(_queryWorld, w)) { _queryWorld = w; _query = w.Query().With<Pickup>().Build(); }
            results.Clear();
            foreach (var chunk in _query)
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) results.Add(chunk.Entities[i]);
        }
    }

    /// <summary>
    /// The player walks into a pickup: the ammo goes into their <see cref="Inventory"/>, the pickup goes (its sprite
    /// with it, through the hierarchy). A full pocket refuses it and the pickup stays. Replaces <c>AmmoPickup</c>
    /// and its trip through the trigger bridge. Runs alone: it tells the HUD (a GameObject) the ammo changed.
    /// </summary>
    [Exclusive] // CombatController.OnAmmoChanged (the HUD's ammo counter)
    public sealed class PickupSystem : SystemBase, IConsumes<TriggerEnterEvent>
    {
        /// <summary>Taken in this dispatch: the entity only goes at playback, so a second overlap must not pay twice.</summary>
        private readonly HashSet<Entity> _taken = new HashSet<Entity>();

        public void Consume(SystemContext ctx, System.ReadOnlySpan<TriggerEnterEvent> events)
        {
            var w = ctx.World;
            _taken.Clear();
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Trigger) || !w.Has<Pickup>(e.Trigger) || _taken.Contains(e.Trigger)) continue;
                if (!w.IsAlive(e.Other) || !w.Has<PlayerTag>(e.Other) || !w.Has<Inventory>(e.Other)) continue;
                var p = w.Get<Pickup>(e.Trigger);
                if (!Weapons.AddAmmo(ref w.Get<Inventory>(e.Other), p.Ammo, p.Amount)) continue; // full: it stays
                _taken.Add(e.Trigger);
                ctx.Commands.DestroyEntity(e.Trigger); // the sprite child goes with it
                EnragedInjector<CombatController>.GetInstance()?.OnAmmoChanged?.Invoke();
            }
        }
    }

    /// <summary>Steps every <see cref="Bob"/>: sim time, so the hover is the same run every time.</summary>
    public sealed class BobSystem : SystemBase, ITick
    {
        private Query _bobs;

        public override void OnCreate() => _bobs = World.Query().With<Bob>().With<LocalTransform>().Build();

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            foreach (var chunk in _bobs)
            {
                var bobs = chunk.Get<Bob>(); var xf = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var b = ref bobs[i];
                    b.Phase += dt * Pickups.BobSpeed;
                    if (b.Phase > 2f * Mathf.PI) b.Phase -= 2f * Mathf.PI;
                    xf[i].Position.y = b.BaseY + Mathf.Sin(b.Phase) * Pickups.BobHeight;
                }
            }
        }
    }
}
