using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Game.Content;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The death camera (8.3), on the view rig's pivot: where the fall starts and ends. The pose in between is a
    /// pure function of <see cref="Flow.DeathClock"/> (<see cref="DeathCams.Pose"/>), so there is no timer here — the
    /// clock that times the death screen and the slow motion times the fall too. Replaces the
    /// <c>PlayerDeathSequence</c> MonoBehaviour and its coroutine.
    /// </summary>
    public struct DeathCam : IComponent
    {
        public Vector3 StartPosition, EndPosition;
        public Quaternion StartRotation, EndRotation;
    }

    /// <summary>The fall's numbers (the old <c>PlayerDeathSequence</c> fields) and its pure pose.</summary>
    public static class DeathCams
    {
        /// <summary>Real seconds the camera takes to reach the floor; the slow motion ramps over the same time.</summary>
        public const float FallDuration = 1.15f;
        /// <summary>Local height of the pivot at the end of the fall (the capsule's bottom is at −0.9).</summary>
        public const float FloorHeight = -0.62f;
        public const float RollAngle = 42f, ExtraPitch = 14f;
        /// <summary>The time scale the world settles at once the fall is over (0 would freeze the enemies).</summary>
        public const float SlowMotion = 0.2f;

        /// <summary>How far through the fall the clock is: 0 at the death, 1 at the floor.</summary>
        public static float Progress(float deathClock) => Mathf.Clamp01(deathClock / FallDuration);

        /// <summary>The fall from a pivot pose: down to the floor, pitched forward, rolled to one side (<paramref name="rollSign"/> ±1).</summary>
        public static DeathCam From(Vector3 position, Quaternion rotation, float rollSign)
        {
            Vector3 euler = rotation.eulerAngles;
            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            return new DeathCam
            {
                StartPosition = position, StartRotation = rotation,
                EndPosition = new Vector3(position.x, FloorHeight, position.z),
                EndRotation = Quaternion.Euler(Mathf.Clamp(pitch + ExtraPitch, -80f, 80f), euler.y, rollSign * RollAngle),
            };
        }

        /// <summary>The pivot's pose at progress <paramref name="k"/>: the drop accelerates like gravity, a tiny bounce at
        /// the end (the thud), the roll eases in and out.</summary>
        public static void Pose(in DeathCam c, float k, out Vector3 position, out Quaternion rotation)
        {
            float fall = k * k;
            float thud = k > 0.85f && k < 1f ? Mathf.Sin((k - 0.85f) / 0.15f * Mathf.PI) * 0.05f : 0f;
            position = Vector3.Lerp(c.StartPosition, c.EndPosition, fall) + Vector3.up * thud;
            rotation = Quaternion.Slerp(c.StartRotation, c.EndRotation, Mathf.SmoothStep(0f, 1f, k));
        }
    }

    /// <summary>
    /// The player's death, everywhere it lands outside the flow (8.3; replaces <c>PlayerState.Die</c> and
    /// <c>PlayerDeathSequence.Begin</c>): the view rig is taken away from <see cref="PlayerViewSystem"/> (no look, no
    /// bob, no shake), the gun is holstered, a finisher in progress is ended, the viewmodel is hidden, the thud
    /// plays, the death screen goes up, and the pivot gets its <see cref="DeathCam"/>. The controls themselves are
    /// the flow's: the run is Dying, and <see cref="FlowSystem"/> blocks the input for every phase but Playing.
    /// </summary>
    [Exclusive] // the audio device and the death screen (a GameObject until the UI host lands)
    public sealed class PlayerDeathSystem : SystemBase, IConsumes<DeathEvent>
    {
        /// <summary>Player deaths since start-up — the gates' probe (the die / fall / checkpoint scripts).</summary>
        public static int Deaths;

        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                var p = e.Entity;
                if (!w.IsAlive(p) || !w.Has<PlayerTag>(p)) continue;
                Deaths++;

                if (w.Has<PlayerViewRig>(p))
                {
                    ref var rig = ref w.Get<PlayerViewRig>(p);
                    rig.External = true;
                    if (w.IsAlive(rig.Pivot) && w.Has<ViewBob>(rig.Pivot)) w.Get<ViewBob>(rig.Pivot).Active = false;
                    if (w.IsAlive(rig.Camera) && w.Has<CameraShake>(rig.Camera)) w.Get<CameraShake>(rig.Camera).Clear();
                    if (w.IsAlive(rig.Pivot) && w.Has<LocalTransform>(rig.Pivot))
                    {
                        var lt = w.Get<LocalTransform>(rig.Pivot);
                        ctx.Commands.Add(rig.Pivot, DeathCams.From(lt.Position, lt.Rotation, Random.value < 0.5f ? -1f : 1f));
                    }
                }
                if (w.Has<Weapon>(p))
                {
                    ref var gun = ref w.Get<Weapon>(p);
                    gun.Holstered = true;
                    Hide(ctx, gun.Sprite);
                }
                if (w.Has<AltFire>(p))
                {
                    // no combo or finisher may continue past death (the finisher entity would keep driving the gun)
                    ref var combo = ref w.Get<AltFire>(p);
                    if (!combo.Finisher.IsNull && w.IsAlive(combo.Finisher)) ctx.Commands.DestroyEntity(combo.Finisher);
                    Hide(ctx, combo.LeftFist);
                    Hide(ctx, combo.RightFist);
                    combo = new AltFire { LeftFist = combo.LeftFist, RightFist = combo.RightFist };
                }
                var clip = ProceduralSounds.PlayerDeath;
                if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 1f);
                DeathScreen.Show();
            }
        }

        private static void Hide(SystemContext ctx, Entity e)
        {
            if (!e.IsNull && ctx.World.IsAlive(e) && !ctx.World.Has<Hidden>(e)) ctx.Commands.Add(e, new Hidden());
        }
    }

    /// <summary>Poses the pivot along its <see cref="DeathCam"/> off the flow's death clock; at the floor the camera's
    /// leftover shake is dropped.</summary>
    [UpdateAfter(typeof(FlowSystem))] // this frame's clock: the fall, the slow motion and the death screen read the same tick
    [Reads(typeof(Flow)), Writes(typeof(CameraShake))]
    public sealed class DeathCamSystem : SystemBase, ITick
    {
        private Query _cams;

        public override void OnCreate() => _cams = World.Query().WithReadOnly<DeathCam>().With<LocalTransform>().Build();

        public void Tick(SystemContext ctx)
        {
            float k = DeathCams.Progress(ctx.World.GetSingleton<Flow>().DeathClock);
            foreach (var chunk in _cams)
            {
                var cams = chunk.Get<DeathCam>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    DeathCams.Pose(in cams[i], k, out var pos, out var rot);
                    xforms[i].Position = pos;
                    xforms[i].Rotation = rot;
                }
            }
            if (k >= 1f && ctx.World.IsAlive(PlayerLink.CameraEntity) && ctx.World.Has<CameraShake>(PlayerLink.CameraEntity))
                ctx.World.Get<CameraShake>(PlayerLink.CameraEntity).Clear();
        }
    }
}
