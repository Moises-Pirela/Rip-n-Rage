using System;

namespace RipNRage.Engine
{
    /// <summary>Closest features of two shapes.</summary>
    public struct DistanceResult
    {
        /// <summary>Gap between the rounded surfaces (negative = overlapping by that much; see <see cref="coresIntersect"/> for deep overlaps).</summary>
        public float distance;
        /// <summary>Closest point on the surface of A / B (undefined when the cores intersect).</summary>
        public Vector3 pointA, pointB;
        /// <summary>Unit direction from B's surface to A's surface (B's outward normal at the contact).</summary>
        public Vector3 normal;
        /// <summary>The polytopal cores themselves intersect: the shapes overlap deeply and no closest points exist.</summary>
        public bool coresIntersect;
        public bool overlapping => coresIntersect || distance <= 0f;
    }

    /// <summary>Result of a continuous sweep of one shape against another.</summary>
    public struct SweepResult
    {
        /// <summary>Fraction of the motion at which the shapes come within the skin (1 = no contact).</summary>
        public float fraction;
        /// <summary>Outward normal of the shape that was hit.</summary>
        public Vector3 normal;
        /// <summary>Point on the surface of the shape that was hit.</summary>
        public Vector3 point;
        public bool hit;
        public bool startSolid;
    }

    /// <summary>
    /// Exact distance, overlap and continuous sweeps between any two <see cref="CollisionShape"/>s.
    ///
    /// Distance is GJK (Gilbert–Johnson–Keerthi) run on the polytopal cores only — points, segments, box corners,
    /// brush vertices — with the rounding radii subtracted afterwards, which keeps the simplex arithmetic on flat
    /// features and makes it robust; the witness points come from the barycentric coordinates of the final
    /// simplex. Sweeps use <b>conservative advancement</b>: advance along the motion by the current distance over
    /// the closing speed until the shapes are within the skin. That gives exact times of impact for every pair
    /// (sphere, capsule, oriented box, brush, triangle) and is the basis of the continuous collision of rigid
    /// bodies and of non-box movers.
    /// </summary>
    public static class ConvexDistance
    {
        private const int MaxIterations = 64;

        private struct Vertex { public Vector3 w, a, b; }

        // the simplex (at most 4 vertices) and its barycentric weights — scratch, one set per thread
        [ThreadStatic] private static Vertex[] _sStorage;
        [ThreadStatic] private static float[] _baryStorage;
        [ThreadStatic] private static int _count;
        private static Vertex[] _s => _sStorage ??= new Vertex[4];
        private static float[] _bary => _baryStorage ??= new float[4];

        // ------------------------------------------------------------------ distance

        /// <summary>Distance between the rounded shapes with witness points; false when the cores intersect (deep overlap).</summary>
        public static bool Distance(in CollisionShape a, in CollisionShape b, out DistanceResult r)
        {
            r = default;
            if (!CoreDistance(in a, in b, out float d, out Vector3 pa, out Vector3 pb, out Vector3 n))
            {
                r.coresIntersect = true; r.distance = float.NegativeInfinity; r.normal = Vector3.up;
                return false;
            }
            float ra = a.Radius, rb = b.Radius;
            r.distance = d - ra - rb;
            r.normal = n;
            r.pointA = pa - n * ra;
            r.pointB = pb + n * rb;
            return true;
        }

        /// <summary>True when the rounded shapes overlap (touching counts).</summary>
        public static bool Overlaps(in CollisionShape a, in CollisionShape b)
        {
            if (!CoreDistance(in a, in b, out float d, out _, out _, out _)) return true;
            return d <= a.Radius + b.Radius;
        }

