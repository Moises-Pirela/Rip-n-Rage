using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Ecs
{
    // ================================================================= water

    /// <summary>
    /// A body of water as a box (world space). Not a trigger: what goes under is the <b>camera</b>, and the camera is
    /// an entity with no body — a trigger volume waiting for it to overlap never fires, which is exactly how the old
    /// sky swap died when the camera became an entity in layer 4. A point-in-box test needs no physics at all.
    /// </summary>
    [NotInPreview] public struct WaterVolume : IComponent
    {
        public Vector3 Min, Max;
        /// <summary>
        /// The open sea: everything below the surface, as far as there is anywhere to go. The box's horizontal
        /// extent stops meaning anything — only <see cref="Max"/>.y, the surface height, does. A harbour is a box;
        /// an ocean is a half-space, and a chase that runs for kilometres cannot be a box.
        /// </summary>
        public bool Infinite;
        /// <summary>
        /// Water you can swim in rather than drown in. Off by default: the harbour a level drops you into is a way
        /// of dying, and a map says so by not saying anything. A map that wants swimming asks for it.
        /// </summary>
        public bool Swimmable;
        /// <summary>Seconds of breath under the surface before it starts to hurt (0 = you never run out).</summary>
        public float Breath;

        /// <summary>The surface height (the top of the box).</summary>
        public float SurfaceY => Max.y;

        public bool Contains(Vector3 p) =>
            Infinite
                ? p.y <= Max.y
                : p.x >= Min.x && p.x <= Max.x && p.y >= Min.y && p.y <= Max.y && p.z >= Min.z && p.z <= Max.z;
    }

    /// <summary>Whether the view is under water, and the sky setting it found on the way in (put back on the way out).</summary>
    public struct Underwater : IComponent
    {
        public bool Under;
        public bool SkyBefore;
    }

    public static class Waters
    {
        /// <summary>A water box: <paramref name="surface"/> is the middle of the top face. Immediate (level load).</summary>
        public static Entity Spawn(World w, Vector3 surface, Vector3 size, bool infinite = false)
        {
            var e = w.CreateEntity();
            w.Add(e, new WaterVolume
            {
                Min = surface - new Vector3(size.x * 0.5f, size.y, size.z * 0.5f),
                Max = surface + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f),
                Infinite = infinite,
            });
            return e;
        }

        /// <summary>The water at a point, if any is: the deepest surface over it.</summary>
        public static bool TryAt(World w, Vector3 p, out WaterVolume found)
        {
            found = default;
            bool any = false;
            foreach (var chunk in w.Query().WithReadOnly<WaterVolume>().Build())
            {
                var volumes = chunk.Get<WaterVolume>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || !volumes[i].Contains(p)) continue;
                    if (!any || volumes[i].SurfaceY > found.SurfaceY) { found = volumes[i]; any = true; }
                }
            }
            return any;
        }

        /// <summary>
        /// The height of the water at a point — one function, so that what you swim in, what you drown under and
        /// (when hulls float, V4) what holds a boat up are all the same surface. Flat today; a sum of waves later.
        /// </summary>
        public static float HeightAt(World w, float x, float z, out bool any)
        {
            any = false;
            float best = float.NegativeInfinity;
            foreach (var chunk in w.Query().WithReadOnly<WaterVolume>().Build())
            {
                var volumes = chunk.Get<WaterVolume>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref readonly var v = ref volumes[i];
                    if (!v.Infinite && (x < v.Min.x || x > v.Max.x || z < v.Min.z || z > v.Max.z)) continue;
                    if (v.SurfaceY > best) { best = v.SurfaceY; any = true; }
                }
            }
            return any ? best : 0f;
        }
    }

    /// <summary>
    /// The sea follows the eye. An infinite <see cref="WaterVolume"/> is water everywhere below its surface, and
    /// this keeps the thing you *see* under the camera to match: the plane is re-centred horizontally on the view
    /// every frame, its height untouched.
    ///
    /// <para>Nothing swims or shimmers as it moves, because the water shader takes its ripples, its normals and
    /// its waves from the world position of each pixel (<c>vWorldPos.xz * _Tiling</c>), not from the mesh's own
    /// coordinates. The surface is anchored to the world; only the patch of it we bother to draw follows.</para>
    /// </summary>
    [UpdateAfter(typeof(TransformSystem))]
    [Writes(typeof(LocalTransform))]
    public sealed class OceanSurfaceSystem : SystemBase, ILateTick
    {
        private Query _cameras, _ocean;

        public override void OnCreate()
        {
            _cameras = ActiveCamera.BuildQuery(World);
            _ocean = World.Query().WithReadOnly<WaterVolume>().With<LocalTransform>().WithReadOnly<PlaneMesh>().Build();
        }

        public void LateTick(SystemContext ctx)
        {
            // the renderer lights what is under the water from the same surface the game swims in, and the
            // physics floats hulls on (one surface, one height - see Waters.HeightAt)
            float level = RipNRage.Engine.Ecs.Physics.WaterSurface.None;
            foreach (var chunk in _ocean)
            {
                var vols = chunk.Get<WaterVolume>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i) && vols[i].SurfaceY > level) level = vols[i].SurfaceY;
            }
            var lighting = Application.Current?.Renderer?.Lighting;
            if (lighting != null) lighting.waterLevel = level;

            if (!ActiveCamera.TryGet(_cameras, out var eye, out _)) return;
            foreach (var chunk in _ocean)
            {
                var volumes = chunk.Get<WaterVolume>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || !volumes[i].Infinite) continue;
                    ref var lt = ref xforms[i];
                    lt.Position = new Vector3(eye.Position.x, lt.Position.y, eye.Position.z);
                }
            }
        }
    }

    /// <summary>
    /// Hides the sky while the view is under water and puts it back as it was when it surfaces (the old
    /// <c>EnterWaterCollision</c>'s rule). Runs after the transforms so it judges the pose this frame is drawn from.
    /// Runs alone: <c>RenderSettings</c> is renderer state, not a component.
    /// </summary>
    [UpdateAfter(typeof(TransformSystem))]
    [Exclusive] // RenderSettings.drawSky
    public sealed class UnderwaterSystem : SystemBase, ILateTick
    {
        private Query _cameras, _water;

        /// <summary>Claims the singleton outside a phase: creating its carrier entity is a structural change.</summary>
        public override void OnCreate()
        {
            World.SingletonEntity<Underwater>();
            _cameras = ActiveCamera.BuildQuery(World);
            _water = World.Query().WithReadOnly<WaterVolume>().Build();
        }

        public void LateTick(SystemContext ctx)
        {
            ref var u = ref World.Singleton<Underwater>();
            bool under = ActiveCamera.TryGet(_cameras, out var eye, out _) && InWater(eye.Position);
            if (under == u.Under) return;
            u.Under = under;
            if (under) { u.SkyBefore = RenderSettings.drawSky; RenderSettings.drawSky = false; }
            else RenderSettings.drawSky = u.SkyBefore;
        }

        private bool InWater(Vector3 p)
        {
            foreach (var chunk in _water)
            {
                var volumes = chunk.Get<WaterVolume>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i) && volumes[i].Contains(p)) return true;
            }
            return false;
        }
    }

    // ================================================================= checkpoints

    /// <summary>A checkpoint volume: saves the game the first time the player walks in.</summary>
    [NotInPreview] public struct Checkpoint : IComponent
    {
        public bool Used;
    }

    public static class Checkpoints
    {
        /// <summary>A trigger box carrying a <see cref="Checkpoint"/>. Immediate (level load).</summary>
        public static Entity Spawn(World w, Vector3 center, Vector3 size)
        {
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(center));
            w.Add(e, new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = true });
            w.Add(e, new CollisionFilter { Layer = Layers.Default, Mask = ~0, UseLayerMatrix = true });
            w.Add(e, new Trigger());
            w.Add(e, new Checkpoint());
            return e;
        }
    }

    /// <summary>
    /// The player enters a checkpoint: one full save to the "checkpoint" slot, once per checkpoint. Replaces both
    /// listeners the old volume woke — <c>CheckpointAutoSave</c>, and the leftover <c>PlayerState.OnTriggerEnter</c>
    /// that wrote a second, legacy "Save1" through <c>SaveManager</c> on the same overlap. Runs alone: the save walks
    /// the whole world and the GameObject facades, and writes a file.
    /// </summary>
    [Exclusive] // GameSaveSystem.Save
    public sealed class CheckpointSystem : SystemBase, IConsumes<TriggerEnterEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<TriggerEnterEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Trigger) || !w.Has<Checkpoint>(e.Trigger)) continue;
                if (!w.IsAlive(e.Other) || !w.Has<PlayerTag>(e.Other)) continue;
                ref var c = ref w.Get<Checkpoint>(e.Trigger);
                if (c.Used) continue;
                c.Used = true;
                GameSaveSystem.Save("checkpoint");
            }
        }
    }

    // ================================================================= the sky

}
