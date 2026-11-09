using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>A touching point between two shapes, with the solver's per-point state.</summary>
    public struct ManifoldPoint
    {
        public Vector3 point;          // world, between the two surfaces
        public float depth;            // penetration (>= 0; negative = separated by up to the contact margin)
        /// <summary>Feature id of the contact (which vertex / edge pair produced it): the key for matching a point across steps.</summary>
        public int id;
        public Vector3 localA;         // anchor relative to body A's centre, in A's frame (warm-start matching, separation updates)
        public Vector3 localB;         // anchor in B's frame
        /// <summary>The point existed in the previous step (its impulses were carried over).</summary>
        public bool persistent;
        /// <summary>Steps this point has existed.</summary>
        public int age;
        /// <summary>The point was sliding (tangential speed above the solver's slip speed) at the start of the step: dynamic friction applies.</summary>
        public bool slipping;
        // solver state
        internal Vector3 rA, rB;
        internal Vector3 anchorA0, anchorB0;   // world anchors at the start of the step (separation updates)
        internal float normalMass, tangentMass0, tangentMass1;
        internal float baseSeparation;   // -depth at the start of the step
        internal float relativeVelocity; // normal speed before the step (restitution)
        internal float normalImpulse, tangentImpulse0, tangentImpulse1, maxNormalImpulse;
        internal float positionImpulse;  // NGS accumulator
    }

    /// <summary>Up to <see cref="MaxPoints"/> contact points sharing one normal between a rigid body and a collider (or another body).</summary>
    public sealed class Manifold
    {
        public const int MaxPoints = 8;
        public SolverBody bodyA;
        public SolverBody bodyB;        // null: B is static / a kinematic mover
        internal ColliderTable.Slot colliderA, colliderB;
        public Vector3 normal;         // from A to B
        public int count;
        public readonly ManifoldPoint[] points = new ManifoldPoint[MaxPoints];
        /// <summary>Combined surface properties of the pair.</summary>
        public float staticFriction, dynamicFriction, restitution;
        /// <summary>Dynamic friction (kept for callers that only know one coefficient).</summary>
        public float friction { get => dynamicFriction; set { dynamicFriction = value; staticFriction = value; } }
        /// <summary>Soft-contact spring settings used by <see cref="PositionCorrectionMode.Soft"/> (set by the solver from its tunables).</summary>
        public float hertz = 30f, dampingRatio = 10f;
        /// <summary>Steps this manifold has existed according to the contact cache (0 = new this step).</summary>
        public int age;
        internal Vector3 tangent0, tangent1;
        internal Vector3 posA0, posB0;
        internal Quaternion rotA0, rotB0;
        internal float biasRate, massScale, impulseScale;
        internal long key;
        internal int owner;            // worker pool the manifold came from
        internal int island = -1;      // island index in the current step

        public void Reset()
        {
            bodyA = bodyB = null; colliderA = colliderB = null; count = 0; normal = Vector3.up; age = 0;
        }

        internal void Add(Vector3 point, float depth, int id = 0)
        {
            if (count < MaxPoints) { points[count] = new ManifoldPoint { point = point, depth = depth, id = id }; count++; }
        }
    }

    /// <summary>
    /// Contact generation for the rigid-body solver: the separating-axis test between two convex polytopes (face
    /// normals of both plus every edge pair) followed by face clipping to get a stable multi-point manifold
    /// (Sutherland–Hodgman of the incident face against the reference face's side planes), the closest-points
    /// case for edge/edge contacts, and the sphere routines. Everything works on world-space shapes.
    /// </summary>
    public static class ContactGeneration
    {
        private enum AxisKind { FaceA, FaceB, Edge }

        // clipping scratch: one set per thread so the narrow phase can run on several (see RigidSolver.threads)
        [ThreadStatic] private static List<(Vector3 p, int id)> _polyAStorage, _polyBStorage;
        [ThreadStatic] private static List<(Vector3 p, float depth, int id)> _clippedStorage;
        private static List<(Vector3 p, int id)> _polyA => _polyAStorage ??= new List<(Vector3, int)>(16);
        private static List<(Vector3 p, int id)> _polyB => _polyBStorage ??= new List<(Vector3, int)>(16);
        private static List<(Vector3 p, float depth, int id)> _clipped => _clippedStorage ??= new List<(Vector3, float, int)>(16);
        private const int EdgeContactId = 0x40000000;

        /// <summary>Tolerances that make the SAT prefer face contacts over edge contacts (more stable manifolds).</summary>
        private const float LinearSlop = 0.005f;
        private const float RelativeFaceBias = 0.05f;
        /// <summary>Shapes this close still produce a contact (kept alive while resting; the solver treats the gap speculatively).</summary>
        public static float ContactMargin = 0.01f;
        /// <summary>
        /// Points kept per face manifold (4..8). Four — the deepest, the farthest from it and the two spanning the most area —
        /// hold a box as well as eight and halve the solver work of a resting pile.
        /// </summary>
        public static int MaxManifoldPoints = 4;
        /// <summary>The reference face stays on A unless B's face is better by this much (keeps manifolds from flipping between steps).</summary>
        private const float FaceSwitchTolerance = 0.001f;

        // ------------------------------------------------------------------ convex vs convex

        /// <summary>
        /// Contact manifold between two convex shapes. The normal points from A to B. With
        /// <paramref name="preferNormal"/> (a mesh triangle's normal, either sign) the contact uses that direction
        /// whenever its penetration is shallow, so bodies sliding over a tessellated surface do not catch on the
        /// edges between its triangles.
        /// </summary>
        public static bool ConvexVsConvex(ConvexShape a, ConvexShape b, Manifold m, Vector3? preferNormal = null)
        {
            m.count = 0;
            float bestSep = float.MinValue; AxisKind bestKind = AxisKind.FaceA; int bestIndex = -1, bestIndex2 = -1; Vector3 bestAxis = Vector3.up;
            float faceSep = float.MinValue;

            // faces of A
            for (int i = 0; i < a.faces.Length; i++)
            {
                Vector3 n = a.faces[i].normal;
                float sep = Vector3.Dot(n, b.Support(-n)) - a.faces[i].dist;
                if (sep > ContactMargin) return false;
                if (sep > bestSep) { bestSep = sep; bestKind = AxisKind.FaceA; bestIndex = i; bestAxis = n; }
            }
            // faces of B
            for (int i = 0; i < b.faces.Length; i++)
            {
                Vector3 n = b.faces[i].normal;
                float sep = Vector3.Dot(n, a.Support(-n)) - b.faces[i].dist;
                if (sep > ContactMargin) return false;
                if (sep > bestSep + FaceSwitchTolerance) { bestSep = sep; bestKind = AxisKind.FaceB; bestIndex = i; bestAxis = -n; }
            }
            faceSep = bestSep;
            // mesh triangles: a shallow contact along the triangle's normal wins over any other axis
            if (preferNormal.HasValue)
            {
                Vector3 pn = preferNormal.Value;
                int triFace = -1; float triSep = float.MinValue;
                for (int i = 0; i < b.faces.Length; i++)
                {
                    if (MathF.Abs(Vector3.Dot(b.faces[i].normal, pn)) < 0.999f) continue;
                    float sep = Vector3.Dot(b.faces[i].normal, a.Support(-b.faces[i].normal)) - b.faces[i].dist;
                    if (sep > triSep) { triSep = sep; triFace = i; }
                }
                if (triFace >= 0 && -triSep <= MathF.Max(-bestSep * 2f, 0.03f))
                {
                    GenerateFaceContacts(b, triFace, a, true, m);
                    return m.count > 0;
                }
            }
            // edge pairs — only those that build a face of the Minkowski difference can be the axis (Gauss map
            // test, Gregorius GDC 2013), which prunes a box pair from 144 candidates to a handful
            Vector3 edgeAxis = Vector3.zero; int edgeA = -1, edgeB = -1; float edgeSep = float.MinValue;
            bool prune = a.hasEdgeFaces && b.hasEdgeFaces;
            for (int i = 0; i < a.edges.Length; i++)
            {
                Vector3 pa = a.vertices[a.edges[i].a], qa = a.vertices[a.edges[i].b];
                Vector3 da = qa - pa;
                Vector3 na0 = Vector3.zero, na1 = Vector3.zero, bxa = Vector3.zero;
                if (prune)
                {
                    na0 = a.faces[a.edges[i].f0].normal; na1 = a.faces[a.edges[i].f1].normal;
                    bxa = Vector3.Cross(na1, na0);
                }
                for (int j = 0; j < b.edges.Length; j++)
                {
                    if (prune)
                    {
                        // B's arc is on the opposite hemisphere (Minkowski difference A − B): negate its normals
                        Vector3 nc = -b.faces[b.edges[j].f0].normal, nd = -b.faces[b.edges[j].f1].normal;
                        Vector3 dxc = Vector3.Cross(nd, nc);
                        float cba = Vector3.Dot(nc, bxa), dba = Vector3.Dot(nd, bxa);
                        float adc = Vector3.Dot(na0, dxc), bdc = Vector3.Dot(na1, dxc);
                        if (!(cba * dba < 0f && adc * bdc < 0f && cba * bdc > 0f)) continue;
                    }
                    Vector3 pb = b.vertices[b.edges[j].a], qb = b.vertices[b.edges[j].b];
                    Vector3 axis = Vector3.Cross(da, qb - pb);
                    float len = axis.magnitude;
                    if (len < 1E-06f) continue; // parallel edges: a face axis covers them
                    axis /= len;
                    if (Vector3.Dot(axis, (pa + qa) * 0.5f - a.center) < 0f) axis = -axis; // away from A
                    a.Project(axis, out _, out float maxA);
                    b.Project(axis, out float minB, out _);
                    float sep = minB - maxA;
                    if (sep > ContactMargin) return false;
                    if (sep > edgeSep) { edgeSep = sep; edgeAxis = axis; edgeA = i; edgeB = j; }
                }
            }
            if (edgeA >= 0 && edgeSep > faceSep + LinearSlop + RelativeFaceBias * MathF.Abs(faceSep))
            {
                bestKind = AxisKind.Edge; bestSep = edgeSep; bestAxis = edgeAxis; bestIndex = edgeA; bestIndex2 = edgeB;
            }

            switch (bestKind)
            {
                case AxisKind.FaceA: GenerateFaceContacts(a, bestIndex, b, false, m); break;
                case AxisKind.FaceB: GenerateFaceContacts(b, bestIndex, a, true, m); break;
                default:
                {
                    ClosestPointsOnSegments(a.vertices[a.edges[bestIndex].a], a.vertices[a.edges[bestIndex].b],
                        b.vertices[b.edges[bestIndex2].a], b.vertices[b.edges[bestIndex2].b], out Vector3 ca, out Vector3 cb);
                    m.normal = bestAxis;
                    m.Add((ca + cb) * 0.5f, -bestSep, EdgeContactId | (bestIndex << 12) | (bestIndex2 & 0xFFF));
                    break;
                }
            }
            return m.count > 0;
        }

        /// <summary>
        /// Clips the incident face of <paramref name="inc"/> (the one most facing the reference face) against the
        /// reference face's side planes and keeps what lies below the reference face. <paramref name="refIsB"/>
        /// tells which shape is the manifold's A so the normal ends up pointing from A to B.
        /// </summary>
        private static void GenerateFaceContacts(ConvexShape refShape, int refFace, ConvexShape inc, bool refIsB, Manifold m)
        {
            Vector3 n = refShape.faces[refFace].normal;
            float dist = refShape.faces[refFace].dist;
            // incident face: most anti-parallel to the reference normal
            int incFace = 0; float bestDot = float.MaxValue;
            for (int i = 0; i < inc.faces.Length; i++)
            {
                float d = Vector3.Dot(inc.faces[i].normal, n);
                if (d < bestDot) { bestDot = d; incFace = i; }
            }
            _polyA.Clear();
            var incIdx = inc.faces[incFace].indices;
            for (int i = 0; i < incIdx.Length; i++) _polyA.Add((inc.vertices[incIdx[i]], (refIsB ? 0x1000 : 0) | incIdx[i]));

            // clip against every side plane of the reference face
            Vector3 fc = refShape.FaceCenter(refFace);
            var refIdx = refShape.faces[refFace].indices;
            var input = _polyA; var output = _polyB;
            for (int i = 0; i < refIdx.Length && input.Count > 0; i++)
            {
                Vector3 va = refShape.vertices[refIdx[i]], vb = refShape.vertices[refIdx[(i + 1) % refIdx.Length]];
                Vector3 side = Vector3.Cross(vb - va, n);
                if (side.sqrMagnitude < 1E-12f) continue;
                side.Normalize();
                if (Vector3.Dot(side, va - fc) < 0f) side = -side; // outward from the face
                float sideDist = Vector3.Dot(side, va);
                output.Clear();
                for (int k = 0; k < input.Count; k++)
                {
                    var (p, pid) = input[k];
                    var (q, qid) = input[(k + 1) % input.Count];
                    float dp = Vector3.Dot(side, p) - sideDist, dq = Vector3.Dot(side, q) - sideDist;
                    if (dp <= 0f) output.Add((p, pid));
                    // a clipped edge makes a new feature: incident edge (by its first vertex id) cut by reference side i
                    if ((dp < 0f && dq > 0f) || (dp > 0f && dq < 0f)) output.Add((p + (q - p) * (dp / (dp - dq)), 0x2000 | ((pid & 0xFF) << 4) | (i & 0xF)));
                }
                var t = input; input = output; output = t;
            }

            _clipped.Clear();
            for (int i = 0; i < input.Count; i++)
            {
                float sep = Vector3.Dot(n, input[i].p) - dist;
                if (sep <= ContactMargin) _clipped.Add((input[i].p - n * (sep * 0.5f), -sep, input[i].id));
            }
            m.normal = refIsB ? -n : n;
            m.count = 0;
            int keep = Math.Clamp(MaxManifoldPoints, 4, Manifold.MaxPoints);
            if (_clipped.Count <= keep) { for (int i = 0; i < _clipped.Count; i++) m.Add(_clipped[i].p, _clipped[i].depth, _clipped[i].id); return; }

            // keep four: the deepest, the farthest from it, and the two that span the most area either side
            int i0 = 0;
            for (int i = 1; i < _clipped.Count; i++) if (_clipped[i].depth > _clipped[i0].depth) i0 = i;
            int i1 = -1; float best = -1f;
            for (int i = 0; i < _clipped.Count; i++) { if (i == i0) continue; float d = (_clipped[i].p - _clipped[i0].p).sqrMagnitude; if (d > best) { best = d; i1 = i; } }
            int i2 = -1, i3 = -1; float bestPos = 0f, bestNeg = 0f;
            Vector3 e = _clipped[i1].p - _clipped[i0].p;
            for (int i = 0; i < _clipped.Count; i++)
            {
                if (i == i0 || i == i1) continue;
                float area = Vector3.Dot(Vector3.Cross(e, _clipped[i].p - _clipped[i0].p), n);
                if (area > bestPos) { bestPos = area; i2 = i; }
                if (area < bestNeg) { bestNeg = area; i3 = i; }
            }
            m.Add(_clipped[i0].p, _clipped[i0].depth, _clipped[i0].id);
            m.Add(_clipped[i1].p, _clipped[i1].depth, _clipped[i1].id);
            if (i2 >= 0) m.Add(_clipped[i2].p, _clipped[i2].depth, _clipped[i2].id);
            if (i3 >= 0) m.Add(_clipped[i3].p, _clipped[i3].depth, _clipped[i3].id);
        }

        /// <summary>Closest points between two segments (Ericson 5.1.9).</summary>
        public static void ClosestPointsOnSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float eps = 1E-09f;
            if (a <= eps && e <= eps) { c1 = p1; c2 = p2; return; }
            if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        }

        // ------------------------------------------------------------------ rounded shapes (sphere / capsule) through GJK

        /// <summary>
        /// A rounded shape (sphere or capsule) A against a polytope B; normal from A to B. Capsules get up to two
        /// points (their end spheres against the polytope's support plane) so they rest flat without rocking.
        /// </summary>
        public static bool RoundedVsConvex(in CollisionShape a, ConvexShape b, Manifold m)
        {
            m.count = 0;
            if (a.type == ShapeType.Sphere) return SphereVsConvex(a.center, a.radius, b, m);
            CollisionShape pb = CollisionShape.Polytope(b);
            if (!ConvexDistance.Distance(in a, in pb, out DistanceResult r))
            {
                // the core segment crosses the polytope: treat the capsule as a sphere at its centre (deep, rare)
                return SphereVsConvex(a.center, a.radius, b, m);
            }
            if (r.distance > ContactMargin) return false;
            Vector3 n = r.normal;                       // from B's surface to A
            m.normal = -n;
            float sB = Vector3.Dot(n, b.Support(n));    // B's support plane along n
            Vector3 axis = a.Axis * a.halfHeight;
            AddRoundedEnd(m, a.center + axis, a.radius, n, sB, 1);
            AddRoundedEnd(m, a.center - axis, a.radius, n, sB, 2);
            if (m.count == 0) m.Add((r.pointA + r.pointB) * 0.5f, -r.distance, 3);
            return m.count > 0;
        }

        private static void AddRoundedEnd(Manifold m, Vector3 end, float radius, Vector3 n, float sB, int id)
        {
            float sep = Vector3.Dot(n, end) - radius - sB;
            if (sep > ContactMargin) return;
            m.Add(end - n * (radius + sep * 0.5f), -sep, id);
        }

        /// <summary>Two rounded shapes (sphere / capsule pairs); normal from A to B. Parallel capsules get their end contacts too.</summary>
        public static bool RoundedVsRounded(in CollisionShape a, in CollisionShape b, Manifold m)
        {
            m.count = 0;
            if (a.type == ShapeType.Sphere && b.type == ShapeType.Sphere) return SphereVsSphere(a.center, a.radius, b.center, b.radius, m);
            if (!ConvexDistance.Distance(in a, in b, out DistanceResult r))
            {
                Vector3 d = b.center - a.center;
                float dist = d.magnitude;
                m.normal = dist > 1E-06f ? d / dist : Vector3.up;
                m.Add((a.center + b.center) * 0.5f, a.radius + b.radius - dist);
                return true;
            }
            if (r.distance > ContactMargin) return false;
            m.normal = -r.normal;
            m.Add((r.pointA + r.pointB) * 0.5f, -r.distance, 0);
            // capsule ends resting along the other shape (parallel capsules, capsule on a big sphere)
            if (a.type == ShapeType.Capsule) AddCapsuleEnds(in a, in b, m, false);
            if (b.type == ShapeType.Capsule) AddCapsuleEnds(in b, in a, m, true);
            return m.count > 0;
        }

        private static void AddCapsuleEnds(in CollisionShape capsule, in CollisionShape other, Manifold m, bool flip)
        {
            Vector3 axis = capsule.Axis * capsule.halfHeight;
            for (int k = -1; k <= 1; k += 2)
            {
                CollisionShape end = CollisionShape.Sphere(capsule.center + axis * k, capsule.radius);
                if (!ConvexDistance.Distance(in end, in other, out DistanceResult r) || r.distance > ContactMargin) continue;
                Vector3 p = (r.pointA + r.pointB) * 0.5f;
                bool dup = false;
                for (int i = 0; i < m.count; i++) if ((m.points[i].point - p).sqrMagnitude < 1E-04f) { dup = true; break; }
                if (!dup) m.Add(p, -r.distance, (flip ? 4 : 0) | (k < 0 ? 1 : 2));
            }
        }

        // ------------------------------------------------------------------ spheres

        /// <summary>Sphere A against a convex shape B; normal from A to B.</summary>
        public static bool SphereVsConvex(Vector3 center, float radius, ConvexShape b, Manifold m)
        {
            m.count = 0;
            Vector3 q = b.ClosestPoint(center, out Vector3 outward, out float distance, out bool inside);
            float depth = inside ? radius + distance : radius - distance;
            if (depth <= -ContactMargin) return false;
            m.normal = -outward;             // from the sphere towards the shape
            m.Add(q - outward * (depth * 0.5f), depth);
            return true;
        }

        /// <summary>Sphere A against sphere B.</summary>
        public static bool SphereVsSphere(Vector3 ca, float ra, Vector3 cb, float rb, Manifold m)
        {
            m.count = 0;
            Vector3 d = cb - ca;
            float dist = d.magnitude;
            float depth = ra + rb - dist;
            if (depth <= -ContactMargin) return false;
            Vector3 n = dist > 1E-06f ? d / dist : Vector3.up;
            m.normal = n;
            m.Add(ca + n * (ra - depth * 0.5f), depth, 0);
            return true;
        }
    }
}
