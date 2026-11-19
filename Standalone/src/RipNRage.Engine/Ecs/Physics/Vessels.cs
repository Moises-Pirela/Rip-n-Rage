using System;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>
    /// The helm: what the pilot is asking for, and the only way anything drives a hull. A pilot AI writes it, a
    /// script writes it, a player at a wheel would write it — nothing else moves a boat.
    /// </summary>
    public struct VesselControl : IComponent
    {
        /// <summary>-1 (astern) to 1 (full ahead).</summary>
        public float Throttle;
        /// <summary>-1 (hard a-port) to 1 (hard a-starboard).</summary>
        public float Steer;
    }

    public sealed class VesselControlConfig : ComponentConfig<VesselControl>
    {
        public float throttle, steer;
        public override VesselControl Bake() => new VesselControl { Throttle = throttle, Steer = steer };
    }

    /// <summary>
    /// An outboard: thrust from a propeller at the stern, pointed by the helm. Because the push is applied at the
    /// stern and off the centreline when the helm is over, the hull turns for the reason a real one does — it is
    /// pushed round, not rotated by fiat — and it heels into the turn on its own.
    ///
    /// <para>A propeller only bites while it is in the water. Launch the hull off a wave and the drive cuts until
    /// it lands, which is the behaviour the chase is built on.</para>
    /// </summary>
    public struct VesselEngine : IComponent
    {
        /// <summary>Newtons at full throttle.</summary>
        public float MaxThrust;
        /// <summary>Where the propeller is, in the hull's own frame (astern, below the waterline).</summary>
        public Vector3 SternOffset;
        /// <summary>How far the drive swings at full helm (degrees).</summary>
        public float SteerAngle;
        /// <summary>Fraction of thrust available astern.</summary>
        public float ReverseScale;
        /// <summary>How fast the throttle and helm actually follow what is asked of them (per second).</summary>
        public float ThrottleResponse, SteerResponse;
        // ---- state ----
        public float Throttle, Steer;
        /// <summary>Is the propeller in the water? (The VFX and the pilot both want to know.)</summary>
        public bool Biting;
    }

    public sealed class VesselEngineConfig : ComponentConfig<VesselEngine>
    {
        [InspectTip("newtons at full throttle")]
        public float maxThrust = 20000f;
        [InspectTip("the propeller, in the hull's frame: astern and below the waterline")]
        public float[] sternOffset = { -2.5f, -0.5f, 0f };
        [InspectRange(0, 60, 1), InspectTip("how far the drive swings at full helm")]
        public float steerAngle = 28f;
        [InspectRange(0, 1, 0.05f)]
        public float reverseScale = 0.35f;
        [InspectTip("how fast the throttle and helm follow the pilot")]
        public float throttleResponse = 1.5f, steerResponse = 3f;
        public override VesselEngine Bake() => new VesselEngine
        {
            MaxThrust = maxThrust, SternOffset = PhysicsConfigUtil.Vec(sternOffset), SteerAngle = steerAngle,
            ReverseScale = reverseScale, ThrottleResponse = throttleResponse, SteerResponse = steerResponse,
        };
    }

    /// <summary>
    /// Drives every hull that has an engine: the throttle and helm chase what the pilot asked for, and the
    /// propeller pushes — if it is under water. Runs with the buoyancy, before the solver steps.
    /// </summary>
    [UpdateAfter(typeof(BuoyancySystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    [Exclusive] // adds forces straight onto solver bodies
    public sealed class VesselEngineSystem : SystemBase, IFixedTick
    {
        private Query _q;
        private PhysicsSyncSystem _sync;
        public override void OnCreate() =>
            _q = World.Query().With<VesselEngine>().WithReadOnly<VesselControl>().WithReadOnly<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            if (_sync == null) return;
            float dt = ctx.DeltaTime;
            if (dt <= 0f) return;

            foreach (var chunk in _q)
            {
                var engines = chunk.Get<VesselEngine>(); var controls = chunk.Get<VesselControl>(); var xforms = chunk.Get<LocalTransform>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    var body = _sync.BodyOf(ents[i]);
                    if (body == null || body.isKinematic) continue;
                    ref var en = ref engines[i];

                    // the controls follow the pilot rather than snapping: an engine takes a moment to answer
                    en.Throttle = Mathf.MoveTowards(en.Throttle, Mathf.Clamp(controls[i].Throttle, -1f, 1f), en.ThrottleResponse * dt);
                    en.Steer = Mathf.MoveTowards(en.Steer, Mathf.Clamp(controls[i].Steer, -1f, 1f), en.SteerResponse * dt);

                    // the propeller sits on the hull, in the hull's frame: from the entity's origin, through its scale
                    Vector3 origin = body.position - body.rotation * body.comOffsetLocal;
                    Vector3 prop = origin + body.rotation * Vector3.Scale(en.SternOffset, xforms[i].Scale);
                    en.Biting = WaterSurface.Exists && WaterSurface.DepthAt(prop) > 0f;
                    if (!en.Biting || Mathf.Abs(en.Throttle) < 1e-3f) continue;

                    // the drive swings with the helm, so the push comes off the centreline and the hull is turned
                    // by being pushed round
                    var swing = Quaternion.AngleAxis(-en.Steer * en.SteerAngle, Vector3.up);
                    Vector3 push = body.rotation * (swing * Vector3.forward);
                    float thrust = en.MaxThrust * en.Throttle * (en.Throttle < 0f ? en.ReverseScale : 1f);
                    body.AddForceAtPosition(push * thrust, prop);
                }
            }
        }
    }
}
