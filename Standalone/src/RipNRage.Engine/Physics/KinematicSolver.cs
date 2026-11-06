using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    public enum ContactKind { Floor, Wall, Ceiling }

    /// <summary>One surface a move touched.</summary>
    public struct Contact
    {
        public Collider collider;
        public Vector3 point;
        /// <summary>Surface normal, pointing away from the collider.</summary>
        public Vector3 normal;
        public ContactKind kind;
        /// <summary>Fraction of the motion that was left when the contact happened (1 = touched at the very start).</summary>
        public float remainingFraction;
        public Transform transform => collider?.transform;
        public GameObject gameObject => collider?.gameObject;
        public bool isFloor => kind == ContactKind.Floor;
        public bool isWall => kind == ContactKind.Wall;
        public bool isCeiling => kind == ContactKind.Ceiling;
    }

    /// <summary>How <see cref="Physics.MoveAndSlide"/> behaves.</summary>
    public struct MoveSettings
    {
        /// <summary>Defaults of the kinematic movers (tunables.cfg [movers]).</summary>
        public static float DefaultFloorMaxAngle = 46f;
        public static int DefaultMaxSlides = 5;

        /// <summary>Which colliders block the move.</summary>
        public QueryFilter filter;
        /// <summary>Gap kept between the box and the surfaces it rests against.</summary>
        public float skin;
        /// <summary>How many times the motion may be redirected along a surface (walls, creases) before giving up.</summary>
        public int maxSlides;
        /// <summary>Steepest surface (degrees from horizontal) that counts as a floor; steeper ones are walls the box slides down.</summary>
        public float floorMaxAngle;
        /// <summary>Height of a ledge the box may climb in one move when it is on the ground (0 = no stairs).</summary>
        public float stepHeight;
        /// <summary>After a downward or horizontal move from the ground, pull the box down onto a floor this close (stairs down, slopes). 0 = off.</summary>
        public float snapLength;
        /// <summary>Motions shorter than this are treated as a stand-still (the ground probe still runs).</summary>
        public float minMove;
        /// <summary>Whether the box was on the ground before this move (enables step-up and snapping).</summary>
        public bool wasGrounded;

        public static MoveSettings Default => new MoveSettings
        {
            filter = QueryFilter.Everything, skin = 0f, maxSlides = DefaultMaxSlides, floorMaxAngle = DefaultFloorMaxAngle,
            stepHeight = 0f, snapLength = 0f, minMove = 1E-04f, wasGrounded = false,
        };

        /// <summary>Settings for a walking character: stairs of <paramref name="stepHeight"/>, snapping over the same distance.</summary>
        public static MoveSettings Character(QueryFilter filter, float floorMaxAngle = 0f, float stepHeight = 0.3f)
        {
            var s = Default;
            s.filter = filter; s.floorMaxAngle = floorMaxAngle > 0f ? floorMaxAngle : DefaultFloorMaxAngle; s.stepHeight = stepHeight; s.snapLength = stepHeight;
            return s;
        }
    }

    /// <summary>Outcome of <see cref="Physics.MoveAndSlide"/>. Reuse one instance per mover; nothing here allocates after the first call.</summary>
    public sealed class MoveResult
    {
        /// <summary>Centre of the box after the move.</summary>
        public Vector3 position;
        /// <summary>Centre before the move.</summary>
        public Vector3 start;
        /// <summary>Displacement actually made.</summary>
        public Vector3 motion => position - start;
        /// <summary>Part of the requested motion that could not be completed (after sliding).</summary>
        public Vector3 remainder;
        public bool isGrounded;
        public Vector3 groundNormal = Vector3.up;
        public Collider ground;
        public bool hitWall, hitCeiling;
        public CollisionFlags flags;
        /// <summary>The move climbed a step this high.</summary>
        public float steppedUp;
        /// <summary>The move was pulled down onto the ground by this much (snap).</summary>
        public float snappedDown;
        /// <summary>Trace iterations used.</summary>
        public int slides;
        /// <summary>Distance the box had to be pushed out of geometry it started in.</summary>
        public float depenetration;
        public bool startedSolid;
        /// <summary>Still overlapping something after the push-out attempts (badly embedded).</summary>
        public bool stuck;
        public readonly List<Contact> contacts = new List<Contact>(8);

        internal void Reset(Vector3 center)
        {
            position = start = center;
            remainder = Vector3.zero;
            isGrounded = false; groundNormal = Vector3.up; ground = null;
            hitWall = hitCeiling = false; flags = CollisionFlags.None;
            steppedUp = snappedDown = 0f;
            slides = 0; depenetration = 0f; startedSolid = stuck = false;
            contacts.Clear();
        }

        /// <summary>
        /// The velocity a body keeps after this move: floors cancel downward speed, walls and ceilings remove the
        /// component pointing into them (so a body running into a wall slides along it next step).
        /// </summary>
        public Vector3 ClipVelocity(Vector3 velocity)
        {
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c.kind == ContactKind.Floor) { if (velocity.y < 0f) velocity.y = 0f; continue; }
                float into = Vector3.Dot(velocity, c.normal);
                if (into < 0f) velocity -= c.normal * into;
            }
            if (isGrounded && velocity.y < 0f) velocity.y = 0f;
            return velocity;
        }
    }

    /// <summary>
    /// Collide-and-slide for axis-aligned boxes (Quake's PM_FlyMove lineage, with the floor handling of modern
    /// character controllers). Each iteration sweeps the box along what is left of the motion, stops at the first
    /// contact and redirects the remainder along the surface; floors keep horizontal speed (no slope drift), a
    /// second plane turns the motion into the crease between the two, a third stops it. Ledges up to
    /// <see cref="MoveSettings.stepHeight"/> are climbed by re-tracing from a raised position, and a grounded box
    /// is snapped back onto the floor afterwards. No sub-stepping is needed: the sweeps are continuous, so nothing
    /// tunnels whatever the frame time.
    /// </summary>
    public static class KinematicSolver
    {
        private const int MaxPlanes = 5;
        private static readonly Vector3[] _planes = new Vector3[MaxPlanes];
        private static readonly List<Collider> _candidates = new List<Collider>(64);
        private static int _depth; // guards the shared scratch buffers against re-entrant use

        public static void MoveAndSlide(PhysicsWorld world, Vector3 center, Vector3 halfExtents, Vector3 motion, MoveSettings s, MoveResult r) =>
            MoveAndSlide(world, CollisionShape.AxisAlignedBox(center, halfExtents), motion, s, r);

        /// <summary>Collide-and-slide for any convex mover shape (box, sphere, capsule).</summary>
        public static void MoveAndSlide(PhysicsWorld world, in CollisionShape shape, Vector3 motion, MoveSettings s, MoveResult r)
        {
            if (_depth > 0)
            {
                // re-entrant call (a collision callback moving another body): use private buffers
                MoveAndSlideCore(world, in shape, motion, s, r, new List<Collider>(32), new Vector3[MaxPlanes]);
                return;
            }
            _depth++;
            try { MoveAndSlideCore(world, in shape, motion, s, r, _candidates, _planes); }
            finally { _depth--; }
        }

        private static void MoveAndSlideCore(PhysicsWorld world, in CollisionShape shape, Vector3 motion, MoveSettings s, MoveResult r, List<Collider> candidates, Vector3[] planes)
        {
            CollisionShape cur = shape;
            r.Reset(cur.center);
            if (s.maxSlides <= 0) s.maxSlides = 1;
            float skin = s.skin > 0f ? s.skin : Physics.defaultSkin;
            float cosFloor = MathF.Cos(Mathf.Clamp(s.floorMaxAngle, 1f, 89f) * Mathf.Deg2Rad);
            float minMove = MathF.Max(s.minMove, 1E-06f);
            if (motion.sqrMagnitude < minMove * minMove) motion = Vector3.zero;

            // broadphase once for the whole move: everything the shape might touch while moving, stepping or snapping
            float reach = skin * 2f + MathF.Max(s.stepHeight, s.snapLength) + 0.05f;
            Bounds region = cur.bounds.Swept(motion).Expanded(reach);
            candidates.Clear();
            world.QueryBounds(region, s.filter, candidates);

            // 1. never start inside geometry
            r.depenetration = Depenetrate(candidates, ref cur, skin, out bool stillSolid);
            if (r.depenetration > 0f) r.startedSolid = true;
            r.stuck = stillSolid;
            if (r.depenetration > reach * 0.5f)
            {
                // a big push-out may have carried the shape outside the region gathered above: look again from where it is now
                region = cur.bounds.Swept(motion).Expanded(reach);
                candidates.Clear();
                world.QueryBounds(region, s.filter, candidates);
            }

            // 2. slide
            Vector3 remaining = motion;
            int planeCount = 0;
            bool groundedNow = false;
            for (int bump = 0; bump < s.maxSlides && remaining.sqrMagnitude > minMove * minMove; bump++)
            {
                r.slides++;
                PhysicsWorld.Sweep(candidates, in cur, remaining, skin, out ShapeHit hit);
                if (hit.startSolid)
                {
                    // pushed into something between traces (numerical drift): push out and retry once
                    float fixedUp = Depenetrate(candidates, ref cur, skin, out stillSolid);
                    r.depenetration += fixedUp;
                    if (stillSolid || fixedUp <= 0f) { r.stuck = stillSolid; break; }
                    continue;
                }
                if (hit.fraction > 0f)
                {
                    cur.center += remaining * hit.fraction;
                    planeCount = 0; // the planes of earlier contacts no longer constrain us once we moved away
                }
                if (!hit.hit) { remaining = Vector3.zero; break; }

                Vector3 n = hit.normal;
                ContactKind kind = Classify(n, cosFloor);
                remaining *= 1f - hit.fraction;

                // stairs: a wall while on the ground may be a ledge we can climb
                if (kind == ContactKind.Wall && s.stepHeight > 0f && (s.wasGrounded || groundedNow) && remaining.y <= 0.001f &&
                    TryStepUp(candidates, in cur, remaining, skin, cosFloor, s.stepHeight, out Vector3 stepped, out Vector3 stepRemaining, out Contact landing))
                {
                    r.steppedUp += stepped.y - cur.center.y;
                    cur.center = stepped;
                    remaining = stepRemaining;
                    groundedNow = true;
                    r.isGrounded = true; r.groundNormal = landing.normal; r.ground = landing.collider;
                    AddContact(r, landing, remaining.magnitude / MathF.Max(motion.magnitude, 1E-06f));
                    planeCount = 0;
                    continue;
                }

                AddContact(r, new Contact { collider = hit.collider, point = hit.point, normal = n, kind = kind }, remaining.magnitude / MathF.Max(motion.magnitude, 1E-06f));
                if (kind == ContactKind.Floor) { groundedNow = true; r.isGrounded = true; r.groundNormal = n; r.ground = hit.collider; }

                if (planeCount >= MaxPlanes) { remaining = Vector3.zero; break; }
                planes[planeCount++] = n;

                // redirect what is left so it runs along every plane touched since we last moved freely
                Vector3 clipped = remaining;
                int i;
                for (i = 0; i < planeCount; i++)
                {
                    clipped = Clip(remaining, planes[i], Classify(planes[i], cosFloor));
                    int j;
                    for (j = 0; j < planeCount; j++)
                        if (j != i && Vector3.Dot(clipped, planes[j]) < -1E-05f) break; // still heading into another plane
                    if (j == planeCount) break;
                }
                if (i == planeCount)
                {
                    if (planeCount != 2) { remaining = Vector3.zero; break; }
                    // wedged between two planes: move along the crease
                    Vector3 crease = Vector3.Cross(planes[0], planes[1]);
                    if (crease.sqrMagnitude < 1E-08f) { remaining = Vector3.zero; break; }
                    crease.Normalize();
                    clipped = crease * Vector3.Dot(crease, remaining);
                }
                remaining = clipped;
                // never turn back against the original intent (corner jitter)
                if (Vector3.Dot(remaining, motion) <= 0f) { remaining = Vector3.zero; break; }
            }
            r.remainder = remaining;

            // 3. ground. A floor touched *during* the move says nothing about where the shape ends (it may have walked
            // off an edge in the same move), so the final state comes from a probe just below the end position; a shape
            // that was walking is also snapped down onto a floor a little further away (stairs down, slope crests).
            // The probe is not a contact of the motion (a projectile skimming the floor must not "hit" it): it only
            // answers "is there a floor under us" — see isGrounded / groundNormal / ground.
            bool touchedFloor = r.isGrounded;
            r.isGrounded = false; r.ground = null; r.groundNormal = Vector3.up;
            float probe = skin * 2f + 0.02f;
            bool maySnap = s.snapLength > 0f && (s.wasGrounded || touchedFloor) && motion.y <= 1E-04f;
            float reachDown = maySnap ? MathF.Max(s.snapLength, probe) : probe;
            PhysicsWorld.Sweep(candidates, in cur, Vector3.down * reachDown, skin, out ShapeHit ground);
            if (ground.hit && !ground.startSolid && ground.normal.y >= cosFloor)
            {
                float drop = ground.fraction * reachDown;
                if (drop <= probe) { r.isGrounded = true; r.groundNormal = ground.normal; r.ground = ground.collider; }
                else if (maySnap)
                {
                    cur.center += Vector3.down * drop; r.snappedDown = drop;
                    r.isGrounded = true; r.groundNormal = ground.normal; r.ground = ground.collider;
                }
            }

            r.position = cur.center;
            if (r.isGrounded) r.flags |= CollisionFlags.Below;
            if (r.hitWall) r.flags |= CollisionFlags.Sides;
            if (r.hitCeiling) r.flags |= CollisionFlags.Above;
        }

        private static void AddContact(MoveResult r, Contact c, float remainingFraction)
        {
            c.remainingFraction = remainingFraction;
            r.contacts.Add(c);
            if (c.kind == ContactKind.Wall) r.hitWall = true;
            else if (c.kind == ContactKind.Ceiling) r.hitCeiling = true;
        }

        public static ContactKind Classify(Vector3 normal, float cosFloor)
        {
            if (normal.y >= cosFloor) return ContactKind.Floor;
            if (normal.y <= -cosFloor) return ContactKind.Ceiling;
            return ContactKind.Wall;
        }

        /// <summary>
        /// Redirects a motion along a surface. Walls and ceilings simply lose the component into the plane. Floors
        /// keep the *horizontal* motion and follow the slope with it — the vertical part (gravity, stick-to-ground
        /// forces) is dropped entirely, so a box standing on a slope does not creep downhill.
        /// </summary>
        public static Vector3 Clip(Vector3 v, Vector3 n, ContactKind kind)
        {
            if (kind == ContactKind.Floor)
            {
                if (n.y < 1E-04f) return Vector3.zero;
                Vector3 h = new Vector3(v.x, 0f, v.z);
                if (h.sqrMagnitude < 1E-12f) return Vector3.zero;
                h.y = -(n.x * h.x + n.z * h.z) / n.y; // the vector on the plane with the same horizontal projection
                return h;
            }
            float into = Vector3.Dot(v, n);
            if (into >= 0f) return v;
            return v - n * into;
        }

        /// <summary>Up, forward, down: climbs a ledge if the shape lands on a floor higher than where it was.</summary>
        private static bool TryStepUp(List<Collider> candidates, in CollisionShape shape, Vector3 remaining, float skin, float cosFloor, float stepHeight,
            out Vector3 newCenter, out Vector3 newRemaining, out Contact landing)
        {
            Vector3 center = shape.center;
            newCenter = center; newRemaining = remaining; landing = default;
            Vector3 horizontal = new Vector3(remaining.x, 0f, remaining.z);
            if (horizontal.sqrMagnitude < 1E-10f) return false;

            PhysicsWorld.Sweep(candidates, in shape, Vector3.up * stepHeight, skin, out ShapeHit up);
            if (up.startSolid) return false;
            float rise = stepHeight * up.fraction;
            if (rise < 0.01f) return false;
            CollisionShape raised = shape.Translated(Vector3.up * rise);

            PhysicsWorld.Sweep(candidates, in raised, horizontal, skin, out ShapeHit fwd);
            if (fwd.startSolid || fwd.fraction <= 1E-03f) return false;
            CollisionShape forward = raised.Translated(horizontal * fwd.fraction);

            float dropDistance = rise + skin * 2f;
            PhysicsWorld.Sweep(candidates, in forward, Vector3.down * dropDistance, skin, out ShapeHit down);
            if (!down.hit || down.startSolid || down.normal.y < cosFloor) return false;
            Vector3 landed = forward.center + Vector3.down * (dropDistance * down.fraction);
            if (landed.y <= center.y + 1E-03f) return false; // did not actually climb anything: the ordinary slide handles it

            newCenter = landed;
            newRemaining = horizontal * (1f - fwd.fraction);
            landing = new Contact { collider = down.collider, point = down.point, normal = down.normal, kind = ContactKind.Floor };
            return true;
        }

        /// <summary>Pushes an axis-aligned box out of every candidate it overlaps; returns the total distance moved.</summary>
        public static float Depenetrate(List<Collider> candidates, ref Vector3 center, Vector3 halfExtents, float skin, out bool stillSolid)
        {
            CollisionShape s = CollisionShape.AxisAlignedBox(center, halfExtents);
            float moved = Depenetrate(candidates, ref s, skin, out stillSolid);
            center = s.center;
            return moved;
        }

        /// <summary>
        /// Pushes the shape out of every candidate it overlaps along the true minimum translation vector, a few
        /// rounds so the push out of one solid does not end inside another. Returns the total distance moved.
        /// </summary>
        public static float Depenetrate(List<Collider> candidates, ref CollisionShape shape, float skin, out bool stillSolid)
        {
            const float tolerance = 0.0005f;
            const float downBias = 2f; // ejecting a character downwards out of the world is nearly always wrong
            float total = 0f;
            stillSolid = false;
            for (int iter = 0; iter < 6; iter++)
            {
                bool any = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!PhysicsWorld.Penetration(candidates[i], in shape, out Vector3 n, out float depth, downBias)) continue;
                    if (depth <= tolerance) continue;
                    shape.center += n * (depth + skin);
                    total += depth + skin;
                    any = true;
                }
                if (!any) return total;
            }
            // still overlapping after the rounds: report it
            for (int i = 0; i < candidates.Count; i++)
                if (PhysicsWorld.Penetration(candidates[i], in shape, out _, out float depth) && depth > tolerance) { stillSolid = true; break; }
            return total;
        }
    }
}