        /// <summary>GJK between the cores: distance and closest points; false when they intersect.</summary>
        public static bool CoreDistance(in CollisionShape a, in CollisionShape b, out float distance, out Vector3 pointA, out Vector3 pointB, out Vector3 normal)
        {
            distance = 0f; pointA = pointB = Vector3.zero; normal = Vector3.up;
            Vector3 dir = a.center - b.center;
            if (dir.sqrMagnitude < 1E-12f) dir = Vector3.right;
            _count = 1;
            Support(in a, in b, dir, out _s[0]);
            _bary[0] = 1f;
            Vector3 closest = _s[0].w;

            for (int it = 0; it < MaxIterations; it++)
            {
                float c2 = closest.sqrMagnitude;
                if (c2 < 1E-14f) return false; // origin on the simplex: intersecting
                Vector3 d = -closest;
                Support(in a, in b, d, out Vertex v);
                // no progress towards the origin: converged
                if (Vector3.Dot(v.w, d) - Vector3.Dot(closest, d) <= 1E-07f * MathF.Max(1f, c2)) break;
                bool dup = false;
                for (int i = 0; i < _count; i++) if ((v.w - _s[i].w).sqrMagnitude < 1E-18f) { dup = true; break; }
                if (dup) break;
                _s[_count++] = v;

                if (_count == 2)
                {
                    closest = ClosestSegment(_s[0].w, _s[1].w, out float u0, out float u1, out int f);
                    if (f == 0) { _count = 1; _bary[0] = 1f; }
                    else if (f == 1) { _s[0] = _s[1]; _count = 1; _bary[0] = 1f; }
                    else { _bary[0] = u0; _bary[1] = u1; }
                }
                else if (_count == 3)
                {
                    closest = ReduceTriangle(0, 1, 2);
                }
                else
                {
                    if (OriginInTetrahedron(_s[0].w, _s[1].w, _s[2].w, _s[3].w)) return false;
                    // the closest of the four faces
                    float best = float.MaxValue; int bi = 0, bj = 1, bk = 2;
                    for (int f = 0; f < 4; f++)
                    {
                        int i = TetraFaces[f, 0], j = TetraFaces[f, 1], k = TetraFaces[f, 2];
                        Vector3 p = ClosestTriangle(_s[i].w, _s[j].w, _s[k].w, out _, out _, out _, out _);
                        float d2 = p.sqrMagnitude;
                        if (d2 < best) { best = d2; bi = i; bj = j; bk = k; }
                    }
                    Vertex vi = _s[bi], vj = _s[bj], vk = _s[bk];
                    _s[0] = vi; _s[1] = vj; _s[2] = vk; _count = 3;
                    closest = ReduceTriangle(0, 1, 2);
                }
            }

            Vector3 pa = Vector3.zero, pb = Vector3.zero;
            for (int i = 0; i < _count; i++) { pa += _s[i].a * _bary[i]; pb += _s[i].b * _bary[i]; }
            float dist = closest.magnitude;
            if (dist < 1E-09f) return false;
            distance = dist;
            pointA = pa; pointB = pb;
            normal = closest / dist;
            return true;
        }

        private static readonly int[,] TetraFaces = { { 0, 1, 2 }, { 0, 1, 3 }, { 0, 2, 3 }, { 1, 2, 3 } };

        private static void Support(in CollisionShape a, in CollisionShape b, Vector3 dir, out Vertex v)
        {
            v.a = a.Support(dir);
            v.b = b.Support(-dir);
            v.w = v.a - v.b;
        }

        /// <summary>Closest point of the triangle _s[i], _s[j], _s[k] to the origin; the simplex is reduced to the feature that holds it.</summary>
        private static Vector3 ReduceTriangle(int i, int j, int k)
        {
            Vector3 p = ClosestTriangle(_s[i].w, _s[j].w, _s[k].w, out float u, out float v, out float w, out int feature);
            Vertex a = _s[i], b = _s[j], c = _s[k];
            switch (feature)
            {
                case 0: _s[0] = a; _count = 1; _bary[0] = 1f; break;
                case 1: _s[0] = b; _count = 1; _bary[0] = 1f; break;
                case 2: _s[0] = c; _count = 1; _bary[0] = 1f; break;
                case 3: _s[0] = a; _s[1] = b; _count = 2; _bary[0] = u; _bary[1] = v; break; // ab
                case 4: _s[0] = a; _s[1] = c; _count = 2; _bary[0] = u; _bary[1] = w; break; // ac
                case 5: _s[0] = b; _s[1] = c; _count = 2; _bary[0] = v; _bary[1] = w; break; // bc
                default: _s[0] = a; _s[1] = b; _s[2] = c; _count = 3; _bary[0] = u; _bary[1] = v; _bary[2] = w; break;
            }
            return p;
        }

        /// <summary>Closest point on segment ab to the origin; feature 0 = a, 1 = b, 2 = interior.</summary>
        private static Vector3 ClosestSegment(Vector3 a, Vector3 b, out float u, out float v, out int feature)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1E-20f ? -Vector3.Dot(a, ab) / len2 : 0f;
            if (t <= 0f) { u = 1f; v = 0f; feature = 0; return a; }
            if (t >= 1f) { u = 0f; v = 1f; feature = 1; return b; }
            u = 1f - t; v = t; feature = 2;
            return a + ab * t;
        }

