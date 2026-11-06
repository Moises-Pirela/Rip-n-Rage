using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine
{
    /// <summary>
    /// Triangle-mesh collider for static geometry (a rendered <see cref="Mesh"/> or explicit triangles). The
    /// triangles are baked to world space when the collider registers (call <see cref="RefreshBounds"/> after
    /// moving it) and indexed by an AABB tree, so queries only look at the triangles near them.
    ///
    /// Triangles are double-sided surfaces with no thickness. For box sweeps every triangle becomes a small
    /// <see cref="ConvexHull"/> with bevel planes (Quake 3's patch-collision trick: two faces, three side planes,
    /// bevels), built on first use and cached; rigid bodies collide with the triangles through the same SAT as any
    /// convex shape, with a bias towards the triangle's own normal so things sliding over a tessellated floor do
    /// not catch on the edges between its triangles.
    /// </summary>
    public sealed class MeshCollider : Collider
    {
        /// <summary>The mesh to collide with (its vertices / triangles; the transform places it).</summary>
        public Mesh mesh;

        private Vector3[] _verts = Array.Empty<Vector3>();
        private int[] _tris = Array.Empty<int>();
        private Bounds _bounds;
        private Bvh _bvh;
        private ConvexHull[] _hulls = Array.Empty<ConvexHull>();
        private bool _baked;

        public MeshCollider() { isStatic = true; }

        public int TriangleCount => _tris.Length / 3;
        public override Bounds bounds { get { if (!_baked) Bake(); return _bounds; } }

        /// <summary>Uses explicit triangles (local space) instead of a <see cref="Mesh"/>.</summary>
        public void SetTriangles(Vector3[] vertices, int[] triangles)
        {
            mesh = null;
            _source = vertices; _sourceTris = triangles;
            _baked = false;
            RefreshBounds();
        }

        private Vector3[] _source;
        private int[] _sourceTris;

        protected override void Awake()
        {
            Bake();
            base.Awake();
        }

        public override void RefreshBounds()
        {
            Bake();
            base.RefreshBounds();
        }

        /// <summary>Transforms the triangles to world space and (re)builds the tree.</summary>
        public void Bake()
        {
            Vector3[] src = mesh != null ? mesh.vertices : _source;
            int[] tris = mesh != null ? mesh.triangles : _sourceTris;
            _baked = true;
            if (src == null || tris == null || tris.Length < 3)
            {
                _verts = Array.Empty<Vector3>(); _tris = Array.Empty<int>(); _hulls = Array.Empty<ConvexHull>(); _bvh = null;
                _bounds = new Bounds(transform.position, transform.position);
                return;
            }
            Matrix4x4 m = transform.localToWorldMatrix;
            _verts = new Vector3[src.Length];
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < src.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(src[i]);
                _verts[i] = w;
                min = Vector3.Min(min, w); max = Vector3.Max(max, w);
            }
            // drop degenerate triangles
            var keep = new List<int>(tris.Length);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= _verts.Length || b >= _verts.Length || c >= _verts.Length) continue;
                if (Vector3.Cross(_verts[b] - _verts[a], _verts[c] - _verts[a]).sqrMagnitude < 1E-12f) continue;
                keep.Add(a); keep.Add(b); keep.Add(c);
            }
            _tris = keep.ToArray();
            _hulls = new ConvexHull[_tris.Length / 3];
            _bounds = _tris.Length > 0 ? new Bounds(min, max) : new Bounds(transform.position, transform.position);
            _bvh = _tris.Length > 0 ? new Bvh(_verts, _tris) : null;
        }

        public void GetTriangle(int index, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            int t = index * 3;
            a = _verts[_tris[t]]; b = _verts[_tris[t + 1]]; c = _verts[_tris[t + 2]];
        }

        /// <summary>Bevelled hull of one triangle for box sweeps (built on first use).</summary>
        public ConvexHull TriangleHull(int index)
        {
            var h = _hulls[index];
            if (h != null) return h;
            GetTriangle(index, out Vector3 a, out Vector3 b, out Vector3 c);
            h = ConvexHull.FromTriangle(a, b, c);
            _hulls[index] = h;
            return h;
        }

        /// <summary>Indices of the triangles whose bounds touch <paramref name="region"/>.</summary>
        public void QueryTriangles(Bounds region, List<int> results)
        {
            if (!_baked) Bake();
            _bvh?.Query(region, results);
        }

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            hit = default;
            if (!_baked) Bake();
            if (_bvh == null) return false;
            float best = maxDistance; int bestTri = -1; Vector3 bestN = Vector3.up;
            _bvh.Raycast(ray, ref best, ref bestTri, ref bestN);
            if (bestTri < 0) return false;
            hit = new RaycastHit { collider = this, point = ray.GetPoint(best), normal = bestN, distance = best };
            return true;
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            if (!_baked) Bake();
            if (_bvh == null) return p;
            float best = float.MaxValue; Vector3 bestPoint = p;
            _bvh.Closest(p, ref best, ref bestPoint);
            return bestPoint;
        }

        /// <summary>Möller–Trumbore, double sided.</summary>
        internal static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, float maxDistance, out float t, out Vector3 normal)
        {
            t = 0f; normal = Vector3.up;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1E-09f) return false;
            float inv = 1f / det;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1E-05f || u > 1f + 1E-05f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < -1E-05f || u + v > 1f + 1E-05f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f || t > maxDistance) return false;
            normal = Vector3.Cross(e1, e2).normalized;
            if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal; // face the ray
            return true;
        }

        internal static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // Ericson, Real-Time Collision Detection 5.1.5
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return a + ab * v; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return a + ac * w; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return b + (c - b) * w; }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            return a + ab * vv + ac * ww;
        }

        // ------------------------------------------------------------------ AABB tree

        /// <summary>Binary AABB tree over the triangles (median split on the longest axis, up to 4 triangles per leaf).</summary>
        private sealed class Bvh
        {
            private struct Node { public Bounds bounds; public int left, right; public int start, count; }

            private readonly Vector3[] _v;
            private readonly int[] _t;
            private readonly int[] _order;       // triangle indices, partitioned by the build
            private readonly Vector3[] _centroid;
            private readonly List<Node> _nodes = new List<Node>();

            public Bvh(Vector3[] v, int[] t)
            {
                _v = v; _t = t;
                int n = t.Length / 3;
                _order = new int[n];
                _centroid = new Vector3[n];
                for (int i = 0; i < n; i++) { _order[i] = i; _centroid[i] = (v[t[i * 3]] + v[t[i * 3 + 1]] + v[t[i * 3 + 2]]) / 3f; }
                Build(0, n);
            }

            private Bounds TriBounds(int tri)
            {
                Vector3 a = _v[_t[tri * 3]], b = _v[_t[tri * 3 + 1]], c = _v[_t[tri * 3 + 2]];
                return new Bounds(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
            }

            private int Build(int start, int count)
            {
                int index = _nodes.Count;
                var node = new Node { start = start, count = count, left = -1, right = -1 };
                Bounds b = TriBounds(_order[start]);
                for (int i = start + 1; i < start + count; i++) b.Encapsulate(TriBounds(_order[i]));
                node.bounds = b;
                _nodes.Add(node);
                if (count <= 4) return index;
                Vector3 size = b.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(_order, start, count, Comparer<int>.Create((x, y) => _centroid[x][axis].CompareTo(_centroid[y][axis])));
                int mid = count / 2;
                node.left = Build(start, mid);
                node.right = Build(start + mid, count - mid);
                node.count = 0;
                _nodes[index] = node;
                return index;
            }

            public void Query(Bounds region, List<int> results) => Query(0, region, results);

            private void Query(int i, Bounds region, List<int> results)
            {
                var n = _nodes[i];
                if (!n.bounds.Intersects(region)) return;
                if (n.count > 0)
                {
                    for (int k = n.start; k < n.start + n.count; k++)
                        if (TriBounds(_order[k]).Intersects(region)) results.Add(_order[k]);
                    return;
                }
                Query(n.left, region, results);
                Query(n.right, region, results);
            }

            public void Raycast(Ray ray, ref float best, ref int bestTri, ref Vector3 bestNormal) => Raycast(0, ray, ref best, ref bestTri, ref bestNormal);

            private void Raycast(int i, Ray ray, ref float best, ref int bestTri, ref Vector3 bestNormal)
            {
                var n = _nodes[i];
                if (!Physics.RayAabb(ray, n.bounds, best, null, out var box) || box.distance > best) return;
                if (n.count > 0)
                {
                    for (int k = n.start; k < n.start + n.count; k++)
                    {
                        int tri = _order[k];
                        if (RayTriangle(ray, _v[_t[tri * 3]], _v[_t[tri * 3 + 1]], _v[_t[tri * 3 + 2]], best, out float t, out Vector3 nn) && t < best)
                        {
                            best = t; bestTri = tri; bestNormal = nn;
                        }
                    }
                    return;
                }
                // nearer child first so the far one can be pruned
                bool hitL = Physics.RayAabb(ray, _nodes[n.left].bounds, best, null, out var l);
                bool hitR = Physics.RayAabb(ray, _nodes[n.right].bounds, best, null, out var r);
                if (hitL && hitR)
                {
                    if (l.distance <= r.distance) { Raycast(n.left, ray, ref best, ref bestTri, ref bestNormal); Raycast(n.right, ray, ref best, ref bestTri, ref bestNormal); }
                    else { Raycast(n.right, ray, ref best, ref bestTri, ref bestNormal); Raycast(n.left, ray, ref best, ref bestTri, ref bestNormal); }
                }
                else if (hitL) Raycast(n.left, ray, ref best, ref bestTri, ref bestNormal);
                else if (hitR) Raycast(n.right, ray, ref best, ref bestTri, ref bestNormal);
            }

            public void Closest(Vector3 p, ref float best, ref Vector3 bestPoint) => Closest(0, p, ref best, ref bestPoint);

            private static float DistanceToBounds(Vector3 p, Bounds b)
            {
                float dx = MathF.Max(b.min.x - p.x, MathF.Max(0f, p.x - b.max.x));
                float dy = MathF.Max(b.min.y - p.y, MathF.Max(0f, p.y - b.max.y));
                float dz = MathF.Max(b.min.z - p.z, MathF.Max(0f, p.z - b.max.z));
                return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            private void Closest(int i, Vector3 p, ref float best, ref Vector3 bestPoint)
            {
                var n = _nodes[i];
                if (DistanceToBounds(p, n.bounds) >= best) return;
                if (n.count > 0)
                {
                    for (int k = n.start; k < n.start + n.count; k++)
                    {
                        int tri = _order[k];
                        Vector3 q = ClosestPointOnTriangle(p, _v[_t[tri * 3]], _v[_t[tri * 3 + 1]], _v[_t[tri * 3 + 2]]);
                        float d = (q - p).magnitude;
                        if (d < best) { best = d; bestPoint = q; }
                    }
                    return;
                }
                float dl = DistanceToBounds(p, _nodes[n.left].bounds), dr = DistanceToBounds(p, _nodes[n.right].bounds);
                if (dl <= dr) { Closest(n.left, p, ref best, ref bestPoint); Closest(n.right, p, ref best, ref bestPoint); }
                else { Closest(n.right, p, ref best, ref bestPoint); Closest(n.left, p, ref best, ref bestPoint); }
            }
        }
    }
}
