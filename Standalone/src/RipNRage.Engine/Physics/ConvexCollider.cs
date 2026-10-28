using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// Convex polyhedron collider defined by world-space planes (Quake brushes). Raycasts use the
    /// plane slab test; character movement uses a separating-axis test against the mover's box.
    /// </summary>
    public sealed class ConvexCollider : Collider
    {
        public Plane[] planes = Array.Empty<Plane>();
        public Vector3[] vertices = Array.Empty<Vector3>();
        private Bounds _bounds;

        /// <summary>Sets the shape from planes and its polygon vertices (used for bounds and SAT projections).</summary>
        public void SetShape(Plane[] worldPlanes, Vector3[] worldVertices)
        {
            planes = worldPlanes; vertices = worldVertices;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            foreach (var v in worldVertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            if (worldVertices.Length == 0) { min = max = transform.position; }
            _bounds = new Bounds(min, max);
        }

        public override Bounds bounds => _bounds;

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            hit = default;
            float tEnter = 0f, tExit = maxDistance;
            Vector3 n = Vector3.zero;
            foreach (var p in planes)
            {
                float denom = Vector3.Dot(p.normal, ray.direction);
                float dist = p.Distance(ray.origin);
                if (MathF.Abs(denom) < 1E-8f) { if (dist > 1E-5f) return false; continue; }
                float t = -dist / denom;
                if (denom < 0f) { if (t > tEnter) { tEnter = t; n = p.normal; } }
                else if (t < tExit) tExit = t;
                if (tEnter > tExit) return false;
            }
            if (n == Vector3.zero) return false;
            hit = new RaycastHit { collider = this, point = ray.GetPoint(tEnter), normal = n, distance = tEnter };
            return true;
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            // iterative clamp against the planes (good enough for overlap queries)
            Vector3 q = p;
            for (int iter = 0; iter < 4; iter++)
                foreach (var pl in planes) { float d = pl.Distance(q); if (d > 0f) q -= pl.normal * d; }
            return q;
        }

        /// <summary>
        /// Separating-axis test of an AABB against this convex shape. Returns the penetration depth
        /// along <paramref name="axis"/> (the axis used for resolution) or -1 when not intersecting.
        /// </summary>
        public float PenetrationAlong(Bounds box, int axis)
        {
            if (!_bounds.Intersects(box)) return -1f;
            var corners = BoxCorners(box);
            // axes: 3 box axes + face normals
            float penAlongAxis = 0f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = Vector3.zero; a[i] = 1f;
                if (!Overlap(a, corners, out float pen)) return -1f;
                if (i == axis) penAlongAxis = pen;
            }
            foreach (var p in planes) if (!Overlap(p.normal, corners, out _)) return -1f;
            return penAlongAxis;
        }

        private bool Overlap(Vector3 axis, Vector3[] boxCorners, out float pen)
        {
            Project(axis, boxCorners, out float aMin, out float aMax);
            Project(axis, vertices, out float bMin, out float bMax);
            pen = MathF.Min(aMax - bMin, bMax - aMin);
            return aMax > bMin + 1E-5f && bMax > aMin + 1E-5f;
        }

        private static void Project(Vector3 axis, Vector3[] pts, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var p in pts) { float d = Vector3.Dot(axis, p); if (d < min) min = d; if (d > max) max = d; }
        }

        private static Vector3[] BoxCorners(Bounds b)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return c;
        }

        /// <summary>Which side of the box (along the axis) the shape is on, for push-out direction.</summary>
        public float CenterAlong(int axis)
        {
            var c = _bounds.center;
            return c[axis];
        }
    }
}
