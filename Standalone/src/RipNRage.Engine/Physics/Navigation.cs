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
    /// Straight-line steering agent with the UnityEngine.AI.NavMeshAgent API the enemy code uses.
    /// Moves on the XZ plane towards its destination, sticks to the ground and stops at obstacles.
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

        private Vector3 _destination;
        private bool _hasDestination;
        private Vector3 _velocity;
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

        public bool Warp(Vector3 position) { transform.position = position; return true; }

        protected override void Update()
        {
            if (!_hasDestination || isStopped) { _velocity = Vector3.Lerp(_velocity, Vector3.zero, Time.deltaTime * acceleration); return; }
            Vector3 desired = desiredVelocity;
            if (remainingDistance <= Mathf.Max(stoppingDistance, 0.05f)) { _velocity = Vector3.zero; return; }
            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * Time.deltaTime);
            Vector3 step = _velocity * Time.deltaTime;
            Vector3 pos = transform.position;
            // obstacle check against non-trigger, non-character colliders
            int mask = ~(1 << Layers.Player | 1 << Layers.Enemies | 1 << Layers.Pickup);
            if (step.sqrMagnitude > 0f && Physics.SphereCast(pos + Vector3.up * 0.5f, radius * 0.5f, step.normalized, out _, step.magnitude + radius, mask))
            {
                _velocity = Vector3.zero;
                return;
            }
            pos += step;
            // stick to ground
            if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out var hit, 3f + baseOffset, mask))
                pos.y = hit.point.y + baseOffset;
            transform.position = pos;
            if (updateRotation && step.sqrMagnitude > 1E-06f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(step.normalized), angularSpeed * Time.deltaTime);
        }
    }
}
