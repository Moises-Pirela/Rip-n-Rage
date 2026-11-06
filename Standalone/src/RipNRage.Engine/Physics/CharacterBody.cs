using System;

namespace RipNRage.Engine
{
    /// <summary>
    /// A force-driven character: a capsule <see cref="RigidBody"/> that never rotates, kept <see cref="rideHeight"/>
    /// above the ground by a spring (a downward shape cast finds the ground; the spring's acceleration pulls the
    /// body to the ride height and damps it) and steered by accelerations towards the velocity the owner asks for.
    /// Everything is a force or an impulse on the body, so the character is a full member of the simulation: it
    /// pushes crates with its mass, gets shoved by blasts and other bodies, and slides along walls through the
    /// contact solver. Because it floats, ledges lower than the ride height are climbed without any step logic,
    /// slopes are followed by the spring and the body never scrapes the floor (no contact friction, the motor
    /// alone decides how fast it stops). Jumps and dashes are velocity changes with a short grace period during
    /// which the spring lets go.
    ///
    /// Call <see cref="Move"/> every frame with the horizontal velocity you want; the motor applies it in the
    /// fixed step. Used by the player (<c>FPSController</c>) and by enemies (<see cref="AI.NavMeshAgent"/>).
    /// </summary>
    /// <summary>A set of <see cref="CharacterBody"/> tunables (tunables.cfg [character] / [enemy_character]).</summary>
    public sealed class CharacterProfile
    {
        public float rideHeight = 0.5f;
        public float rideFrequency = 6f;
        public float rideDamping = 0.8f;
        public float maxRideAcceleration = 90f;
        public float groundTolerance = 0.25f;
        public float maxSlopeAngle = 50f;
        public float acceleration = 70f;
        public float airAcceleration = 20f;
        public float stopDeceleration = 60f;
        public float jumpGrace = 0.25f;
        public float mass = 80f;

        public CharacterProfile Clone() => (CharacterProfile)MemberwiseClone();
        public void CopyFrom(CharacterProfile o)
        {
            rideHeight = o.rideHeight; rideFrequency = o.rideFrequency; rideDamping = o.rideDamping; maxRideAcceleration = o.maxRideAcceleration;
            groundTolerance = o.groundTolerance; maxSlopeAngle = o.maxSlopeAngle; acceleration = o.acceleration; airAcceleration = o.airAcceleration;
            stopDeceleration = o.stopDeceleration; jumpGrace = o.jumpGrace; mass = o.mass;
        }
    }

    public enum CharacterProfileKind { Custom, Player, Enemy }

    public sealed class CharacterBody : Behaviour
    {
        /// <summary>Shared tunables new player / enemy bodies start from (see <see cref="PhysicsSettings"/>).</summary>
        public static readonly CharacterProfile PlayerProfile = new CharacterProfile();
        public static readonly CharacterProfile EnemyProfile = new CharacterProfile { rideHeight = 0.35f, acceleration = 40f, airAcceleration = 8f, stopDeceleration = 40f, mass = 85f };
        /// <summary>Which shared profile this body follows (Custom = its own values only).</summary>
        public CharacterProfileKind profile = CharacterProfileKind.Custom;

        // ---- ride (the hovering spring) ----
        /// <summary>Gap kept between the bottom of the capsule and the ground (also the tallest ledge climbed for free).</summary>
        public float rideHeight = 0.5f;
        /// <summary>Spring stiffness as a natural frequency (Hz).</summary>
        public float rideFrequency = 6f;
        /// <summary>Spring damping ratio (1 = critical).</summary>
        public float rideDamping = 0.8f;
        /// <summary>Largest acceleration the spring may apply (m/s²).</summary>
        public float maxRideAcceleration = 90f;
        /// <summary>How far below the ride height the ground is still "under us" (stairs down, slope crests) before the body counts as airborne.</summary>
        public float groundTolerance = 0.25f;
        /// <summary>Steepest ground (degrees) the character can stand on; steeper surfaces count as walls and it slides off them.</summary>
        public float maxSlopeAngle = 50f;

        // ---- motor ----
        /// <summary>Horizontal acceleration towards the requested velocity on the ground (m/s²).</summary>
        public float acceleration = 70f;
        /// <summary>Acceleration while airborne (air control).</summary>
        public float airAcceleration = 20f;
        /// <summary>Deceleration when no velocity is requested (stopping).</summary>
        public float stopDeceleration = 60f;
        /// <summary>Seconds after a jump or an upward impulse during which the spring does not pull the body back down.</summary>
        public float jumpGrace = 0.25f;
        /// <summary>Layers that count as ground.</summary>
        public int groundMask = ~0;

        /// <summary>Copies the shared profile's values into this body (called at Awake and whenever the settings change).</summary>
        public void ApplyProfile()
        {
            var p = profile == CharacterProfileKind.Player ? PlayerProfile : profile == CharacterProfileKind.Enemy ? EnemyProfile : null;
            if (p == null) return;
            rideHeight = p.rideHeight; rideFrequency = p.rideFrequency; rideDamping = p.rideDamping; maxRideAcceleration = p.maxRideAcceleration;
            groundTolerance = p.groundTolerance; maxSlopeAngle = p.maxSlopeAngle; acceleration = p.acceleration; airAcceleration = p.airAcceleration;
            stopDeceleration = p.stopDeceleration; jumpGrace = p.jumpGrace;
            if (_body != null && p.mass > 0f && !_body.isKinematic) { _body.mass = p.mass; _body.ComputeMass(); }
        }

