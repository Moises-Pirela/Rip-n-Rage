using System.Collections.Generic;

namespace RipNRage.Engine.AI
{
    public struct NavMeshHit
    {
        public Vector3 position;
        public bool hit;
    }

    public sealed class NavMeshPath
    {
        public Vector3[] corners = System.Array.Empty<Vector3>();
    }

    /// <summary>
    /// Replacement for Unity's NavMesh queries. Without a baked navmesh, positions are sampled by
    /// dropping a ray onto the level geometry so AI wander targets land on walkable ground.
    /// </summary>
    public static class NavMesh
    {
        public const int AllAreas = -1;

        public static bool SamplePosition(Vector3 source, out NavMeshHit hit, float maxDistance, int areaMask)
        {
            hit = new NavMeshHit { position = source, hit = false };
            if (Physics.Raycast(source + Vector3.up * maxDistance, Vector3.down, out var rh, maxDistance * 2f, ~(1 << Layers.Player | 1 << Layers.Enemies)))
            {
                hit.position = rh.point;
                hit.hit = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Straight-line steering agent with the UnityEngine.AI.NavMeshAgent API the enemy code uses. Moves on the
    /// XZ plane towards its destination and is carried through the world by <see cref="Physics.MoveAndSlide"/>:
    /// gravity keeps it on the ground, it slides along obstacles, climbs ledges up to <see cref="stepHeight"/> and
    /// follows slopes and stairs down. Its collision box is the <see cref="CapsuleCollider"/> on the same object
    /// when there is one (so the mover and the hit box agree), otherwise <see cref="radius"/> / <see cref="height"/>
    /// around the transform with the base <see cref="baseOffset"/> below it.
    /// </summary>
    public sealed class NavMeshAgent : Behaviour
    {
        public float speed = 3.5f;
        public float angularSpeed = 120f;
        public float acceleration = 8f;
        public float stoppingDistance = 0f;
        public float radius = 0.5f;
        public float height = 2f;
        /// <summary>Vertical offset of the transform above the ground (Unity's NavMeshAgent.baseOffset).</summary>
        public float baseOffset = 0f;
        public bool updateRotation = false;
        public bool isStopped;
        /// <summary>Defaults of new agents (tunables.cfg [movers]).</summary>
        public static float DefaultStepHeight = 0.4f, DefaultSlopeLimit = 50f;
        /// <summary>Highest ledge the agent walks up in one step.</summary>
        public float stepHeight = DefaultStepHeight;
        /// <summary>Steepest slope (degrees) the agent treats as ground.</summary>
        public float slopeLimit = DefaultSlopeLimit;
        /// <summary>Layers that block the agent: level geometry only by default — never other agents, the player, pickups or projectiles.</summary>
        public int collisionMask = ~(1 << Layers.Player | 1 << Layers.Enemies | 1 << Layers.Pickup | 1 << Layers.Projectile | 1 << Layers.VisionSensor);

        private Vector3 _destination;
        private bool _hasDestination;
        private Vector3 _velocity;
        private float _verticalSpeed;
        private bool _grounded;
        private Collider _shape;
        private bool _shapeLooked;
        private CharacterBody _motor;
        private bool _motorLooked;

        /// <summary>The force-driven body carrying this agent (when the object has a <see cref="CharacterBody"/>); null = the agent moves itself with the kinematic solver.</summary>
        public CharacterBody motor
        {
            get
            {
                if (!_motorLooked) { _motor = GetComponent<CharacterBody>(); _motorLooked = true; }
                return _motor != null && !_motor.destroyed ? _motor : null;
            }
        }
        private readonly MoveResult _move = new MoveResult();
        private readonly NavMeshPath _path = new NavMeshPath();

        public Vector3 velocity { get => _velocity; set => _velocity = value; }
        public Vector3 desiredVelocity
        {
            get
            {
                if (!_hasDestination) return Vector3.zero;
                Vector3 d = _destination - transform.position; d.y = 0f;
                return d.sqrMagnitude > 1E-04f ? d.normalized * speed : Vector3.zero;
            }
        }
        public Vector3 destination => _destination;
        public bool hasPath => _hasDestination;
        public NavMeshPath path => _path;
        public bool isGrounded => _grounded;
        /// <summary>Result of the last physical move (contacts, ground).</summary>
        public MoveResult LastMove => _move;
        public float remainingDistance
        {
            get
            {
                if (!_hasDestination) return float.PositiveInfinity;
                Vector3 d = _destination - transform.position; d.y = 0f;
                return d.magnitude;
            }
        }

        public bool SetDestination(Vector3 target)
        {
            _destination = target;
            _hasDestination = true;
            _path.corners = new[] { transform.position, target };
            return true;
        }

        public void ResetPath() { _hasDestination = false; _velocity = Vector3.zero; }

        public bool Warp(Vector3 position)
        {
            var m = motor;
            if (m != null) { m.Warp(position); _velocity = Vector3.zero; return true; }
            transform.position = position; _verticalSpeed = 0f; _move.Reset(Shape.center); return true;
        }

        /// <summary>The agent's collision shape in world space: its capsule collider, else a capsule of <see cref="radius"/> / <see cref="height"/>.</summary>
        public CollisionShape Shape
        {
            get
            {
                if (!_shapeLooked) { _shape = GetComponent<CapsuleCollider>(); _shapeLooked = true; }
                if (_shape != null && !_shape.destroyed) return _shape.shape;
                float r = Mathf.Max(radius, 0.05f), h = Mathf.Max(height, r * 2f);
                Vector3 c = transform.position + Vector3.up * (h * 0.5f - baseOffset);
                return CollisionShape.Capsule(c, Quaternion.identity, r, Mathf.Max(h * 0.5f - r, 0f));
            }
        }

        /// <summary>Bounds of the collision shape.</summary>
        public Bounds Box => Shape.bounds;

        protected override void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // steering on the XZ plane
            if (!_hasDestination || isStopped) _velocity = Vector3.Lerp(_velocity, Vector3.zero, dt * acceleration);
            else if (remainingDistance <= Mathf.Max(stoppingDistance, 0.05f)) _velocity = Vector3.zero;
            else _velocity = Vector3.MoveTowards(_velocity, desiredVelocity, acceleration * dt);
            _velocity.y = 0f;

            // a force-driven body: hand it the velocity we want, it does the rest (gravity, ground, walls, pushing)
            var m = motor;
            if (m != null)
            {
                m.Move(_velocity);
                _grounded = m.isGrounded;
                Vector3 stepM = new Vector3(_velocity.x, 0f, _velocity.z) * dt;
                if (updateRotation && stepM.sqrMagnitude > 1E-06f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(stepM.normalized), angularSpeed * Time.deltaTime);
                return;
            }

            // gravity: a light downward push while grounded keeps the ground contact, free fall otherwise
            _verticalSpeed = _grounded ? -2f : Mathf.Max(_verticalSpeed + Physics.gravity.y * dt, -50f);
            Vector3 motion = (_velocity + Vector3.up * _verticalSpeed) * dt;

            CollisionShape shape = Shape;
            Vector3 offset = shape.center - transform.position;
            var settings = new MoveSettings
            {
                filter = new QueryFilter { layerMask = collisionMask, self = _shape, ignore = transform },
                skin = 0f, maxSlides = 4, floorMaxAngle = slopeLimit,
                stepHeight = _grounded ? stepHeight : 0f, snapLength = _grounded ? stepHeight : 0f,
                minMove = 1E-05f, wasGrounded = _grounded,
            };
            Physics.MoveAndSlide(in shape, motion, settings, _move);
            transform.position = _move.position - offset;
            _grounded = _move.isGrounded;
            if (_grounded && _verticalSpeed < 0f) _verticalSpeed = 0f;
            // a wall the steering keeps pushing into: drop the blocked part so the agent is not reported as moving
            if (_move.hitWall) _velocity = _move.ClipVelocity(_velocity);

            Vector3 step = new Vector3(_velocity.x, 0f, _velocity.z) * dt;
            if (updateRotation && step.sqrMagnitude > 1E-06f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(step.normalized), angularSpeed * Time.deltaTime);
        }
    }
}
