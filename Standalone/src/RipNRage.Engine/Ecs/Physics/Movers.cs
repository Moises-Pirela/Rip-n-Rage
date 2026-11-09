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
    public struct CharacterMotor : IComponent
    {
        public CharacterProfileKind Profile;
        // ---- ride + motor tunables (overwritten from the profile unless Custom) ----
        public float RideHeight, RideFrequency, RideDamping, MaxRideAcceleration, GroundTolerance, MaxSlopeAngle;
        public float Acceleration, AirAcceleration, StopDeceleration, JumpGrace;
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
        // ---- state (written by the system) ----
        public bool IsGrounded, IsDashing;
        public Vector3 GroundNormal, GroundVelocity;
        public Entity Ground;
        /// <summary>From the capsule bottom to the ground (infinity = none in reach).</summary>
        public float GroundDistance;
        // ---- internal timers (sim time) ----
        internal float UngroundedUntil, DashUntil, SavedGravityScale;
        internal Vector3 DashVelocity;
        internal bool Configured;
    }

    public sealed class CharacterMotorConfig : ComponentConfig<CharacterMotor>
    {
        public CharacterProfileKind profile = CharacterProfileKind.Custom;
        public float rideHeight = 0.5f, rideFrequency = 6f, rideDamping = 0.8f, maxRideAcceleration = 90f, groundTolerance = 0.25f, maxSlopeAngle = 50f;
        public float acceleration = 70f, airAcceleration = 20f, stopDeceleration = 60f, jumpGrace = 0.25f;
        public int groundMask = ~0;
        public override CharacterMotor Bake() => new CharacterMotor
        {
            Profile = profile, RideHeight = rideHeight, RideFrequency = rideFrequency, RideDamping = rideDamping, MaxRideAcceleration = maxRideAcceleration,
            GroundTolerance = groundTolerance, MaxSlopeAngle = maxSlopeAngle, Acceleration = acceleration, AirAcceleration = airAcceleration,
            StopDeceleration = stopDeceleration, JumpGrace = jumpGrace, GroundMask = groundMask,
            PendingMinVerticalSpeed = float.NaN, GroundNormal = Vector3.up, GroundDistance = float.PositiveInfinity, SavedGravityScale = 1f,
        };
    }

    /// <summary>Steps every <see cref="CharacterMotor"/>: ground probe, dash, ride spring, motor (fixed step, before the solver).</summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    public sealed class CharacterMotorSystem : SystemBase, IFixedTick
    {
        private Query _q;
        private PhysicsSyncSystem _sync;
        private float _time;
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
                    Step(ref motors[i], body, dt);
                }
            }
        }

        /// <summary>A character never tumbles, sleeps, scrapes or tunnels (applied once per body).</summary>
        private static void Configure(ref CharacterMotor m, SolverBody b)
        {
            b.freezeRotation = true; b.canSleep = false; b.friction = 0.05f; b.staticFriction = 0.05f; b.restitution = 0f;
            b.collisionDetection = CollisionDetectionMode.Continuous; b.linearDamping = 0f; b.angularDamping = 0f;
            b.ComputeMassProperties();
            m.SavedGravityScale = b.gravityScale;
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

        private void Unground(ref CharacterMotor m, float seconds) { m.UngroundedUntil = MathF.Max(m.UngroundedUntil, _time + seconds); m.IsGrounded = false; }

        private void StopDash(ref CharacterMotor m, SolverBody b)
        {
            if (!m.IsDashing) return;
            m.IsDashing = false;
            b.gravityScale = m.SavedGravityScale;
        }

        internal void Step(ref CharacterMotor m, SolverBody b, float dt)
        {
            if (!m.Configured) Configure(ref m, b);
            ApplyProfile(ref m);
            b.WakeUp();

            // ---- commands ----
            Vector3 v = b.linearVelocity;
            if (m.PendingStopDash) { StopDash(ref m, b); m.PendingStopDash = false; }
            if (m.PendingDashTime > 0f)
            {
                if (!m.IsDashing) m.SavedGravityScale = b.gravityScale;
                m.IsDashing = true; m.DashVelocity = m.PendingDash; m.DashUntil = _time + m.PendingDashTime;
                b.gravityScale = 0f; v = m.PendingDash;
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
                v.y = MathF.Max(v.y, 0f) + m.PendingJump;
                Unground(ref m, m.JumpGrace);
                m.PendingJump = 0f;
            }
            if (!float.IsNaN(m.PendingMinVerticalSpeed)) { if (v.y < m.PendingMinVerticalSpeed) v.y = m.PendingMinVerticalSpeed; m.PendingMinVerticalSpeed = float.NaN; }

            ProbeGround(ref m, b);

            // ---- dash: exact velocity, no gravity, no spring ----
            if (m.IsDashing)
            {
                if (_time >= m.DashUntil) { StopDash(ref m, b); v = new Vector3(m.DashVelocity.x, 0f, m.DashVelocity.z) * 0.6f; }
                else { b.linearVelocity = m.DashVelocity; return; }
            }

            // ---- ride spring: hover at the ride height, damped against the ground's motion ----
            if (m.IsGrounded && _time >= m.UngroundedUntil)
            {
                float error = m.RideHeight - m.GroundDistance;
                float omega = 2f * MathF.PI * MathF.Max(m.RideFrequency, 0.1f);
                float relVel = v.y - m.GroundVelocity.y;
                float a = error * omega * omega - relVel * 2f * m.RideDamping * omega;
                v.y += Mathf.Clamp(a, -m.MaxRideAcceleration, m.MaxRideAcceleration) * dt;
            }

            // ---- motor: accelerate the horizontal velocity towards the request (riding along with the ground) ----
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            Vector3 target = new Vector3(m.Desired.x, 0f, m.Desired.z);
            if (m.IsGrounded) target += new Vector3(m.GroundVelocity.x, 0f, m.GroundVelocity.z);
            float accel = !m.IsGrounded ? m.AirAcceleration : (target.sqrMagnitude > 0.01f ? m.Acceleration : m.StopDeceleration);
            Vector3 delta = Vector3.ClampMagnitude(target - horizontal, accel * dt);
            v.x += delta.x; v.z += delta.z;
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
            m.GroundVelocity = Vector3.zero;
            if (PhysicsApi.ShapeCast(in probe, Vector3.down, filter, out RaycastHit hit, reach))
            {
                m.GroundDistance = MathF.Max(0f, hit.distance - r * 0.05f); // the probe is slightly smaller than the capsule
                m.GroundNormal = hit.normal;
                m.Ground = hit.entity;
                m.IsGrounded = hit.normal.y >= cosSlope && m.GroundDistance <= m.RideHeight + m.GroundTolerance && _time >= m.UngroundedUntil;
                var gb = PhysicsApi.World.Table.BodyOf(hit.proxy);
                if (gb != null) m.GroundVelocity = gb.GetPointVelocity(hit.point);
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
    public struct KinematicMover : IComponent
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
        public MotionMode mode = MotionMode.Slide; public float bounciness = 0.4f, friction = 0.2f, restSpeed = 0.6f;
        public int collisionMask = ~0; public bool useLayerMatrix = true, collideWithTriggers;
        public float[] halfExtents = { 0, 0, 0 }, center = { 0, 0, 0 };
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
