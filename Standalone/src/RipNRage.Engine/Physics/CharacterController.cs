using System.Collections.Generic;

namespace RipNRage.Engine
{
    [System.Flags]
    public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4 }

    /// <summary>Collision volume of a <see cref="CharacterController"/>.</summary>
    public enum ControllerShape { Box, Capsule }

    /// <summary>
    /// Kinematic box mover with the UnityEngine.CharacterController API the FPS controller uses:
    /// Move, isGrounded, velocity, center, height, radius, stepOffset, skinWidth, slopeLimit.
    ///
    /// It is a thin shell over <see cref="Physics.MoveAndSlide"/>: one continuous collide-and-slide per Move,
    /// with stairs (<see cref="stepOffset"/>), walkable-slope classification (<see cref="slopeLimit"/>) and ground
    /// snapping. Sweeps are exact and continuous, so a long frame cannot push the box through a floor or a wall.
    /// The only safety net left is the rewind: if a Move has to push the box out of geometry by more than
    /// <see cref="maxCorrection"/> it was already badly stuck (spawned inside a brush, teleported, knocked through a
    /// wall by a bug elsewhere) and it returns to the last position known to be free instead of being shot out of a slab.
    /// </summary>
    public sealed class CharacterController : Collider
    {
        public float height = 1.8f;
        public float radius = 0.5f;
        public float stepOffset = 0.3f;
        public float skinWidth = 0.08f;
        public float slopeLimit = 45f;
        public float minMoveDistance = 0.001f;
        /// <summary>Box (the original square-footprint mover) or a true capsule of <see cref="radius"/> / <see cref="height"/> (Unity's shape: rounds corners, slides around edges).</summary>
        public ControllerShape controllerShape = ControllerShape.Box;
        /// <summary>Pull the box down onto floors within <see cref="stepOffset"/> after a grounded move (stairs down, slope crests).</summary>
        public bool snapToGround = true;
        /// <summary>Default push mass of new controllers (tunables.cfg [movers]).</summary>
        public static float DefaultPushMass = 80f;
        /// <summary>Mass the character pushes rigid bodies with (bodies lighter than this take its full speed; 0 = never push).</summary>
        public float pushMass = DefaultPushMass;
        /// <summary>Push-out larger than this is treated as "the mover was stuck" and rewound instead of applied.</summary>
        public float maxCorrection = 0.6f;
        /// <summary>How far back in the world a rewind may reach before it would itself feel like a teleport.</summary>
        public float rewindRange = 8f;

        /// <summary>Slide iterations of the last Move (debug / overlay).</summary>
        public int LastSubsteps { get; private set; }
        /// <summary>Depenetration distance applied on the last Move (debug / overlay).</summary>
        public float LastCorrection { get; private set; }
        /// <summary>How often the mover was found badly stuck and rewound to its last free position (debug / overlay).</summary>
        public int StuckRewinds { get; private set; }

        private readonly MoveResult _result = new MoveResult();
        private bool _isGrounded;
        private Vector3 _safeSpot;
        private bool _hasSafeSpot;
        private float _lastStuckWarning = -10f;
        private Vector3 _velocity;
        private CollisionFlags _flags;

        public bool isGrounded => _isGrounded;
        public Vector3 velocity => _velocity;
        public CollisionFlags collisionFlags => _flags;
        /// <summary>Normal of the floor under the box (up when airborne).</summary>
        public Vector3 groundNormal => _isGrounded ? _result.groundNormal : Vector3.up;
        /// <summary>The collider the box stands on (null when airborne).</summary>
        public Collider ground => _isGrounded ? _result.ground : null;
        /// <summary>Surfaces touched by the last Move.</summary>
        public IReadOnlyList<Contact> Contacts => _result.contacts;
        /// <summary>Full result of the last Move.</summary>
        public MoveResult LastMove => _result;

        protected override void Awake()
        {
            base.Awake();
            Physics.RegisterMover(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Physics.UnregisterMover(this);
        }

        /// <summary>Half size of the collision box (the capsule's bounding box when <see cref="controllerShape"/> is a capsule).</summary>
        public Vector3 halfExtents => new Vector3(radius, Mathf.Max(height, radius * 2f) * 0.5f, radius);

        /// <summary>The controller's collision shape at a given centre.</summary>
        public CollisionShape ShapeAt(Vector3 worldCenter) => controllerShape == ControllerShape.Capsule
            ? CollisionShape.Capsule(worldCenter, Quaternion.identity, radius, Mathf.Max(height * 0.5f - radius, 0f))
            : CollisionShape.AxisAlignedBox(worldCenter, halfExtents);

        public override CollisionShape shape => ShapeAt(transform.position + center);
        public override Bounds bounds => Bounds.FromCenterExtents(transform.position + center, halfExtents);

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            if (controllerShape != ControllerShape.Capsule) return Physics.RayAabb(ray, bounds, maxDistance, this, out hit);
            var s = shape;
            return Physics.RayCapsule(ray, s.center - Vector3.up * s.halfHeight, s.center + Vector3.up * s.halfHeight, s.radius, maxDistance, this, out hit);
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            if (controllerShape == ControllerShape.Capsule)
            {
                var s = shape;
                Vector3 a = s.center - Vector3.up * s.halfHeight, b2 = s.center + Vector3.up * s.halfHeight;
                Vector3 ab = b2 - a;
                float t = ab.sqrMagnitude > 1E-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                Vector3 q = a + ab * t;
                Vector3 d = p - q;
                return d.sqrMagnitude <= s.radius * s.radius ? p : q + d.normalized * s.radius;
            }
            var b = bounds;
            return new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.y, b.max.y), Mathf.Clamp(p.z, b.min.z, b.max.z));
        }

        /// <summary>Gap kept to surfaces: a quarter of the Unity skin width, within sane bounds.</summary>
        private float Skin => Mathf.Clamp(skinWidth * 0.25f, 0.004f, 0.02f);

        private MoveSettings Settings(Vector3 motion) => new MoveSettings
        {
            filter = QueryFilter.For(this),
            skin = Skin,
            maxSlides = 5,
            floorMaxAngle = Mathf.Clamp(slopeLimit, 1f, 89f),
            stepHeight = _isGrounded ? stepOffset : 0f,
            snapLength = snapToGround && _isGrounded && motion.y <= 0f ? stepOffset : 0f,
            minMove = minMoveDistance,
            wasGrounded = _isGrounded,
        };

        /// <summary>Moves by <paramref name="motion"/> (world units) resolving collisions. Returns collision flags.</summary>
        public CollisionFlags Move(Vector3 motion)
        {
            if (!enabled) return CollisionFlags.None;
            Vector3 start = transform.position;
            Vector3 offset = center;
            Vector3 e = halfExtents;

            Physics.MoveAndSlide(ShapeAt(start + offset), motion, Settings(motion), _result);
            Vector3 pos = _result.position - offset;
            _isGrounded = _result.isGrounded;
            _flags = _result.flags;
            LastSubsteps = _result.slides;
            LastCorrection = _result.depenetration;

            // safety net: a correction big enough to read as a teleport means the mover was already badly stuck.
            // Rewinding to the last spot known to be free is always closer to where the player expects to be.
            if ((_result.depenetration > maxCorrection || _result.stuck) && _hasSafeSpot &&
                (_safeSpot - start).sqrMagnitude < rewindRange * rewindRange &&
                !Physics.CheckShape(ShapeAt(_safeSpot + offset), QueryFilter.For(this)))
            {
                StuckRewinds++;
                if (Time.unscaledTime - _lastStuckWarning > 1f)
                {
                    _lastStuckWarning = Time.unscaledTime;
                    Debug.LogWarning($"CharacterController: {_result.depenetration:0.00}m push-out needed at {pos} — rewinding to the last free position {_safeSpot}");
                }
                pos = _safeSpot;
                var probe = Settings(Vector3.zero);
                probe.wasGrounded = true;
                Physics.MoveAndSlide(ShapeAt(pos + offset), Vector3.zero, probe, _result);
                pos = _result.position - offset;
                _isGrounded = _result.isGrounded;
                _flags = _result.flags;
            }

            // the position is remembered only while it is genuinely free, so the rewind above can be trusted
            if (!_result.stuck) { _safeSpot = pos; _hasSafeSpot = true; }

            transform.position = pos;
            Physics.World.Touch(this);
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;
            _velocity = (pos - start) / dt;
            Physics.PushRigidBodies(_result, _velocity, pushMass); // the velocity actually achieved, not the requested one
            return _flags;
        }

        /// <summary>Teleports without collision (for spawning / respawn).</summary>
        public void Warp(Vector3 position)
        {
            transform.position = position;
            Physics.World.Touch(this);
            _velocity = Vector3.zero;
            _hasSafeSpot = false; // the remembered spot belongs to wherever we came from
            _result.Reset(position + center);
        }
    }
}
