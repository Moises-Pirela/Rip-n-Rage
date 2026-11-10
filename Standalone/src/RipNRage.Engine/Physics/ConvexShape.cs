using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// A convex polytope with full topology (vertices, face polygons, edges) in world space — what the rigid-body
    /// narrow phase needs: the separating-axis test walks faces and edge pairs, and the contact manifold is made
    /// by clipping one face polygon against another. (<see cref="ConvexHull"/> is the plane-only form used for
    /// box sweeps; a triangle is the degenerate two-faced case.)
    /// </summary>
    public sealed class ConvexShape
    {
        public struct Face
        {
            public Vector3 normal;
            public float dist;       // dot(normal, p) == dist on the face
            public int[] indices;    // polygon loop
        }

        /// <summary>Edge between vertices a and b; f0 / f1 are the two faces sharing it (-1 = unknown: no Gauss-map pruning for this edge).</summary>
        public struct Edge { public int a, b; public int f0, f1; }

        public Vector3[] vertices = Array.Empty<Vector3>();
        public Face[] faces = Array.Empty<Face>();
        public Edge[] edges = Array.Empty<Edge>();
        public Vector3 center;
        /// <summary>Radius of the bounding sphere around <see cref="center"/>.</summary>
        public float radius;
        /// <summary>Set by <see cref="SetBox"/>: the shape is an oriented box (axes + half extents below), so closest points and capsule contacts are analytic.</summary>
        public bool isBox;
        public Vector3 boxAxisX, boxAxisY, boxAxisZ, boxHalf;

        // ------------------------------------------------------------------ boxes

        // vertex i: bit0 = +x, bit1 = +y, bit2 = +z
        private static readonly int[][] BoxFaces =
        {
            new[] { 1, 3, 7, 5 }, new[] { 0, 4, 6, 2 }, // +x, -x
            new[] { 2, 6, 7, 3 }, new[] { 0, 1, 5, 4 }, // +y, -y
            new[] { 4, 5, 7, 6 }, new[] { 0, 2, 3, 1 }, // +z, -z
        };
        // faces: 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z
        private static readonly Edge[] BoxEdges =
        {
            new Edge { a = 0, b = 1, f0 = 3, f1 = 5 }, new Edge { a = 2, b = 3, f0 = 2, f1 = 5 }, new Edge { a = 4, b = 5, f0 = 3, f1 = 4 }, new Edge { a = 6, b = 7, f0 = 2, f1 = 4 },
            new Edge { a = 0, b = 2, f0 = 1, f1 = 5 }, new Edge { a = 1, b = 3, f0 = 0, f1 = 5 }, new Edge { a = 4, b = 6, f0 = 1, f1 = 4 }, new Edge { a = 5, b = 7, f0 = 0, f1 = 4 },
            new Edge { a = 0, b = 4, f0 = 1, f1 = 3 }, new Edge { a = 1, b = 5, f0 = 0, f1 = 3 }, new Edge { a = 2, b = 6, f0 = 1, f1 = 2 }, new Edge { a = 3, b = 7, f0 = 0, f1 = 2 },
        };

        /// <summary>Allocates a box shape; fill it with <see cref="SetBox"/>.</summary>
        public static ConvexShape CreateBox()
        {
            var s = new ConvexShape { vertices = new Vector3[8], faces = new Face[6], edges = BoxEdges, hasEdgeFaces = true };
            for (int i = 0; i < 6; i++) s.faces[i].indices = BoxFaces[i];
            return s;
        }

        /// <summary>Oriented box in place (no allocation).</summary>
        public void SetBox(Vector3 c, Vector3 halfExtents, Quaternion rotation)
        {
            Vector3 ax = rotation * Vector3.right, ay = rotation * Vector3.up, az = rotation * Vector3.forward;
            Vector3 ex = ax * halfExtents.x, ey = ay * halfExtents.y, ez = az * halfExtents.z;
            for (int i = 0; i < 8; i++)
                vertices[i] = c + ((i & 1) == 0 ? -ex : ex) + ((i & 2) == 0 ? -ey : ey) + ((i & 4) == 0 ? -ez : ez);
            faces[0].normal = ax; faces[0].dist = Vector3.Dot(ax, c) + halfExtents.x;
            faces[1].normal = -ax; faces[1].dist = -Vector3.Dot(ax, c) + halfExtents.x;
            faces[2].normal = ay; faces[2].dist = Vector3.Dot(ay, c) + halfExtents.y;
            faces[3].normal = -ay; faces[3].dist = -Vector3.Dot(ay, c) + halfExtents.y;
            faces[4].normal = az; faces[4].dist = Vector3.Dot(az, c) + halfExtents.z;
            faces[5].normal = -az; faces[5].dist = -Vector3.Dot(az, c) + halfExtents.z;
            center = c;
            radius = halfExtents.magnitude;
            isBox = true; boxAxisX = ax; boxAxisY = ay; boxAxisZ = az; boxHalf = halfExtents;
        }

        /// <summary>Box-local coordinates of a world point.</summary>
        public Vector3 BoxLocal(Vector3 p) { Vector3 d = p - center; return new Vector3(Vector3.Dot(d, boxAxisX), Vector3.Dot(d, boxAxisY), Vector3.Dot(d, boxAxisZ)); }

        private Vector3 BoxWorld(Vector3 l) => center + boxAxisX * l.x + boxAxisY * l.y + boxAxisZ * l.z;

        /// <summary>Analytic closest point on an oriented box (same contract as <see cref="ClosestPoint"/>).</summary>
        private Vector3 BoxClosestPoint(Vector3 p, out Vector3 normal, out float distance, out bool inside)
        {
            Vector3 l = BoxLocal(p), h = boxHalf;
            Vector3 q = new Vector3(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y), Mathf.Clamp(l.z, -h.z, h.z));
            inside = q == l;
            if (!inside)
            {
                Vector3 wq = BoxWorld(q);
                Vector3 dir = p - wq;
                distance = dir.magnitude;
                normal = distance > 1E-06f ? dir / distance : boxAxisY;
                return wq;
            }
            // inside: the face we are least behind (smallest depth), like the generic path
            float dx = h.x - MathF.Abs(l.x), dy = h.y - MathF.Abs(l.y), dz = h.z - MathF.Abs(l.z);
            if (dx <= dy && dx <= dz) { normal = boxAxisX * (l.x >= 0f ? 1f : -1f); distance = dx; }
            else if (dy <= dz) { normal = boxAxisY * (l.y >= 0f ? 1f : -1f); distance = dy; }
            else { normal = boxAxisZ * (l.z >= 0f ? 1f : -1f); distance = dz; }
            return p + normal * distance;
        }

        // ------------------------------------------------------------------ triangles

        private static readonly Edge[] TriEdges = { new Edge { a = 0, b = 1, f0 = -1, f1 = -1 }, new Edge { a = 1, b = 2, f0 = -1, f1 = -1 }, new Edge { a = 2, b = 0, f0 = -1, f1 = -1 } };

        /// <summary>True when every edge knows its two faces (the SAT can prune edge pairs with the Gauss map).</summary>
        public bool hasEdgeFaces;

        /// <summary>Allocates a (double-sided, zero thickness) triangle shape; fill it with <see cref="SetTriangle"/>.</summary>
        public static ConvexShape CreateTriangle()
        {
            var s = new ConvexShape { vertices = new Vector3[3], faces = new Face[2], edges = TriEdges };
            s.faces[0].indices = new[] { 0, 1, 2 };
            s.faces[1].indices = new[] { 0, 2, 1 };
            return s;
        }

        public void SetTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            isBox = false;
            vertices[0] = a; vertices[1] = b; vertices[2] = c;
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            n = len > 1E-12f ? n / len : Vector3.up;
            float d = Vector3.Dot(n, a);
            faces[0].normal = n; faces[0].dist = d;
            faces[1].normal = -n; faces[1].dist = -d;
            center = (a + b + c) / 3f;
            radius = MathF.Sqrt(MathF.Max((a - center).sqrMagnitude, MathF.Max((b - center).sqrMagnitude, (c - center).sqrMagnitude)));
        }

        /// <summary>The triangle's own (front) normal.</summary>
        public Vector3 triangleNormal => faces.Length > 0 ? faces[0].normal : Vector3.up;

        // ------------------------------------------------------------------ general hulls

        /// <summary>
        /// Builds the topology of a hull from its face planes and polygon vertices (the brush data): the vertices
        /// on each plane are ordered around the face, edges are the polygon sides shared by two faces.
        /// </summary>
        public static ConvexShape FromPlanes(Plane[] planes, Vector3[] verts)
        {
            const float eps = 1E-03f;
            var s = new ConvexShape();
            var unique = new List<Vector3>(verts.Length);
            foreach (var v in verts)
            {
                bool dup = false;
                for (int i = 0; i < unique.Count; i++) if ((unique[i] - v).sqrMagnitude < 1E-08f) { dup = true; break; }
                if (!dup) unique.Add(v);
            }
            s.vertices = unique.ToArray();
            Vector3 c = Vector3.zero;
            foreach (var v in s.vertices) c += v;
            if (s.vertices.Length > 0) c /= s.vertices.Length;
            s.center = c;
            float r2 = 0f;
            foreach (var v in s.vertices) r2 = MathF.Max(r2, (v - c).sqrMagnitude);
            s.radius = MathF.Sqrt(r2);

            var faces = new List<Face>(planes.Length);
            var edgeFaces = new Dictionary<(int, int), (int f0, int f1)>();
            var loop = new List<(float angle, int index)>();
            foreach (var pl in planes)
            {
                Vector3 n = pl.normal.normalized;
                if (n == Vector3.zero) continue;
                float d = pl.dist / MathF.Max(pl.normal.magnitude, 1E-06f);
                loop.Clear();
                Vector3 fc = Vector3.zero; int count = 0;
                for (int i = 0; i < s.vertices.Length; i++)
                    if (MathF.Abs(Vector3.Dot(n, s.vertices[i]) - d) < eps) { fc += s.vertices[i]; count++; loop.Add((0f, i)); }
                if (count < 3) continue;
                fc /= count;
                Vector3 u = Vector3.Cross(n, MathF.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 w = Vector3.Cross(n, u);
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 p = s.vertices[loop[i].index] - fc;
                    loop[i] = (MathF.Atan2(Vector3.Dot(p, w), Vector3.Dot(p, u)), loop[i].index);
                }
                loop.Sort((x, y) => x.angle.CompareTo(y.angle));
                var idx = new int[loop.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = loop[i].index;
                faces.Add(new Face { normal = n, dist = d, indices = idx });
                int faceIndex = faces.Count - 1;
                for (int i = 0; i < idx.Length; i++)
                {
                    int a = idx[i], b = idx[(i + 1) % idx.Length];
                    var key = a < b ? (a, b) : (b, a);
                    if (edgeFaces.TryGetValue(key, out var ef)) { if (ef.f1 < 0 && ef.f0 != faceIndex) edgeFaces[key] = (ef.f0, faceIndex); }
                    else edgeFaces[key] = (faceIndex, -1);
                }
            }
            s.faces = faces.ToArray();
            s.edges = new Edge[edgeFaces.Count];
            int k = 0; bool all = true;
            foreach (var kv in edgeFaces)
            {
                s.edges[k++] = new Edge { a = kv.Key.Item1, b = kv.Key.Item2, f0 = kv.Value.f0, f1 = kv.Value.f1 };
                if (kv.Value.f1 < 0) all = false;
            }
            s.hasEdgeFaces = all;
            return s;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Extent of the shape along an axis.</summary>
        public void Project(Vector3 axis, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float d = Vector3.Dot(axis, vertices[i]);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        public Vector3 Support(Vector3 dir)
        {
            Vector3 best = center; float bd = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float d = Vector3.Dot(dir, vertices[i]);
                if (d > bd) { bd = d; best = vertices[i]; }
            }
            return best;
        }

        public bool Contains(Vector3 p)
        {
            for (int i = 0; i < faces.Length; i++)
                if (Vector3.Dot(faces[i].normal, p) - faces[i].dist > 0f) return false;
            return true;
        }

        /// <summary>Centroid of a face polygon.</summary>
        public Vector3 FaceCenter(int face)
        {
            var idx = faces[face].indices;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < idx.Length; i++) c += vertices[idx[i]];
            return idx.Length > 0 ? c / idx.Length : center;
        }

        /// <summary>
        /// Closest point on the surface to <paramref name="p"/> and the outward direction there. When the point is
        /// inside, the closest face is used and <paramref name="inside"/> is set (distance is then the depth).
        /// </summary>
        public Vector3 ClosestPoint(Vector3 p, out Vector3 normal, out float distance, out bool inside)
        {
            if (isBox) return BoxClosestPoint(p, out normal, out distance, out inside);
            // inside: the face we are least behind
            float bestD = float.MinValue; int bestFace = -1;
            inside = true;
            for (int i = 0; i < faces.Length; i++)
            {
                float d = Vector3.Dot(faces[i].normal, p) - faces[i].dist;
                if (d > 0f) inside = false;
                if (d > bestD) { bestD = d; bestFace = i; }
            }
            if (inside && bestFace >= 0)
            {
                normal = faces[bestFace].normal;
                distance = -bestD;
                return p - normal * bestD;
            }
            inside = false;
            // outside: faces (projection inside the polygon), then edges
            float best = float.MaxValue; Vector3 bestPoint = center;
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                float d = Vector3.Dot(f.normal, p) - f.dist;
                if (d <= 0f) continue;
                Vector3 q = p - f.normal * d;
                Vector3 fc = FaceCenter(i);
                bool insidePoly = true;
                var idx = f.indices;
                for (int k = 0; k < idx.Length && insidePoly; k++)
                {
                    Vector3 a = vertices[idx[k]], b = vertices[idx[(k + 1) % idx.Length]];
                    Vector3 side = Vector3.Cross(b - a, f.normal);
                    if (Vector3.Dot(side, a - fc) < 0f) side = -side; // outward
                    if (Vector3.Dot(side, q - a) > 1E-05f) insidePoly = false;
                }
                if (insidePoly && d < best) { best = d; bestPoint = q; }
            }
            for (int i = 0; i < edges.Length; i++)
            {
                Vector3 a = vertices[edges[i].a], b = vertices[edges[i].b];
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 1E-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                Vector3 q = a + ab * t;
                float d = (p - q).magnitude;
                if (d < best) { best = d; bestPoint = q; }
            }
            distance = best;
            Vector3 dir = p - bestPoint;
            normal = dir.sqrMagnitude > 1E-12f ? dir.normalized : (faces.Length > 0 ? faces[bestFace < 0 ? 0 : bestFace].normal : Vector3.up);
            return bestPoint;
        }
    }
}
