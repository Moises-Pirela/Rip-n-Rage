using System.Collections.Generic;

namespace RipNRage.Engine
{
    [System.Flags]
    public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4 }

    /// <summary>
    /// Kinematic capsule mover with the UnityEngine.CharacterController API the FPS controller uses:
    /// Move, isGrounded, velocity, center, height, radius, stepOffset, skinWidth.
    /// Collides against all non-trigger colliders using axis-separated AABB sweeps. Every sweep is split into
    /// sub-steps no longer than <see cref="MaxSubstep"/>: a long frame (hitch, low fps) used to move the box
    /// several metres at once, which let it jump clean over floors and walls — the classic "fell through the
    /// level" — so now no single step can ever exceed a fraction of the box size.
    /// </summary>
    public sealed class CharacterController : Collider
    {
        public float height = 1.8f;
        public float radius = 0.5f;
        public float stepOffset = 0.3f;
        public float skinWidth = 0.08f;
        public float slopeLimit = 45f;
        public float minMoveDistance = 0.001f;
        /// <summary>Longest distance one collision sweep may cover (metres); longer motions are sub-stepped so thin geometry cannot be tunnelled through.</summary>
        public float MaxSubstep = 0.2f;
        /// <summary>Sub-steps taken in the last Move (debug).</summary>
        public int LastSubsteps { get; private set; }
        /// <summary>Push-out larger than this is treated as "the mover was stuck" and rewound instead of applied.</summary>
        public float maxCorrection = 0.6f;
        /// <summary>How far back in the world a rewind may reach before it would itself feel like a teleport.</summary>
        public float rewindRange = 8f;
        /// <summary>Depenetration distance applied on the last Move (debug / overlay).</summary>
        public float LastCorrection { get; private set; }
        /// <summary>How often the mover was found badly stuck and rewound to its last free position (debug / overlay).</summary>
        public int StuckRewinds { get; private set; }

        private bool _isGrounded;
        private Vector3 _safeSpot;
        private bool _hasSafeSpot;
        private float _lastStuckWarning = -10f;
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
            LastSubsteps = 0;

            // Horizontal movement with step-up support.
            Vector3 horizontal = new Vector3(motion.x, 0f, motion.z);
            if (horizontal.sqrMagnitude > 0f)
            {
                Vector3 before = pos;
                pos = SlideAxisStepped(pos, 0, motion.x, out bool hitX);
                pos = SlideAxisStepped(pos, 2, motion.z, out bool hitZ);
                if ((hitX || hitZ) && _isGrounded && stepOffset > 0f)
                {
                    // Try again from a raised position, then drop back down.
                    Vector3 raised = SlideAxis(before, 1, stepOffset, out bool hitUp);
                    if (!hitUp)
                    {
                        raised = SlideAxisStepped(raised, 0, motion.x, out bool hx2);
                        raised = SlideAxisStepped(raised, 2, motion.z, out bool hz2);
                        Vector3 dropped = SlideAxis(raised, 1, -stepOffset, out _);
                        float gainedBefore = new Vector3(pos.x - before.x, 0, pos.z - before.z).sqrMagnitude;
                        float gainedAfter = new Vector3(dropped.x - before.x, 0, dropped.z - before.z).sqrMagnitude;
                        if (gainedAfter > gainedBefore + 1E-06f) { pos = dropped; hitX = hx2; hitZ = hz2; }
                    }
                }
                if (hitX || hitZ) _flags |= CollisionFlags.Sides;
            }

            // Vertical movement decides grounded state.
            pos = SlideAxisStepped(pos, 1, motion.y, out bool hitY);
            if (motion.y < 0f) _isGrounded = hitY;
            else if (motion.y > 0f) { _isGrounded = false; if (hitY) _flags |= CollisionFlags.Above; }
            else _isGrounded = ProbeGround(pos);
            if (_isGrounded) _flags |= CollisionFlags.Below;

            // safety net 1: whatever the sweeps did, never end a frame inside solid geometry
            Vector3 resolved = Depenetrate(pos);
            float correction = (resolved - pos).magnitude;
            LastCorrection = correction;

            // safety net 2: a correction big enough to read as a teleport means the mover was already badly stuck
            // (spawned inside a brush, a hitch, a knockback through a wall). Rewinding to the last spot that was
            // known to be free is always closer to where the player expects to be than being shot out of a slab.
            if (correction > maxCorrection && _hasSafeSpot && (_safeSpot - start).sqrMagnitude < rewindRange * rewindRange && !Overlapping(BoundsAt(_safeSpot)))
            {
                StuckRewinds++;
                if (Time.unscaledTime - _lastStuckWarning > 1f)
                {
                    _lastStuckWarning = Time.unscaledTime;
                    Debug.LogWarning($"CharacterController: {correction:0.00}m push-out needed at {pos} — rewinding to the last free position {_safeSpot}");
                }
                pos = _safeSpot;
                _isGrounded = ProbeGround(pos);
            }
            else pos = resolved;

            // the position is remembered only while it is genuinely free, so the rewind above can be trusted
            if (!Overlapping(BoundsAt(pos))) { _safeSpot = pos; _hasSafeSpot = true; }

            transform.position = pos;
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;
            _velocity = (pos - start) / dt;
            return _flags;
        }

        /// <summary>
        /// Pushes the box out of anything it still overlaps after the sweeps (a teleport into a wall, a hitch that
        /// sank it into a slab, an unlucky order of axis sweeps) along the true minimum translation vector: for a
        /// box, the cheapest of the six face exits; for a brush, the cheapest of its expanded face planes and the
        /// three box axes. Crucially the MTV is measured against the face the box is closest to clearing, so it is
        /// the small real penetration — the old code compared projection overlap lengths, which for a character
        /// contained inside a big collider are just that collider's extents, so a 2 cm sink into a dock plank could
        /// resolve as a shove of tens of metres. Pushing down is charged double so the mover pops up out of floors
        /// rather than dropping through them, while a shallow head bump into a ceiling still resolves downwards.
        /// </summary>
        private Vector3 Depenetrate(Vector3 pos)
        {
            const float tolerance = 0.005f;
            const float downBias = 2f; // ejecting a character downwards out of the world is nearly always wrong
            for (int iter = 0; iter < 6; iter++)
            {
                Bounds b = BoundsAt(pos);
                bool any = false;
                foreach (var c in _buffer)
                {
                    Bounds o = c.bounds;
                    if (!b.Intersects(o)) continue;
                    Vector3 n = Vector3.up; float depth = 0f;
                    bool overlap = c is ConvexCollider convex
                        ? convex.ComputeMtv(b, out n, out depth)
                        : BoxMtv(b, o, downBias, out n, out depth);
                    if (!overlap || depth <= tolerance) continue;

                    any = true;
                    pos += n * (depth + 1E-04f);
                    if (n.y > 0.5f) _isGrounded = true; // popped up out of a floor: we are standing on it
                    b = BoundsAt(pos);
                }
                if (!any) break;
            }
            return pos;
        }

        /// <summary>Shortest push that takes box <paramref name="b"/> out of box <paramref name="o"/>.</summary>
        private static bool BoxMtv(Bounds b, Bounds o, float downBias, out Vector3 normal, out float depth)
        {
            normal = Vector3.up; depth = 0f;
            float best = float.MaxValue;
            Vector3 bestN = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float pushPos = o.max[i] - b.min[i]; // move +i to clear the far face
                float pushNeg = b.max[i] - o.min[i]; // move -i to clear the near face
                if (pushPos <= 0f || pushNeg <= 0f) return false;
                Vector3 a = Vector3.zero; a[i] = 1f;
                float costPos = pushPos, costNeg = pushNeg;
                if (i == 1) costNeg *= downBias; // prefer popping up out of a floor over dropping through it
                if (costPos < best) { best = costPos; bestN = a; depth = pushPos; }
                if (costNeg < best) { best = costNeg; bestN = -a; depth = pushNeg; }
            }
            if (bestN == Vector3.zero) return false;
            normal = bestN;
            return true;
        }

        /// <summary>True when the box overlaps any solid collider gathered for this move.</summary>
        private bool Overlapping(Bounds b)
        {
            const float tolerance = 0.005f;
            foreach (var c in _buffer)
            {
                Bounds o = c.bounds;
                if (!b.Intersects(o)) continue;
                if (c is ConvexCollider convex) { if (convex.ComputeMtv(b, out _, out float d) && d > tolerance) return true; continue; }
                if (BoxMtv(b, o, 1f, out _, out float depth) && depth > tolerance) return true;
            }
            return false;
        }

        /// <summary>Teleports without collision (for spawning / respawn).</summary>
        public void Warp(Vector3 position)
        {
            transform.position = position;
            _velocity = Vector3.zero;
            _hasSafeSpot = false; // the remembered spot belongs to wherever we came from
        }

        private Bounds BoundsAt(Vector3 pos)
        {
            Vector3 c = pos + center;
            float h = Mathf.Max(height, radius * 2f);
            // slightly shrunk so touching faces do not count as penetration
            float s = skinWidth * 0.5f;
            return new Bounds(c - new Vector3(radius - s, h * 0.5f - s, radius - s), c + new Vector3(radius - s, h * 0.5f - s, radius - s));
        }

        /// <summary>Sweeps along one axis in sub-steps of at most <see cref="MaxSubstep"/>, stopping at the first blocker.</summary>
        private Vector3 SlideAxisStepped(Vector3 pos, int axis, float amount, out bool blocked)
        {
            blocked = false;
            float a = Mathf.Abs(amount);
            if (a < 1E-09f) return pos;
            float maxStep = Mathf.Max(0.02f, MaxSubstep);
            int n = Mathf.Max(1, Mathf.CeilToInt(a / maxStep));
            float step = amount / n;
            for (int i = 0; i < n; i++)
            {
                pos = SlideAxis(pos, axis, step, out bool hit);
                LastSubsteps++;
                if (hit) { blocked = true; break; }
            }
            return pos;
        }

        /// <summary>
        /// Moves along one axis and pushes back out of anything hit.
        ///
        /// The push-back is always clamped to the span the step just covered: a blocked sweep may be cut short,
        /// down to standing still, but it can never end up *behind* where it started. That invariant is what makes
        /// the mover teleport-proof. The raw "how deep am I in this box" numbers are the other collider's own
        /// extents when the character is contained in it (sunk into an 8 m thick dock slab, or inside the rotated
        /// pier brush): on the dock, a 2 cm sink plus one 10 cm walk step resolved to a 94 m jump sideways into the
        /// void, which read as a random mid-air teleport followed by the endless fall/respawn loop. Leftover penetration is cleaned up by
        /// <see cref="Depenetrate"/>, which knows the real shortest way out.
        /// </summary>
        private Vector3 SlideAxis(Vector3 pos, int axis, float amount, out bool blocked)
        {
            blocked = false;
            if (Mathf.Abs(amount) < 1E-09f) return pos;
            float from = pos[axis], to = from + amount;
            float lo = Mathf.Min(from, to), hi = Mathf.Max(from, to);
            float back = amount > 0f ? -1f : 1f; // direction that undoes the step
            pos[axis] = to;
            Bounds b = BoundsAt(pos);
            for (int iter = 0; iter < 4; iter++)
            {
                bool any = false;
                foreach (var c in _buffer)
                {
                    Bounds o = c.bounds;
                    if (!b.Intersects(o)) continue;
                    float push;
                    if (c is ConvexCollider convex)
                    {
                        if (!convex.ComputeMtv(b, out Vector3 face, out _)) continue;
                        // A surface that does not face this axis cannot block motion along it: the floor under the
                        // player must not stop them walking, a wall they are brushing must not stop them falling.
                        // (The box path gets this for free from OverlapsOtherAxes.) The matching axis sweep and the
                        // depenetration pass own those contacts instead.
                        if (Mathf.Abs(face[axis]) < 0.3f) continue;
                        // rotated / sloped brush: distance to travel back along this axis until the shapes part
                        push = convex.SeparationAlong(b, axis, back);
                        if (push <= 0f) continue;
                    }
                    else
                    {
                        // only resolve against colliders that actually overlap on the other two axes by a margin
                        if (!OverlapsOtherAxes(b, o, axis)) continue;
                        push = amount > 0f ? b.max[axis] - o.min[axis] : o.max[axis] - b.min[axis];
                        if (push <= 0f) continue;
                    }
                    any = true; blocked = true;
                    if (push > float.MaxValue * 0.5f) pos[axis] = back > 0f ? hi : lo; // cannot separate this way: undo the step
                    else pos[axis] = Mathf.Clamp(pos[axis] + back * (push + 1E-04f), lo, hi);
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
                if (c is ConvexCollider convex) { if (convex.Overlaps(b)) return true; continue; }
                if (b.Intersects(o) && OverlapsOtherAxes(b, o, 1)) return true;
            }
            return false;
        }
    }
}