        private RigidBody _body;
        private CapsuleCollider _capsule;
        private Vector3 _desired;
        private bool _desiredSet;
        private float _ungroundedUntil;
        private float _dashUntil;
        private Vector3 _dashVelocity;
        private float _savedGravityScale = 1f;
        private bool _dashing;

        // ---- state ----
        public bool isGrounded { get; private set; }
        public Vector3 groundNormal { get; private set; } = Vector3.up;
        public Collider ground { get; private set; }
        /// <summary>Gap between the capsule bottom and the ground (ride height when resting; large when airborne).</summary>
        public float groundDistance { get; private set; } = float.PositiveInfinity;
        /// <summary>Velocity of whatever we stand on (moving props).</summary>
        public Vector3 groundVelocity { get; private set; }
        public bool isDashing => _dashing;
        public RigidBody body => _body;
        public CapsuleCollider capsule => _capsule;
        public Vector3 velocity => _body != null ? _body.linearVelocity : Vector3.zero;
        public float radius => _capsule != null ? _capsule.worldRadius : 0.4f;
        /// <summary>Full capsule height (local units).</summary>
        public float height => _capsule != null ? _capsule.height : 1.8f;
        /// <summary>Height of the transform above the ground when resting: capsule bottom offset + ride height.</summary>
        public float standingHeight => _capsule != null ? -(_capsule.center.y - _capsule.height * 0.5f) + rideHeight : rideHeight;

        protected override void Awake()
        {
            _body = GetComponent<RigidBody>();
            _capsule = GetComponent<CapsuleCollider>();
            if (_body == null) { _body = gameObject.AddComponent<RigidBody>(); _body.mass = 80f; }
            // a character never tumbles, never sleeps, never scrapes and never tunnels
            _body.freezeRotation = true;
            _body.canSleep = false;
            _body.friction = 0.05f;
            _body.restitution = 0f;
            _body.interpolate = true;
            _body.collisionDetection = CollisionDetectionMode.Continuous;
            _body.linearDamping = 0f;
            _body.angularDamping = 0f;
            _savedGravityScale = _body.gravityScale;
            ApplyProfile();
        }

        // ------------------------------------------------------------------ commands

        /// <summary>Horizontal velocity to steer towards (world units per second); call every frame, the y component is ignored.</summary>
        public void Move(Vector3 desiredHorizontalVelocity)
        {
            _desired = new Vector3(desiredHorizontalVelocity.x, 0f, desiredHorizontalVelocity.z);
            _desiredSet = true;
        }

        /// <summary>Jump: the vertical velocity becomes <paramref name="upSpeed"/> and the spring lets go for <see cref="jumpGrace"/>.</summary>
        public void Jump(float upSpeed)
        {
            if (_body == null) return;
            Vector3 v = _body.linearVelocity;
            v.y = MathF.Max(v.y, 0f) + upSpeed;
            _body.linearVelocity = v;
            Unground(jumpGrace);
        }

        /// <summary>Dash: the body moves at <paramref name="velocity"/> for <paramref name="duration"/> seconds without gravity, then hands back to the motor.</summary>
        public void Dash(Vector3 velocity, float duration)
        {
            if (_body == null) return;
            if (!_dashing) _savedGravityScale = _body.gravityScale;
            _dashing = true;
            _dashVelocity = velocity;
            _dashUntil = Time.time + duration;
            _body.gravityScale = 0f;
            _body.linearVelocity = velocity;
            Unground(duration + 0.05f);
        }

        /// <summary>Ends a dash early.</summary>
        public void StopDash()
        {
            if (!_dashing) return;
            _dashing = false;
            _body.gravityScale = _savedGravityScale;
        }

        /// <summary>Instant velocity change (knockback, rocket jump); an upward push also lets the spring go for a moment.</summary>
        public void AddVelocityChange(Vector3 delta)
        {
            if (_body == null) return;
            _body.linearVelocity += delta;
            if (delta.y > 0.5f) Unground(jumpGrace);
        }

        /// <summary>Keeps the spring off (and the body airborne) for <paramref name="seconds"/>.</summary>
        public void Unground(float seconds)
        {
            _ungroundedUntil = MathF.Max(_ungroundedUntil, Time.time + seconds);
            isGrounded = false;
        }

        /// <summary>Never fall faster than <paramref name="minVerticalSpeed"/> (wall runs glide).</summary>
        public void ClampVerticalVelocity(float minVerticalSpeed)
        {
            if (_body == null) return;
            Vector3 v = _body.linearVelocity;
            if (v.y < minVerticalSpeed) { v.y = minVerticalSpeed; _body.linearVelocity = v; }
        }

