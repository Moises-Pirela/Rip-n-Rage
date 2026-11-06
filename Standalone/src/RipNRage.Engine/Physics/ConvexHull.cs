using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// Convex polyhedron stored as outward-facing planes (a point is inside when it is behind every plane).
    ///
    /// This is the one narrow-phase primitive of the engine: every solid is a hull (brushes are, rotated boxes
    /// become one, axis-aligned boxes are the degenerate six-plane case) and every mover is an axis-aligned box.
    /// Sweeping a box against a hull is a ray cast through the hull's planes pushed outwards by the box's extents —
    /// the Minkowski sum, the trick Quake's collision used. For that to be *exact* at edges and corners the hull
    /// carries extra <b>bevel planes</b> (the six axis-aligned planes of its bounding box plus one plane per
    /// edge/axis pair, see <see cref="FromPlanes"/>); with them the expanded planes are precisely the faces of the
    /// Minkowski sum, so the trace returns the true time of impact and the true minimum translation vector.
    /// </summary>
    public sealed class ConvexHull
    {
        /// <summary>All planes, the real faces first, then the bevels.</summary>
        public Plane[] planes = Array.Empty<Plane>();
        /// <summary>Number of real face planes at the start of <see cref="planes"/> (raycasts only need those).</summary>
        public int faceCount;
        public Bounds bounds;

        /// <summary>Tolerance inside which a box start position still counts as "outside" a plane (metres).</summary>
        public const float SolidTolerance = 0.001f;

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Builds a hull from its face planes and the vertices of its polygons (duplicates allowed — the vertex
        /// soup the level format produces is fine). With <paramref name="addBevels"/> the axial and edge bevel
        /// planes are generated so box sweeps are exact.
        /// </summary>
        public static ConvexHull FromPlanes(Plane[] facePlanes, Vector3[] vertices, bool addBevels = true)
        {
            var hull = new ConvexHull();
            var list = new List<Plane>(facePlanes.Length + 6);
            foreach (var p in facePlanes)
            {
                float m = p.normal.magnitude;
                if (m < 1E-6f) continue;
                list.Add(m == 1f ? p : new Plane(p.normal / m, p.dist / m));
            }
            hull.faceCount = list.Count;

            // distinct vertices (the polygons share their corners)
            var verts = new List<Vector3>(vertices.Length);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            foreach (var v in vertices)
            {
                bool dup = false;
                for (int i = 0; i < verts.Count; i++)
                    if ((verts[i] - v).sqrMagnitude < 1E-08f) { dup = true; break; }
                if (dup) continue;
                verts.Add(v);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
            }
            if (verts.Count == 0) { min = max = Vector3.zero; }
            hull.bounds = new Bounds(min, max);

            if (addBevels && verts.Count >= 3)
            {
                // axial bevels: the six bounding-box planes
                for (int axis = 0; axis < 3; axis++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 n = Vector3.zero; n[axis] = sign;
                        if (HasPlane(list, n)) continue;
                        list.Add(new Plane(n, sign > 0 ? max[axis] : -min[axis]));
                    }

                // edge bevels: for every edge shared by two faces and every world axis, the plane that contains
                // the edge and is parallel to the axis (the faces of the Minkowski sum that come from edge/edge contacts)
                int faces = hull.faceCount;
                for (int i = 0; i < faces; i++)
                    for (int j = i + 1; j < faces; j++)
                    {
                        if (!SharedEdge(list[i], list[j], verts, out Vector3 a, out Vector3 b)) continue;
                        Vector3 edge = (b - a).normalized;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            Vector3 ax = Vector3.zero; ax[axis] = 1f;
                            Vector3 bn = Vector3.Cross(edge, ax);
                            if (bn.sqrMagnitude < 1E-06f) continue; // edge parallel to the axis: the axial bevel covers it
                            bn.Normalize();
                            for (int sign = -1; sign <= 1; sign += 2)
                            {
                                Vector3 n = bn * sign;
                                if (HasPlane(list, n)) continue;
                                float d = float.MinValue;
                                foreach (var v in verts) d = MathF.Max(d, Vector3.Dot(n, v));
                                // only a supporting plane that actually touches the hull along this edge is useful
                                if (Vector3.Dot(n, a) < d - 1E-03f || Vector3.Dot(n, b) < d - 1E-03f) continue;
                                list.Add(new Plane(n, d));
                            }
                        }
                    }
            }
            hull.planes = list.ToArray();
            return hull;
        }

        /// <summary>Hull of an oriented box: <paramref name="localToWorld"/> applied to a box of <paramref name="size"/> around <paramref name="center"/>.</summary>
        public static ConvexHull FromBox(Matrix4x4 localToWorld, Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 wc = localToWorld.MultiplyPoint3x4(center);
            var planes = new Plane[6];
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 unit = Vector3.zero; unit[axis] = 1f;
                Vector3 a = localToWorld.MultiplyVector(unit); // scaled world axis
                Vector3 n = a.normalized;
                if (n == Vector3.zero) n = unit;
                Vector3 face = a * h[axis];
                planes[axis * 2] = new Plane(n, Vector3.Dot(n, wc + face));
                planes[axis * 2 + 1] = new Plane(-n, Vector3.Dot(-n, wc - face));
            }
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = localToWorld.MultiplyPoint3x4(center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            return FromPlanes(planes, corners, true);
        }

        /// <summary>
        /// Hull of a single triangle: a double-sided surface of zero thickness (its two faces, three side planes
        /// and the bevels), which is how a box is swept against a triangle mesh (Quake 3's patch collision).
        /// </summary>
        public static ConvexHull FromTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (n == Vector3.zero) n = Vector3.up;
            float d = Vector3.Dot(n, a);
            var planes = new Plane[5];
            planes[0] = new Plane(n, d);
            planes[1] = new Plane(-n, -d);
            planes[2] = SidePlane(a, b, c, n);
            planes[3] = SidePlane(b, c, a, n);
            planes[4] = SidePlane(c, a, b, n);
            return FromPlanes(planes, new[] { a, b, c }, true);
        }

        /// <summary>Plane through edge a-b, perpendicular to the triangle, facing away from the third vertex.</summary>
        private static Plane SidePlane(Vector3 a, Vector3 b, Vector3 other, Vector3 n)
        {
            Vector3 s = Vector3.Cross(b - a, n).normalized;
            if (s == Vector3.zero) s = Vector3.Cross(b - a, Vector3.up).normalized;
            if (Vector3.Dot(s, other - a) > 0f) s = -s;
            return new Plane(s, Vector3.Dot(s, a));
        }

        /// <summary>Hull of an axis-aligned box (six planes, no bevels needed).</summary>
        public static ConvexHull FromBounds(Bounds b)
        {
            var hull = new ConvexHull { faceCount = 6, bounds = b };
            hull.planes = new[]
            {
                new Plane(Vector3.right, b.max.x), new Plane(Vector3.left, -b.min.x),
                new Plane(Vector3.up, b.max.y), new Plane(Vector3.down, -b.min.y),
                new Plane(Vector3.forward, b.max.z), new Plane(Vector3.back, -b.min.z),
            };
            return hull;
        }

        private static bool HasPlane(List<Plane> list, Vector3 n)
        {
            for (int i = 0; i < list.Count; i++)
                if (Vector3.Dot(list[i].normal, n) > 0.999f) return true;
            return false;
        }

        /// <summary>Two distinct vertices lying on both planes = the planes share an edge.</summary>
        private static bool SharedEdge(Plane p, Plane q, List<Vector3> verts, out Vector3 a, out Vector3 b)
        {
            const float eps = 1E-03f;
            a = b = Vector3.zero;
            int found = 0;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                if (MathF.Abs(p.Distance(v)) > eps || MathF.Abs(q.Distance(v)) > eps) continue;
                if (found == 0) { a = v; found = 1; }
                else if ((v - a).sqrMagnitude > 1E-06f)
                {
                    // keep the two farthest apart so the edge direction is well conditioned
                    if (found == 1 || (v - a).sqrMagnitude > (b - a).sqrMagnitude) b = v;
                    found = 2;
                }
            }
            return found == 2;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Half-width of a box with half extents <paramref name="e"/> projected on <paramref name="n"/>.</summary>
        public static float Support(Vector3 n, Vector3 e) => MathF.Abs(n.x) * e.x + MathF.Abs(n.y) * e.y + MathF.Abs(n.z) * e.z;

        public bool Contains(Vector3 p)
        {
            for (int i = 0; i < planes.Length; i++)
                if (planes[i].Distance(p) > 0f) return false;
            return true;
        }

        /// <summary>Ray against the real faces (slab test). No hit when the origin is inside.</summary>
        public bool Raycast(Ray ray, float maxDistance, out float distance, out Vector3 normal)
        {
            distance = 0f; normal = Vector3.zero;
            float tEnter = 0f, tExit = maxDistance;
            bool entered = false;
            for (int i = 0; i < faceCount; i++)
            {
                Plane p = planes[i];
                float denom = Vector3.Dot(p.normal, ray.direction);
                float dist = p.Distance(ray.origin);
                if (MathF.Abs(denom) < 1E-08f) { if (dist > 1E-05f) return false; continue; }
                float t = -dist / denom;
                if (denom < 0f) { if (t > tEnter) { tEnter = t; normal = p.normal; entered = true; } }
                else if (t < tExit) tExit = t;
                if (tEnter > tExit) return false;
            }
            if (!entered) return false;
            distance = tEnter;
            return true;
        }

        /// <summary>True when an axis-aligned box (centre <paramref name="c"/>, half extents <paramref name="e"/>) overlaps the hull.</summary>
        public bool OverlapsBox(Vector3 c, Vector3 e)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                Plane p = planes[i];
                if (p.Distance(c) - Support(p.normal, e) >= 0f) return false;
            }
            return true;
        }

        /// <summary>
        /// Minimum translation that separates the box from the hull: the expanded plane the box centre is closest
        /// to clearing. <paramref name="normal"/> points out of the hull. Pushing downwards is charged
        /// <paramref name="downBias"/> times more so a character sunk into a floor pops up rather than dropping through.
        /// </summary>
        public bool BoxPenetration(Vector3 c, Vector3 e, out Vector3 normal, out float depth, float downBias = 1f)
        {
            normal = Vector3.up; depth = 0f;
            float bestCost = float.MaxValue;
            bool found = false;
            for (int i = 0; i < planes.Length; i++)
            {
                Plane p = planes[i];
                float d = p.Distance(c) - Support(p.normal, e); // < 0 while behind the expanded plane
                if (d >= 0f) return false;
                float cost = -d;
                if (p.normal.y < -0.5f) cost *= downBias;
                if (cost < bestCost) { bestCost = cost; normal = p.normal; depth = -d; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Sweeps an axis-aligned box (half extents <paramref name="e"/>) whose centre moves from
        /// <paramref name="start"/> to <paramref name="end"/> through the hull (Quake's ClipBoxToBrush).
        /// <paramref name="fraction"/> is the current best and is lowered to this hull's time of impact when it is
        /// earlier; the box is stopped <paramref name="skin"/> short of the surface so the next trace does not start
        /// inside it. Returns true when the trace was shortened; <paramref name="startSolid"/> is set when the box
        /// already overlaps the hull at <paramref name="start"/> (fraction 0, <paramref name="plane"/> = the
        /// penetration plane).
        /// </summary>
        public bool TraceBox(Vector3 e, Vector3 start, Vector3 end, float skin, ref float fraction, out Plane plane, out bool startSolid)
        {
            plane = default; startSolid = false;
            float enter = -1f, leave = 1f;
            bool startOut = false;
            Plane clip = default;
            for (int i = 0; i < planes.Length; i++)
            {
                Plane p = planes[i];
                float dist = p.dist + Support(p.normal, e);
                float d1 = Vector3.Dot(p.normal, start) - dist;
                float d2 = Vector3.Dot(p.normal, end) - dist;
                if (d1 > -SolidTolerance) startOut = true;
                // in front of this plane and not reaching it: the plane separates the whole motion from the hull
                if (d1 > 0f && (d2 >= skin || d2 >= d1)) return false;
                if (d1 <= 0f && d2 <= 0f) continue;
                if (d1 > d2)
                {
                    float f = (d1 - skin) / (d1 - d2); // entering: stop `skin` short
                    if (f < 0f) f = 0f;
                    if (f > enter) { enter = f; clip = p; }
                }
                else
                {
                    float f = (d1 + skin) / (d1 - d2); // leaving
                    if (f > 1f) f = 1f;
                    if (f < leave) leave = f;
                }
            }
            if (!startOut)
            {
                startSolid = true;
                fraction = 0f;
                if (BoxPenetration(start, e, out Vector3 n, out float depth)) plane = new Plane(n, Vector3.Dot(n, start) - depth);
                return true;
            }
            if (enter < leave && enter > -1f && enter < fraction)
            {
                fraction = enter;
                plane = clip;
                return true;
            }
            return false;
        }
    }
}
