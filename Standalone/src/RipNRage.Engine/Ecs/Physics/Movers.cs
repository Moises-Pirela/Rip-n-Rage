using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Physics
{
    public enum CharacterProfileKind { Custom, Player, Enemy }

    /// <summary>
    /// A force-driven character: the entity's <see cref="Body"/> + capsule <see cref="ColliderShape"/> floats
    /// <see cref="RideHeight"/> above the ground on a damped spring (a downward sphere cast finds the ground) and is
    /// steered by accelerations towards <see cref="Desired"/>. Everything is a force on the body, so the character
    /// pushes crates with its mass, is shoved by blasts and slides along walls through the contact solver.
    ///
    /// Gameplay writes intent (<see cref="Desired"/>, the <c>Pending*</c> commands) and reads state
    /// (<see cref="IsGrounded"/> ...); the <see cref="CharacterMotorSystem"/> consumes intent each fixed step.
    /// Profile kinds Player / Enemy track the tunables.cfg [character] / [enemy_character] values live.
    /// </summary>
    [NotInPreview] public struct CharacterMotor : IComponent
    {
        public CharacterProfileKind Profile;
        // ---- ride + motor tunables (overwritten from the profile unless Custom) ----
        public float RideHeight, RideFrequency, RideDamping, MaxRideAcceleration, GroundTolerance, MaxSlopeAngle;
        public float Acceleration, AirAcceleration, StopDeceleration, JumpGrace;
        /// <summary>
        /// Press back on what we stand on (Newton's third law). The ride spring and the motor push this character
        /// about; the same forces, reversed, go into whatever is carrying it. A deck dips under boarders, a dinghy
        /// is shoved by someone running across it, and a crate is pressed down rather than merely stood upon.
        /// Off for nothing: the ground is usually the world, which does not care.
        /// </summary>
        public bool PressesOnGround;
        /// <summary>Collider layers that count as ground.</summary>
        public int GroundMask;
        // ---- intent ----
        /// <summary>Wanted horizontal velocity (y ignored); hold it every frame. Zero = brake.</summary>
        public Vector3 Desired;
        /// <summary>Jump: upward speed added on top of max(vy, 0) (consumed).</summary>
        public float PendingJump;
        /// <summary>Velocity change (knockback, rocket jumps; consumed).</summary>
        public Vector3 PendingImpulse;
        /// <summary>Dash at this velocity for <see cref="PendingDashTime"/> seconds, gravity off (consumed when the time is &gt; 0).</summary>
        public Vector3 PendingDash; public float PendingDashTime;
        public bool PendingStopDash;
        /// <summary>Floor on the vertical speed this step (wall-run glide); NaN = none. Consumed.</summary>
        public float PendingMinVerticalSpeed;
        /// <summary>New capsule height (slide crouch / stand up), the bottom stays put; NaN = none. Consumed.</summary>
        public float PendingHeight;
        /// <summary>
        /// Gravity multiplier for the body, re-applied every step (a wall run glides at a fraction, the player falls
        /// at 2.5×). Unlike the <c>Pending*</c> commands this is a level, not an edge: hold the value. A dash sets
        /// gravity aside and restores this when it ends.
        /// </summary>
        public float GravityScale;
        // ---- state (written by the system) ----
        public bool IsGrounded, IsDashing;
        public Vector3 GroundNormal, GroundVelocity;
        /// <summary>The ground body's angular velocity (rad/s) — what a turning deck does to whoever stands on it.</summary>
        public Vector3 GroundAngularVelocity;
        public Entity Ground;
        /// <summary>
        /// The velocity of the frame this character moves in — every speed it is given (<see cref="Desired"/>, a
        /// dash, a jump's rise) is relative to this, not to the world.
        ///
        /// <para>Standing, it is the ground's velocity at the contact. In the air it is <b>frozen at the value it
        /// had on leaving the surface</b>: the deck you jumped from is an inertial frame, and you keep its velocity
        /// until something else acts on you — so a standing jump on a boat doing 25 m/s lands where it started.
        /// While wall running, gameplay writes the wall's velocity here (the motor only overwrites it while
        /// grounded). On ground that does not move it is zero, and every number behaves exactly as it always has.</para>
        /// </summary>
        public Vector3 FrameVelocity;
        /// <summary>The frame's yaw rate in degrees per second while grounded (0 in the air): a turning deck carries
        /// the rider's aim round with it.</summary>
        public float FrameYawRate;
        /// <summary>Is the frame actually moving? (A deck under way, as opposed to solid ground.)</summary>
        public bool OnMovingFrame => FrameVelocity.sqrMagnitude > 0.01f;
        /// <summary>A world velocity as this character experiences it — what the deck sees, not what the sea sees.</summary>
        public Vector3 Relative(Vector3 worldVelocity) => worldVelocity - FrameVelocity;
        /// <summary>From the capsule bottom to the ground (infinity = none in reach).</summary>
        public float GroundDistance;
        /// <summary>Where the ground probe struck: where this character's weight is actually carried.</summary>
        public Vector3 GroundPoint;
        // ---- internal timers (sim time) ----
        internal float UngroundedUntil, DashUntil;
        internal Vector3 DashVelocity;
        internal bool Configured;
    }

    public sealed class CharacterMotorConfig : ComponentConfig<CharacterMotor>
    {
        [InspectTip("Player / Enemy track tunables.cfg live; Custom uses the fields here")]
        public CharacterProfileKind profile = CharacterProfileKind.Custom;
        [InspectTip("the spring the capsule rides on: height above the ground, its frequency and damping, how hard it may push, how far below it still counts as ground, the steepest walkable slope")]
        public float rideHeight = 0.5f, rideFrequency = 6f, rideDamping = 0.8f, maxRideAcceleration = 90f, groundTolerance = 0.25f, maxSlopeAngle = 50f;
        [InspectTip("m/s² on the ground and in the air, braking, seconds a jump is still honoured after leaving the ground")]
        public float acceleration = 70f, airAcceleration = 20f, stopDeceleration = 60f, jumpGrace = 0.25f;
        [InspectTip("press back on what you stand on: a deck dips under boarders, a dinghy is shoved by a run across it")]
        public bool pressesOnGround = true;
        public int groundMask = ~0;
        public float gravityScale = 1f;
        public override CharacterMotor Bake() => new CharacterMotor
        {
            GravityScale = gravityScale,
            Profile = profile, RideHeight = rideHeight, RideFrequency = rideFrequency, RideDamping = rideDamping, MaxRideAcceleration = maxRideAcceleration,
            GroundTolerance = groundTolerance, MaxSlopeAngle = maxSlopeAngle, Acceleration = acceleration, AirAcceleration = airAcceleration,
            StopDeceleration = stopDeceleration, JumpGrace = jumpGrace, GroundMask = groundMask, PressesOnGround = pressesOnGround,
            PendingMinVerticalSpeed = float.NaN, PendingHeight = float.NaN, GroundNormal = Vector3.up, GroundDistance = float.PositiveInfinity,
        };
    }

    /// <summary>Steps every <see cref="CharacterMotor"/>: ground probe, dash, ride spring, motor (fixed step, before the solver).</summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    [Exclusive] // owns state outside components: the physics world
    public sealed class CharacterMotorSystem : SystemBase, IFixedTick, IOnEntityDestroyed, IOnSimEnd
    {
        private Query _q;
        private PhysicsSyncSystem _sync;
        private float _time;
        /// <summary>Each character's ride: one constraint per character, re-placed every step on whatever it stands
        /// on, and solved with the contacts (see <see cref="RideConstraint"/>).</summary>
        private readonly Dictionary<Entity, RideConstraint> _rides = new Dictionary<Entity, RideConstraint>();

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            if (_rides.TryGetValue(e, out var ride)) { PhysicsApi.World.RemoveConstraint(ride); _rides.Remove(e); }
        }

        public void OnSimEnd(SystemContext ctx)
        {
            foreach (var ride in _rides.Values) PhysicsApi.World.RemoveConstraint(ride);
            _rides.Clear();
        }

        private RideConstraint RideOf(Entity e)
        {
            if (!_rides.TryGetValue(e, out var ride)) _rides[e] = ride = new RideConstraint { enabled = false };
            return ride;
        }
        public override void OnCreate() => _q = World.Query().With<CharacterMotor>().WithReadOnly<PhysicsHandle>().Build();

        public void FixedTick(SystemContext ctx)
        {
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            if (_sync == null) return;
            float dt = ctx.DeltaTime;
            _time += dt;
            foreach (var chunk in _q)
            {
                var motors = chunk.Get<CharacterMotor>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    var body = _sync.BodyOf(ents[i]);
                    if (body == null || body.collider == null) continue;
                    if (!float.IsNaN(motors[i].PendingHeight)) { SetHeight(ents[i], ref motors[i], body, motors[i].PendingHeight); motors[i].PendingHeight = float.NaN; }
                    Step(ents[i], ref motors[i], body, dt);
                }
            }
        }

        /// <summary>
        /// Resizes the capsule keeping its bottom where it is (the slide crouch shrinks from the top): the collider,
        /// the body's mass properties and centre of mass follow; the entity origin does not move.
        /// </summary>
        private void SetHeight(Entity e, ref CharacterMotor m, SolverBody b, float newHeight)
        {
            if (!World.Has<ColliderShape>(e) || !World.Has<Body>(e)) return;
            ref var cs = ref World.Get<ColliderShape>(e);
            if (cs.Kind != ShapeKind.Capsule) return;
            float bottom = cs.Center.y - cs.Height * 0.5f;
            cs.Height = MathF.Max(newHeight, cs.Radius * 2f);
            cs.Center = new Vector3(cs.Center.x, bottom + cs.Height * 0.5f, cs.Center.z);
            b.collider.ecsShape = cs;
            Vector3 v = b.linearVelocity, w = b.angularVelocity;
            Vector3 scale = World.Has<LocalTransform>(e) ? World.Get<LocalTransform>(e).Scale : Vector3.one;
            b.SetupEcs(World.Get<Body>(e), cs, scale); // recomputes the capsule and the centre-of-mass offset
            b.ReadTransform();                         // the true origin is unchanged; position (centre of mass) follows
            b.linearVelocity = v; b.angularVelocity = w;
            m.Configured = false;                      // SetupEcs restored the Body defaults: re-apply the character settings
            PhysicsSyncSystem.UpdateWorldShape(b.collider, b);
        }

        /// <summary>A character never tumbles, sleeps, scrapes or tunnels (applied once per body).</summary>
        private static void Configure(ref CharacterMotor m, SolverBody b)
        {
            b.freezeRotation = true; b.canSleep = false; b.friction = 0.05f; b.staticFriction = 0.05f; b.restitution = 0f;
            b.collisionDetection = CollisionDetectionMode.Continuous; b.linearDamping = 0f; b.angularDamping = 0f; b.interpolate = true;
            b.ComputeMassProperties();
            m.Configured = true;
        }

        private static void ApplyProfile(ref CharacterMotor m)
        {
            var p = m.Profile == CharacterProfileKind.Player ? CharacterProfiles.Player : m.Profile == CharacterProfileKind.Enemy ? CharacterProfiles.Enemy : null;
            if (p == null) return;
            m.RideHeight = p.rideHeight; m.RideFrequency = p.rideFrequency; m.RideDamping = p.rideDamping; m.MaxRideAcceleration = p.maxRideAcceleration;
            m.GroundTolerance = p.groundTolerance; m.MaxSlopeAngle = p.maxSlopeAngle; m.Acceleration = p.acceleration; m.AirAcceleration = p.airAcceleration;
            m.StopDeceleration = p.stopDeceleration; m.JumpGrace = p.jumpGrace;
        }

        /// <summary>The horizontal part of a velocity.</summary>
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void Unground(ref CharacterMotor m, float seconds) { m.UngroundedUntil = MathF.Max(m.UngroundedUntil, _time + seconds); m.IsGrounded = false; }

        private void StopDash(ref CharacterMotor m, SolverBody b)
        {
            if (!m.IsDashing) return;
            m.IsDashing = false;
            b.gravityScale = m.GravityScale;
        }

        internal void Step(Entity e, ref CharacterMotor m, SolverBody b, float dt)
        {
            if (!m.Configured) Configure(ref m, b);
            ApplyProfile(ref m);
            b.WakeUp();
            if (!m.IsDashing) b.gravityScale = m.GravityScale; // a level, re-applied every step (the dash owns it meanwhile)

            // ---- commands ----
            Vector3 v = b.linearVelocity;
            if (m.PendingStopDash) { StopDash(ref m, b); m.PendingStopDash = false; }
            if (m.PendingDashTime > 0f)
            {
                // the dash is a speed relative to what we stand on: on a deck doing 25 m/s it carries the deck
                m.IsDashing = true; m.DashVelocity = m.PendingDash + Flat(m.FrameVelocity); m.DashUntil = _time + m.PendingDashTime;
                b.gravityScale = 0f; v = m.DashVelocity;
                Unground(ref m, m.PendingDashTime + 0.05f);
                m.PendingDashTime = 0f;
            }
            if (m.PendingImpulse != Vector3.zero)
            {
                v += m.PendingImpulse;
                if (m.PendingImpulse.y > 0.5f) Unground(ref m, m.JumpGrace);
                m.PendingImpulse = Vector3.zero;
            }
            if (m.PendingJump > 0f)
            {
                v.y = MathF.Max(v.y, m.FrameVelocity.y) + m.PendingJump; // a heaving deck throws its own rise in
                Unground(ref m, m.JumpGrace);
                m.PendingJump = 0f;
            }
            if (!float.IsNaN(m.PendingMinVerticalSpeed)) { if (v.y < m.PendingMinVerticalSpeed) v.y = m.PendingMinVerticalSpeed; m.PendingMinVerticalSpeed = float.NaN; }

            ProbeGround(ref m, b);
            var ride = RideOf(e);
            ride.enabled = false; // until the ride block below finds there is something to stand on

            // ---- the frame we move in: the ground while we stand on it, frozen while we are off it ----
            // Wall running writes its own frame (the wall's velocity) from gameplay; only the ground overwrites it.
            if (m.IsGrounded)
            {
                m.FrameVelocity = m.GroundVelocity;
                m.FrameYawRate = m.GroundAngularVelocity.y * Mathf.Rad2Deg;
            }
            else m.FrameYawRate = 0f;

            // ---- dash: exact velocity, no gravity, no spring ----
            if (m.IsDashing)
            {
                // hand back a fast but normal run *in the frame*, not 60% of the deck's own speed
                if (_time >= m.DashUntil) { StopDash(ref m, b); Vector3 f = Flat(m.FrameVelocity); v = f + Flat(m.DashVelocity - f) * 0.6f; }
                else { b.linearVelocity = m.DashVelocity; return; }
            }

            // ---- the ride: hover at the ride height ----
            // Holding the character up is the ground's job, and the ground is a body in the solver: the support is
            // a constraint solved there with the contacts (RideConstraint), so a crate under a character is held up
            // by the floor under the crate, and whatever carries the character carries its weight - exactly, not on
            // average. What stays here is the one thing the ground does not do: a character lifted over the ride
            // height (cresting a bump, stepping down) is held to the ground rather than floating off it. That pull is
            // the motor's, not a contact force, so nothing pushes back for it.
            if (m.IsGrounded && _time >= m.UngroundedUntil)
            {
                float error = m.RideHeight - m.GroundDistance;
                float omega = 2f * MathF.PI * MathF.Max(m.RideFrequency, 0.1f);
                float relVel = v.y - m.GroundVelocity.y;
                float spring = error * omega * omega - relVel * 2f * m.RideDamping * omega;
                if (spring < 0f) v.y += MathF.Max(spring, -m.MaxRideAcceleration) * dt; // stick: the spring would pull

                var ground = _sync?.BodyOf(m.Ground);
                ride.Place(b, ground, m.GroundPoint, Vector3.up, m.GroundDistance, m.RideHeight);
                ride.stiffness = b.mass * omega * omega;            // the rider's own spring: the sag is g/ω² on any ground
                ride.damping = 2f * m.RideDamping * b.mass * omega;
                ride.maxForce = m.MaxRideAcceleration * b.mass;
                ride.enabled = true;
                if (!ride.registered) PhysicsApi.World.AddConstraint(ride);
            }
            else ride.ClearImpulses();

            // ---- motor: the horizontal velocity, towards the request, in the frame we move in ----
            Vector3 request = new Vector3(m.Desired.x, 0f, m.Desired.z);
            if (ride.enabled)
            {
                // On the ground the feet are the ride's: it drives the velocity relative to the ground point towards
                // the request, solved with the contacts (RideConstraint.SolveTraction) - braking judged on what was
                // asked for, as it always was, so letting go on a moving deck stops you on the deck.
                ride.traction = true;
                ride.targetVelocity = request;
                ride.maxTractionForce = (request.sqrMagnitude > 0.01f ? m.Acceleration : m.StopDeceleration) * b.mass;
            }
            else
            {
                // In the air there is nothing to push against, so air control is the motor's own: it steers relative
                // to the deck we jumped from instead of braking towards a standstill the sea is not in. On still
                // ground the frame is zero and this is the line it always was.
                ride.traction = false;
                Vector3 horizontal = new Vector3(v.x, 0f, v.z);
                Vector3 target = request + Flat(m.FrameVelocity);
                Vector3 delta = Vector3.ClampMagnitude(target - horizontal, m.AirAcceleration * dt);
                v.x += delta.x; v.z += delta.z;
            }
            b.linearVelocity = v;
            b.angularVelocity = Vector3.zero;
        }

        private void ProbeGround(ref CharacterMotor m, SolverBody b)
        {
            var shape = b.WorldShape;
            float r = shape.type == ShapeType.Sphere || shape.type == ShapeType.Capsule ? shape.radius : MathF.Min(b.boxHalfExtents.x, b.boxHalfExtents.z);
            float half = shape.type == ShapeType.Capsule ? shape.halfHeight : shape.type == ShapeType.Box ? MathF.Max(0f, shape.halfExtents.y - r) : 0f;
            Vector3 bottomCentre = shape.center - (shape.rotation * Vector3.up) * half;
            var probe = CollisionShape.Sphere(bottomCentre, r * 0.95f);
            float reach = m.RideHeight + m.GroundTolerance + 0.05f;
            var filter = QueryFilter.For(ColliderTable.ProxyOf(b.collider), b.collider.layer, m.GroundMask);
            filter.useLayerMatrix = false; // ground is decided by the mask alone
            float cosSlope = MathF.Cos(Mathf.Clamp(m.MaxSlopeAngle, 1f, 89f) * Mathf.Deg2Rad);
            m.GroundVelocity = Vector3.zero; m.GroundAngularVelocity = Vector3.zero;
            if (PhysicsApi.ShapeCast(in probe, Vector3.down, filter, out RaycastHit hit, reach))
            {
                m.GroundDistance = MathF.Max(0f, hit.distance - r * 0.05f); // the probe is slightly smaller than the capsule
                m.GroundNormal = hit.normal;
                m.GroundPoint = hit.point;
                m.Ground = hit.entity;
                m.IsGrounded = hit.normal.y >= cosSlope && m.GroundDistance <= m.RideHeight + m.GroundTolerance && _time >= m.UngroundedUntil;
                var gb = PhysicsApi.World.Table.BodyOf(hit.proxy);
                if (gb != null) { m.GroundVelocity = gb.GetPointVelocity(hit.point); m.GroundAngularVelocity = gb.angularVelocity; }
            }
            else
            {
                m.GroundDistance = float.PositiveInfinity; m.GroundNormal = Vector3.up; m.Ground = Entity.Null; m.IsGrounded = false;
            }
        }
    }

    // ==================================================================== kinematic movers

    public enum MotionMode
    {
        /// <summary>Slides along what it hits (characters, sliding debris).</summary>
        Slide,
        /// <summary>Reflects off what it hits (Bounciness, Friction), settles below RestSpeed.</summary>
        Bounce,
        /// <summary>Stops at the first contact and stays stopped (projectiles).</summary>
        Stop,
    }

    /// <summary>
    /// A swept kinematic mover: integrates <see cref="Velocity"/> (gravity, drag, terminal speed) and moves the entity
    /// with continuous move-and-slide through the world every fixed step — it cannot tunnel at any speed. Pushes rigid
    /// bodies it runs into (<see cref="PushMass"/>). With <see cref="ReportCollisions"/> its contacts are posted as
    /// <see cref="CollisionEvent"/>s (Stop mode: the first one only). The shape is <see cref="HalfExtents"/> (an
    /// axis-aligned box around the entity + <see cref="Center"/>) when set, else the entity's own
    /// <see cref="ColliderShape"/> (sphere / capsule / box), which also makes it visible to triggers and queries.
    /// Movers are roots and must not also have a <see cref="Body"/>.
    /// </summary>
    [NotInPreview] public struct KinematicMover : IComponent
    {
        public Vector3 Velocity;
        public bool UseGravity; public float GravityScale, Drag, TerminalSpeed;
        public MotionMode Mode; public float Bounciness, Friction, RestSpeed;
        public int CollisionMask; public bool UseLayerMatrix, CollideWithTriggers;
        /// <summary>Never collide with this entity (the shooter).</summary>
        public Entity Ignore;
        public Vector3 HalfExtents, Center;
        public float PushMass;
        public float FloorMaxAngle, StepHeight, SnapLength, Skin; public int MaxSlides;
        // ---- state ----
        public bool Stopped, IsGrounded, HitWall, HitCeiling;
        public Vector3 GroundNormal;
        public Entity Ground;
    }

    public sealed class KinematicMoverConfig : ComponentConfig<KinematicMover>
    {
        public float[] velocity = { 0, 0, 0 };
        public bool useGravity = true; public float gravityScale = 1f, drag, terminalSpeed = 60f;
        [InspectTip("what a hit does: Slide along, Bounce off, or Stop; the bounce's energy, friction and the speed below which it rests")]
        public MotionMode mode = MotionMode.Slide; public float bounciness = 0.4f, friction = 0.2f, restSpeed = 0.6f;
        public int collisionMask = ~0; public bool useLayerMatrix = true, collideWithTriggers;
        public float[] halfExtents = { 0, 0, 0 }, center = { 0, 0, 0 };
        [InspectTip("how heavy it feels to bodies it pushes; the steepest floor; a step it climbs; how far it snaps down to stay grounded; the skin gap")]
        public float pushMass = 10f, floorMaxAngle = 46f, stepHeight, snapLength, skin; public int maxSlides = 4;
        public override KinematicMover Bake() => new KinematicMover
        {
            Velocity = PhysicsConfigUtil.Vec(velocity), UseGravity = useGravity, GravityScale = gravityScale, Drag = drag, TerminalSpeed = terminalSpeed,
            Mode = mode, Bounciness = bounciness, Friction = friction, RestSpeed = restSpeed,
            CollisionMask = collisionMask, UseLayerMatrix = useLayerMatrix, CollideWithTriggers = collideWithTriggers,
            HalfExtents = PhysicsConfigUtil.Vec(halfExtents), Center = PhysicsConfigUtil.Vec(center), PushMass = pushMass,
            FloorMaxAngle = floorMaxAngle, StepHeight = stepHeight, SnapLength = snapLength, Skin = skin, MaxSlides = maxSlides, GroundNormal = Vector3.up,
        };
    }

    /// <summary>Moves every <see cref="KinematicMover"/> (fixed step, before the rigid solver so bodies see where it went).</summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    [Exclusive] // owns state outside components: the physics world
    public sealed class KinematicMoverSystem : SystemBase, IFixedTick
    {
        private Query _q;
        private PhysicsSyncSystem _sync;
        private readonly MoveResult _result = new MoveResult();
        private readonly List<Entity> _scratch = new List<Entity>();

        public override void OnCreate() => _q = World.Query().With<KinematicMover>().With<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            float dt = ctx.DeltaTime;
            if (dt <= 0f) return;
            _scratch.Clear();
            foreach (var chunk in _q) { var ents = chunk.Entities; for (int i = 0; i < chunk.Length; i++) if (chunk.Includes(i)) _scratch.Add(ents[i]); }
            foreach (var e in _scratch)
            {
                ref var m = ref World.Get<KinematicMover>(e);
                if (m.Stopped && m.Mode == MotionMode.Stop) continue;
                ref var lt = ref World.Get<LocalTransform>(e);
                var slot = _sync?.SlotOf(e);

                if (m.UseGravity) m.Velocity += PhysicsApi.gravity * (m.GravityScale * dt);
                if (m.Drag > 0f) m.Velocity *= MathF.Max(0f, 1f - m.Drag * dt);
                if (m.TerminalSpeed > 0f && m.Velocity.y < -m.TerminalSpeed) m.Velocity.y = -m.TerminalSpeed;

                CollisionShape shape = m.HalfExtents.sqrMagnitude > 0f || slot == null
                    ? CollisionShape.AxisAlignedBox(lt.Position + m.Center, m.HalfExtents.sqrMagnitude > 0f ? m.HalfExtents : new Vector3(0.25f, 0.25f, 0.25f))
                    : slot.worldShape;
                Vector3 offset = shape.center - lt.Position;
                var settings = new MoveSettings
                {
                    filter = new QueryFilter
                    {
                        layerMask = m.CollisionMask, self = ColliderTable.ProxyOf(slot), ignore = m.Ignore, includeTriggers = m.CollideWithTriggers,
                        useLayerMatrix = m.UseLayerMatrix && slot != null, selfLayer = slot != null ? slot.layer : 0,
                    },
                    skin = m.Skin, maxSlides = m.MaxSlides, floorMaxAngle = m.FloorMaxAngle, stepHeight = m.StepHeight, snapLength = m.SnapLength,
                    minMove = 1E-05f, wasGrounded = m.IsGrounded,
                };
                PhysicsApi.MoveAndSlide(in shape, m.Velocity * dt, settings, _result);
                lt.Position = _result.position - offset;
                _sync?.Rebake(e);
                PhysicsApi.PushRigidBodies(_result, _result.motion / dt, m.PushMass);

                switch (m.Mode)
                {
                    case MotionMode.Slide: m.Velocity = _result.ClipVelocity(m.Velocity); break;
                    case MotionMode.Bounce:
                        if (_result.contacts.Count > 0)
                        {
                            Vector3 n = _result.contacts[0].normal;
                            float into = Vector3.Dot(m.Velocity, n);
                            if (into < 0f) { Vector3 np = n * into; m.Velocity = (m.Velocity - np) * (1f - m.Friction) - np * m.Bounciness; }
                            if (_result.isGrounded && m.Velocity.magnitude < m.RestSpeed) m.Velocity = Vector3.zero;
                        }
                        break;
                    case MotionMode.Stop:
                        if (_result.contacts.Count > 0) { m.Velocity = Vector3.zero; m.Stopped = true; }
                        break;
                }
                m.IsGrounded = _result.isGrounded; m.GroundNormal = _result.groundNormal; m.Ground = _result.ground;
                m.HitWall = _result.hitWall; m.HitCeiling = _result.hitCeiling;

                if (_result.contacts.Count > 0 && World.Has<ReportCollisions>(e))
                    for (int c = 0; c < _result.contacts.Count; c++)
                    {
                        var ct = _result.contacts[c];
                        ctx.Post(new CollisionEvent { Self = e, Other = ct.entity, Point = ct.point, Normal = ct.normal });
                        if (m.Mode == MotionMode.Stop) break;
                    }
            }
        }
    }
}
