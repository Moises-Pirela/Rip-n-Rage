using System.Collections.Generic;

namespace RipNRage.Engine
{
    [System.Flags]
    public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4 }

    /// <summary>
    /// Kinematic capsule mover with the UnityEngine.CharacterController API the FPS controller uses:
    /// Move, isGrounded, velocity, center, height, radius, stepOffset, skinWidth.
    /// Collides against all non-trigger colliders using axis-separated AABB sweeps.
    /// </summary>
    public sealed class CharacterController : Collider
    {
        public float height = 1.8f;
        public float radius = 0.5f;
        public float stepOffset = 0.3f;
        public float skinWidth = 0.08f;
        public float slopeLimit = 45f;
        public float minMoveDistance = 0.001f;

        private bool _isGrounded;
        private Vector3 _velocity;
        private CollisionFlags _flags;
        private readonly List<Collider> _buffer = new List<Collider>();

        public bool isGrounded => _isGrounded;
        public Vector3 velocity => _velocity;
        public CollisionFlags collisionFlags => _flags;

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

        public override Bounds bounds
        {
            get
            {
                Vector3 c = transform.position + center;
                float h = Mathf.Max(height, radius * 2f);
                return new Bounds(c - new Vector3(radius, h * 0.5f, radius), c + new Vector3(radius, h * 0.5f, radius));
            }
        }

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit) => Physics.RayAabb(ray, bounds, maxDistance, this, out hit);

        public override Vector3 ClosestPoint(Vector3 p)
        {
            var b = bounds;
            return new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.y, b.max.y), Mathf.Clamp(p.z, b.min.z, b.max.z));
        }

        /// <summary>Moves by <paramref name="motion"/> (world units) resolving collisions. Returns collision flags.</summary>
        public CollisionFlags Move(Vector3 motion)
        {
            if (!enabled) return CollisionFlags.None;
            Vector3 start = transform.position;
            _flags = CollisionFlags.None;

            // Gather candidate colliders once (broad phase around the swept box).
            Bounds sweep = bounds;
            Vector3 target = start + motion;
            Bounds endB = new Bounds(sweep.min + motion, sweep.max + motion);
            Bounds broad = new Bounds(Vector3.Min(sweep.min, endB.min) - new Vector3(1, 1, 1), Vector3.Max(sweep.max, endB.max) + new Vector3(1, 1, 1));
            _buffer.Clear();
            foreach (var c in Physics.All)
            {
                if (c == this || !c.IsEnabledForPhysics || c.isTrigger) continue;
                if (!Physics.LayersCollide(this, c)) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (c.bounds.Intersects(broad)) _buffer.Add(c);
            }

            Vector3 pos = start;

            // Horizontal movement with step-up support.
            Vector3 horizontal = new Vector3(motion.x, 0f, motion.z);
            if (horizontal.sqrMagnitude > 0f)
            {
                Vector3 before = pos;
                pos = SlideAxis(pos, 0, motion.x, out bool hitX);
                pos = SlideAxis(pos, 2, motion.z, out bool hitZ);
                if ((hitX || hitZ) && _isGrounded && stepOffset > 0f)
                {
                    // Try again from a raised position, then drop back down.
                    Vector3 raised = SlideAxis(before, 1, stepOffset, out bool hitUp);
                    if (!hitUp)
                    {
                        raised = SlideAxis(raised, 0, motion.x, out bool hx2);
                        raised = SlideAxis(raised, 2, motion.z, out bool hz2);
                        Vector3 dropped = SlideAxis(raised, 1, -stepOffset, out _);
                        float gainedBefore = new Vector3(pos.x - before.x, 0, pos.z - before.z).sqrMagnitude;
                        float gainedAfter = new Vector3(dropped.x - before.x, 0, dropped.z - before.z).sqrMagnitude;
                        if (gainedAfter > gainedBefore + 1E-06f) { pos = dropped; hitX = hx2; hitZ = hz2; }
                    }
                }
                if (hitX || hitZ) _flags |= CollisionFlags.Sides;
            }

            // Vertical movement decides grounded state.
            bool wasGrounded = _isGrounded;
            pos = SlideAxis(pos, 1, motion.y, out bool hitY);
            if (motion.y < 0f) _isGrounded = hitY;
            else if (motion.y > 0f) { _isGrounded = false; if (hitY) _flags |= CollisionFlags.Above; }
            else _isGrounded = ProbeGround(pos);
            if (_isGrounded) _flags |= CollisionFlags.Below;

            transform.position = pos;
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;
            _velocity = (pos - start) / dt;
            return _flags;
        }

        /// <summary>Teleports without collision (for spawning / respawn).</summary>
        public void Warp(Vector3 position)
        {
            transform.position = position;
            _velocity = Vector3.zero;
        }

        private Bounds BoundsAt(Vector3 pos)
        {
            Vector3 c = pos + center;
            float h = Mathf.Max(height, radius * 2f);
            // slightly shrunk so touching faces do not count as penetration
            float s = skinWidth * 0.5f;
            return new Bounds(c - new Vector3(radius - s, h * 0.5f - s, radius - s), c + new Vector3(radius - s, h * 0.5f - s, radius - s));
        }

        private Vector3 SlideAxis(Vector3 pos, int axis, float amount, out bool blocked)
        {
            blocked = false;
            if (Mathf.Abs(amount) < 1E-09f) return pos;
            pos[axis] += amount;
            Bounds b = BoundsAt(pos);
            for (int iter = 0; iter < 4; iter++)
            {
                bool any = false;
                foreach (var c in _buffer)
                {
                    Bounds o = c.bounds;
                    if (!b.Intersects(o)) continue;
                    if (c is ConvexCollider convex)
                    {
                        // rotated / sloped brush: separating-axis test, push out along the movement axis
                        float pen = convex.PenetrationAlong(b, axis);
                        if (pen <= 0f) continue;
                        any = true; blocked = true;
                        pos[axis] += (amount > 0f ? -1f : 1f) * (pen + 1E-04f);
                        b = BoundsAt(pos);
                        continue;
                    }
                    // only resolve against colliders that actually overlap on the other two axes by a margin
                    if (!OverlapsOtherAxes(b, o, axis)) continue;
                    any = true; blocked = true;
                    if (amount > 0f) pos[axis] -= (b.max[axis] - o.min[axis]) + 1E-04f;
                    else pos[axis] += (o.max[axis] - b.min[axis]) + 1E-04f;
                    b = BoundsAt(pos);
                }
                if (!any) break;
            }
            return pos;
        }

        private static bool OverlapsOtherAxes(Bounds a, Bounds b, int axis)
        {
            const float margin = 1E-03f;
            for (int i = 0; i < 3; i++)
            {
                if (i == axis) continue;
                if (a.max[i] - margin <= b.min[i] || a.min[i] + margin >= b.max[i]) return false;
            }
            return true;
        }

        private bool ProbeGround(Vector3 pos)
        {
            Bounds b = BoundsAt(pos);
            b.min.y -= skinWidth + 0.02f;
            foreach (var c in _buffer)
            {
                Bounds o = c.bounds;
                if (c is ConvexCollider convex) { if (convex.PenetrationAlong(b, 1) > 0f) return true; continue; }
                if (b.Intersects(o) && OverlapsOtherAxes(b, o, 1)) return true;
            }
            return false;
        }
    }
}
