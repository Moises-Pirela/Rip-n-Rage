using System;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>
    /// A floor that moves under its own steam: a kinematic <see cref="Body"/> driven at a velocity rather than
    /// pushed by forces. The solver integrates a kinematic body's pose from its velocity, so everything that asks
    /// the physics world what the ground is doing — the ride spring, <see cref="CharacterMotor.FrameVelocity"/>,
    /// contacts, friction on the crates standing on it — gets the right answer with no special case anywhere.
    ///
    /// <para>This is the deck a vessel will be built on. A boat differs from it only in where the velocity comes
    /// from (buoyancy and a propeller, rather than a number in a file).</para>
    /// </summary>
    [NotInPreview] public struct PlatformMotion : IComponent
    {
        /// <summary>Metres per second, world axes.</summary>
        public Vector3 Velocity;
        /// <summary>Degrees per second about +y: a deck that turns under its riders.</summary>
        public float YawRate;
        /// <summary>Reverse at this distance from where it started (0 = never: straight on for ever).</summary>
        public float PatrolDistance;
        /// <summary>Heave: metres of rise and fall, and how many times a second (0 = still).</summary>
        public float BobAmplitude, BobFrequency;
        // ---- state ----
        public Vector3 Origin;
        public float Phase;
        public int Direction;
        internal bool Started;
    }

    public sealed class PlatformMotionConfig : ComponentConfig<PlatformMotion>
    {
        [InspectTip("metres per second, world axes")]
        public float[] velocity = { 0, 0, 0 };
        [InspectTip("degrees per second about +y")]
        public float yawRate;
        [InspectTip("reverse this far from the start (0 = straight on for ever)")]
        public float patrolDistance;
        [InspectTip("heave: metres of rise and fall, and times a second")]
        public float bobAmplitude, bobFrequency;
        public override PlatformMotion Bake() => new PlatformMotion
        {
            Velocity = PhysicsConfigUtil.Vec(velocity), YawRate = yawRate, PatrolDistance = patrolDistance,
            BobAmplitude = bobAmplitude, BobFrequency = bobFrequency, Direction = 1,
        };
    }

    /// <summary>
    /// Drives every <see cref="PlatformMotion"/> by writing its body's velocity — never its pose. A platform that
    /// teleported would carry nobody: the solver would see a body that jumped, with no velocity to hand to the
    /// riders standing on it. Runs before the physics sync, so the velocity it writes is the one this step uses.
    /// </summary>
    [UpdateBefore(typeof(PhysicsSyncSystem))]
    [Writes(typeof(BodyVelocity))]
    public sealed class PlatformMotionSystem : SystemBase, IFixedTick
    {
        private Query _q;
        public override void OnCreate() =>
            _q = World.Query().With<PlatformMotion>().With<BodyVelocity>().WithReadOnly<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            if (dt <= 0f) return;
            foreach (var chunk in _q)
            {
                var motions = chunk.Get<PlatformMotion>(); var vels = chunk.Get<BodyVelocity>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var pm = ref motions[i];
                    if (!pm.Started) { pm.Origin = xforms[i].Position; pm.Started = true; if (pm.Direction == 0) pm.Direction = 1; }

                    // patrol: turn round at the far end, measured along the run from where it started
                    if (pm.PatrolDistance > 0f && pm.Velocity != Vector3.zero)
                    {
                        Vector3 along = xforms[i].Position - pm.Origin;
                        float travelled = Vector3.Dot(along, pm.Velocity.normalized) * pm.Direction;
                        if (travelled > pm.PatrolDistance) pm.Direction = -pm.Direction;
                    }

                    Vector3 v = pm.Velocity * pm.Direction;
                    if (pm.BobAmplitude > 0f && pm.BobFrequency > 0f)
                    {
                        pm.Phase += dt * pm.BobFrequency * Mathf.PI * 2f;
                        v.y += pm.BobAmplitude * pm.BobFrequency * Mathf.PI * 2f * MathF.Cos(pm.Phase);
                    }
                    vels[i].Linear = v;
                    vels[i].Angular = new Vector3(0f, pm.YawRate * Mathf.Deg2Rad, 0f);
                }
            }
        }
    }

    /// <summary>
    /// "I ride that thing": the entity is held at a fixed pose in its carrier's frame, and moved by this system
    /// every fixed step. The crates lashed to a deck, a trigger volume aboard a ship, the lamp in its hold.
    ///
    /// <para>It exists because a physics entity cannot be a child: <see cref="PhysicsSyncSystem"/> refuses to
    /// register anything with a <see cref="Parent"/> ("physics entities must be roots"), so a collider aboard a
    /// vessel has to be carried rather than parented.</para>
    ///
    /// <para>Anything that is meant to be <em>stood on</em> should be a kinematic body, not a static collider: a
    /// carried body is given its carrier's velocity at its own position, which is what the ride spring and the
    /// riders' <see cref="CharacterMotor.FrameVelocity"/> read. A carried static collider is re-baked where it
    /// lands — right for a trigger or a decoration, wrong for a floor.</para>
    /// </summary>
    public struct CarriedBy : IComponent
    {
        public Entity Carrier;
        /// <summary>Where it sits in the carrier's frame. Captured from the two poses the first time it is carried.</summary>
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public bool Captured;
    }

    /// <summary>Spin a rotation on by an angular velocity (rad/s) for one step.</summary>
    internal static class Spin
    {
        public static Quaternion By(Quaternion q, Vector3 angularVelocity, float dt)
        {
            float speed = angularVelocity.magnitude;
            if (speed < 1e-6f) return q;
            return Quaternion.AngleAxis(speed * dt * Mathf.Rad2Deg, angularVelocity / speed) * q;
        }
    }

    /// <summary>
    /// Poses everything that rides something else, and hands a carried body its carrier's velocity so that whoever
    /// stands on <em>it</em> is carried in turn. Runs before the physics sync so the pose it writes is this step's.
    /// </summary>
    [UpdateBefore(typeof(PhysicsSyncSystem)), UpdateAfter(typeof(PlatformMotionSystem))]
    [Writes(typeof(BodyVelocity))]
    public sealed class CarrySystem : SystemBase, IFixedTick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().With<CarriedBy>().With<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var rides = chunk.Get<CarriedBy>(); var xforms = chunk.Get<LocalTransform>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var ride = ref rides[i];
                    if (ride.Carrier.IsNull || !World.IsAlive(ride.Carrier) || !World.Has<LocalTransform>(ride.Carrier)) continue;
                    var carrier = World.Get<LocalTransform>(ride.Carrier);

                    if (!ride.Captured)
                    {
                        // where it was placed, read into the carrier's frame once
                        var inverse = Quaternion.Inverse(carrier.Rotation);
                        ride.LocalPosition = inverse * (xforms[i].Position - carrier.Position);
                        ride.LocalRotation = inverse * xforms[i].Rotation;
                        ride.Captured = true;
                    }

                    bool carried = World.Has<BodyVelocity>(ents[i]);
                    bool carrierMoves = World.Has<BodyVelocity>(ride.Carrier);

                    // A body of its own is integrated by the solver this step, exactly as its carrier is, so it is
                    // posed where the carrier is *now* and given the carrier's velocity — the two then move
                    // together. Anything else (a trigger, a decoration: a collider with no body) is integrated by
                    // nobody, so it is posed where the carrier will *be* at the end of this step. Otherwise it
                    // trails its deck by a step, which at chase speed is half a metre.
                    Vector3 origin = carrier.Position;
                    Quaternion rotation = carrier.Rotation;
                    if (!carried && carrierMoves)
                    {
                        var cv0 = World.Get<BodyVelocity>(ride.Carrier);
                        origin += cv0.Linear * ctx.DeltaTime;
                        rotation = Spin.By(rotation, cv0.Angular, ctx.DeltaTime);
                    }

                    Vector3 position = origin + rotation * ride.LocalPosition;
                    xforms[i].Position = position;
                    xforms[i].Rotation = rotation * ride.LocalRotation;

                    // a carried body carries in turn: its velocity is the carrier's, at its own position
                    if (carried && carrierMoves)
                    {
                        var cv = World.Get<BodyVelocity>(ride.Carrier);
                        ref var v = ref World.Get<BodyVelocity>(ents[i]);
                        v.Linear = cv.Linear + Vector3.Cross(cv.Angular, position - carrier.Position);
                        v.Angular = cv.Angular;
                    }
                }
            }
        }
    }
}
