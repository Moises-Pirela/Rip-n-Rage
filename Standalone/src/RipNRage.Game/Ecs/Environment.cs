using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
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

        public bool Contains(Vector3 p) =>
            p.x >= Min.x && p.x <= Max.x && p.y >= Min.y && p.y <= Max.y && p.z >= Min.z && p.z <= Max.z;
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
        public static Entity Spawn(World w, Vector3 surface, Vector3 size)
        {
            var e = w.CreateEntity();
            w.Add(e, new WaterVolume
            {
                Min = surface - new Vector3(size.x * 0.5f, size.y, size.z * 0.5f),
                Max = surface + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f),
            });
            return e;
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

    /// <summary>The sky turning: degrees per second and how far it has gone (sim time, so every run turns the same).</summary>
    public struct SkySpin : IComponent
    {
        public float DegreesPerSecond;
        public float Angle;
    }

    public static class Sky
    {
        /// <summary>Starts the sky turning (the level / test area calls it at load; replaces SkyboxRotator).</summary>
        public static void Spin(World w, float degreesPerSecond)
        {
            ref var s = ref w.Singleton<SkySpin>();
            s.DegreesPerSecond = degreesPerSecond;
        }
    }

    /// <summary>Turns the skybox. Runs alone: <c>RenderSettings</c> is renderer state.</summary>
    [Exclusive] // RenderSettings.skyboxRotation
    public sealed class SkySpinSystem : SystemBase, ITick
    {
        public override void OnCreate() => World.SingletonEntity<SkySpin>();

        public void Tick(SystemContext ctx)
        {
            ref var s = ref World.Singleton<SkySpin>();
            if (s.DegreesPerSecond == 0f) return;
            s.Angle = (s.Angle + s.DegreesPerSecond * ctx.DeltaTime) % 360f;
            RenderSettings.skyboxRotation = s.Angle;
        }
    }
}