        /// <summary>Closest point on triangle abc to the origin with barycentrics (Ericson 5.1.5); feature: 0 a, 1 b, 2 c, 3 ab, 4 ac, 5 bc, 6 face.</summary>
        private static Vector3 ClosestTriangle(Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w, out int feature)
        {
            Vector3 ab = b - a, ac = c - a, ap = -a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { u = 1f; v = 0f; w = 0f; feature = 0; return a; }
            Vector3 bp = -b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { u = 0f; v = 1f; w = 0f; feature = 1; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float t = d1 / (d1 - d3); u = 1f - t; v = t; w = 0f; feature = 3; return a + ab * t; }
            Vector3 cp = -c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { u = 0f; v = 0f; w = 1f; feature = 2; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float t = d2 / (d2 - d6); u = 1f - t; v = 0f; w = t; feature = 4; return a + ac * t; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { float t = (d4 - d3) / ((d4 - d3) + (d5 - d6)); u = 0f; v = 1f - t; w = t; feature = 5; return b + (c - b) * t; }
            float denom = 1f / (va + vb + vc);
            v = vb * denom; w = vc * denom; u = 1f - v - w; feature = 6;
            return a + ab * v + ac * w;
        }

        private static bool OriginInTetrahedron(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            return SameSide(a, b, c, d) && SameSide(b, c, d, a) && SameSide(c, d, a, b) && SameSide(d, a, b, c);
        }

        /// <summary>The origin and p4 are on the same side of the plane p1 p2 p3 (or the origin is on it).</summary>
        private static bool SameSide(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            Vector3 n = Vector3.Cross(p2 - p1, p3 - p1);
            float dO = Vector3.Dot(n, -p1), d4 = Vector3.Dot(n, p4 - p1);
            return dO * d4 > 0f || MathF.Abs(dO) < 1E-12f;
        }

        // ------------------------------------------------------------------ sweeps

        /// <summary>
        /// Continuous sweep of <paramref name="a"/> moving by <paramref name="motionA"/> against <paramref name="b"/>
        /// moving by <paramref name="motionB"/> (over the same interval), stopping <paramref name="skin"/> short of
        /// contact. Only the part of the motion up to <paramref name="maxFraction"/> is examined (pass the best hit so
        /// far to prune). Translation only: a shape keeps its orientation during the sweep.
        /// </summary>
        public static bool Sweep(in CollisionShape a, Vector3 motionA, in CollisionShape b, Vector3 motionB, float maxFraction, float skin, out SweepResult result)
        {
            result = default; result.fraction = 1f;
            Vector3 rel = motionA - motionB;
            float speed = rel.magnitude; // per unit of fraction
            float t = 0f;
            CollisionShape at = a, bt = b;
            DistanceResult last = default;
            for (int it = 0; it < MaxIterations; it++)
            {
                if (!Distance(in at, in bt, out DistanceResult d))
                {
                    if (it == 0) { result.hit = true; result.startSolid = true; result.fraction = 0f; result.normal = Vector3.up; return true; }
                    // advanced into a deep overlap (should not happen with the half-skin step): report the previous time
                    result.hit = true; result.fraction = t; result.normal = last.normal; result.point = last.pointB; return true;
                }
                last = d;
                if (d.distance <= skin)
                {
                    if (it == 0 && d.distance < -skin) { result.startSolid = true; result.normal = d.normal; result.point = d.pointB; result.hit = true; result.fraction = 0f; return true; }
                    result.hit = true; result.fraction = t; result.normal = d.normal; result.point = d.pointB;
                    return true;
                }
                if (speed < 1E-09f) return false;
                // the shapes cannot close faster than the relative speed: this much of the motion is certainly free
                float dt = (d.distance - skin * 0.5f) / speed;
                t += dt;
                if (t >= maxFraction) return false;
                at = a.Translated(motionA * t);
                bt = b.Translated(motionB * t);
            }
            // out of iterations (very slow convergence): treat the last known position as the hit
            result.hit = true; result.fraction = t; result.normal = last.normal; result.point = last.pointB;
            return true;
        }
    }
}