        /// <summary>Gravity multiplier of the body (wall runs use a fraction of it).</summary>
        public float gravityScale
        {
            get => _dashing ? _savedGravityScale : (_body != null ? _body.gravityScale : 1f);
            set { if (_dashing) _savedGravityScale = value; else if (_body != null) _body.gravityScale = value; }
        }

        /// <summary>Teleport without collision (spawn, respawn, load).</summary>
        public void Warp(Vector3 position)
        {
            if (_body == null) { transform.position = position; return; }
            StopDash();
            _body.MovePosition(position, resetVelocity: true);
            _body.ReadTransform();
            isGrounded = false; groundDistance = float.PositiveInfinity;
            _ungroundedUntil = 0f;
        }

        public void ResetVelocity()
        {
            StopDash();
            if (_body != null) { _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
            _desired = Vector3.zero;
        }

        /// <summary>Changes the capsule height keeping its bottom where it is (crouch / slide) and refreshes the body's mass properties.</summary>
        public void SetHeight(float newHeight)
        {
            if (_capsule == null) return;
            float bottom = _capsule.center.y - _capsule.height * 0.5f;
            _capsule.height = MathF.Max(newHeight, _capsule.radius * 2f);
            _capsule.center = new Vector3(_capsule.center.x, bottom + _capsule.height * 0.5f, _capsule.center.z);
            if (_body == null) return;
            _body.WriteTransform();   // the true pose (the transform may hold an interpolated one)
            _body.ComputeMass();      // the centre of mass moved with the capsule centre
            _body.ReadTransform();
        }

        // ------------------------------------------------------------------ simulation

        protected override void FixedUpdate()
        {
            if (_body == null || !_body.isActiveAndEnabled) return;
            float dt = Time.fixedDeltaTime;
            ProbeGround();

            Vector3 v = _body.linearVelocity;

            // dash: exact velocity, no gravity, no spring
            if (_dashing)
            {
                if (Time.time >= _dashUntil) { StopDash(); v = new Vector3(_dashVelocity.x, 0f, _dashVelocity.z) * 0.6f; }
                else { v = _dashVelocity; _body.linearVelocity = v; _desiredSet = false; return; }
            }

            // ride spring: hover at the ride height, damped against the ground's motion
            bool springAllowed = isGrounded && Time.time >= _ungroundedUntil;
            if (springAllowed)
            {
                float error = rideHeight - groundDistance;
                float omega = 2f * MathF.PI * MathF.Max(rideFrequency, 0.1f);
                float relVel = v.y - groundVelocity.y;
                float a = error * omega * omega - relVel * 2f * rideDamping * omega;
                a = Mathf.Clamp(a, -maxRideAcceleration, maxRideAcceleration);
                v.y += a * dt;
                // do not let the spring's correction fight a push that is clearly lifting us
            }

            // motor: accelerate the horizontal velocity towards the request
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            Vector3 target = _desiredSet ? _desired : Vector3.zero;
            if (isGrounded) target += new Vector3(groundVelocity.x, 0f, groundVelocity.z); // ride along with what we stand on
            float accel = !isGrounded ? airAcceleration : (target.sqrMagnitude > 0.01f ? acceleration : stopDeceleration);
            Vector3 delta = Vector3.ClampMagnitude(target - horizontal, accel * dt);
            v.x += delta.x; v.z += delta.z;

            _body.linearVelocity = v;
            _body.angularVelocity = Vector3.zero;
            _desiredSet = false;
        }

        private void ProbeGround()
        {
            var shape = _capsule != null ? _capsule.shape : CollisionShape.Sphere(transform.position, 0.4f);
            float half = _capsule != null ? _capsule.worldHalfHeight : 0f;
            float r = _capsule != null ? _capsule.worldRadius : 0.4f;
            // cast the bottom sphere of the capsule downwards
            Vector3 bottomCentre = shape.center - shape.Axis * half;
            var probe = CollisionShape.Sphere(bottomCentre, r * 0.95f);
            float reach = rideHeight + groundTolerance + 0.05f;
            var filter = _capsule != null ? QueryFilter.For(_capsule, groundMask) : QueryFilter.Solids(groundMask);
            float cosSlope = MathF.Cos(Mathf.Clamp(maxSlopeAngle, 1f, 89f) * Mathf.Deg2Rad);
            groundVelocity = Vector3.zero;
            if (Physics.ShapeCast(in probe, Vector3.down, filter, out RaycastHit hit, reach))
            {
                groundDistance = MathF.Max(0f, hit.distance - r * 0.05f); // the probe is slightly smaller than the capsule
                groundNormal = hit.normal;
                ground = hit.collider;
                bool walkable = hit.normal.y >= cosSlope;
                isGrounded = walkable && groundDistance <= rideHeight + groundTolerance && Time.time >= _ungroundedUntil;
                var rb = hit.collider != null ? hit.collider.attachedRigidbody : null;
                if (rb != null) groundVelocity = rb.GetPointVelocity(hit.point);
            }
            else
            {
                groundDistance = float.PositiveInfinity;
                groundNormal = Vector3.up;
                ground = null;
                isGrounded = false;
            }
        }
    }
}
