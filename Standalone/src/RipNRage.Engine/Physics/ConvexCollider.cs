using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// Convex polyhedron collider defined by world-space planes (Quake brushes). Raycasts use the plane slab
    /// test; character movement uses the expanded-plane (Minkowski) overlap test, which yields a true minimum
    /// translation vector instead of a projection overlap length.
    /// </summary>
    public sealed class ConvexCollider : Collider
    {
        public Plane[] planes = Array.Empty<Plane>();
        public Vector3[] vertices = Array.Empty<Vector3>();
        private Bounds _bounds;

        /// <summary>Sets the shape from planes and its polygon vertices (the vertices give the bounding box).</summary>
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
        /// True when an axis-aligned box overlaps this brush.
        ///
        /// The test is the half-space form of the Minkowski sum: every face plane is pushed outwards by the box's
        /// support along that plane's normal, and the box centre is tested against the expanded planes. The box is
        /// separated as soon as it is outside any one of them (a convex shape only needs a single separating plane),
        /// so this never misses a real overlap. Without the extra bevel planes it can report an overlap for a box
        /// that only just clears a sharp edge — conservative by at most a few centimetres, the classic Quake
        /// trade-off, and always a *small* error, never a large one.
        /// </summary>
        public bool Overlaps(Bounds box)
        {
            if (!_bounds.Intersects(box)) return false;
            Vector3 c = box.center, e = (box.max - box.min) * 0.5f;
            foreach (var p in planes)
                if (p.Distance(c) - Support(p.normal, e) >= 0f) return false;
            return true;
        }

        /// <summary>
        /// Minimum translation vector that separates <paramref name="box"/> from this brush: the cheapest of the
        /// face-normal and box-axis separating directions. <paramref name="normal"/> points out of the brush and
        /// <paramref name="depth"/> is how far along it the box has to move. Because the depth is measured against
        /// the *plane that is closest to clearing*, it is always the small real penetration — not the width of the
        /// brush, which is what the old projection-overlap code returned for a box sitting inside a big slab.
        /// </summary>
        public bool ComputeMtv(Bounds box, out Vector3 normal, out float depth)
        {
            normal = Vector3.up; depth = 0f;
            if (!_bounds.Intersects(box)) return false;
            Vector3 c = box.center, e = (box.max - box.min) * 0.5f;
            float best = float.MaxValue;
            Vector3 bestN = Vector3.zero;
            foreach (var p in planes)
            {
                float d = p.Distance(c) - Support(p.normal, e); // < 0 while the box is behind the expanded plane
                if (d >= 0f) return false;
                if (-d < best) { best = -d; bestN = p.normal; }
            }
            for (int i = 0; i < 3; i++)
            {
                float pushPos = _bounds.max[i] - box.min[i]; // move +i until the box clears the brush's far side
                float pushNeg = box.max[i] - _bounds.min[i]; // move -i until it clears the near side
                if (pushPos <= 0f || pushNeg <= 0f) return false;
                Vector3 a = Vector3.zero; a[i] = 1f;
                if (pushPos < best) { best = pushPos; bestN = a; }
                if (pushNeg < best) { best = pushNeg; bestN = -a; }
            }
            if (bestN == Vector3.zero || best >= float.MaxValue) return false;
            normal = bestN; depth = best;
            return true;
        }

        /// <summary>
        /// Distance the box has to travel along <paramref name="sign"/> * <paramref name="axis"/> before it stops
        /// overlapping this brush (0 when it already does not, <see cref="float.MaxValue"/> when moving that way
        /// can never separate them). Used to resolve an axis sweep without leaving the sweep's axis.
        /// </summary>
        public float SeparationAlong(Bounds box, int axis, float sign)
        {
            if (!Overlaps(box)) return 0f;
            Vector3 c = box.center, e = (box.max - box.min) * 0.5f;
            float best = float.MaxValue;
            foreach (var p in planes)
            {
                float along = sign * p.normal[axis];
                if (along <= 1E-4f) continue; // this face cannot be cleared by moving that way
                float d = p.Distance(c) - Support(p.normal, e);
                if (d >= 0f) return 0f;
                float t = -d / along;
                if (t < best) best = t;
            }
            // the brush's own bounding slab on the sweep axis is a separating plane too
            float slab = sign > 0f ? _bounds.max[axis] - box.min[axis] : box.max[axis] - _bounds.min[axis];
            if (slab > 0f && slab < best) best = slab;
            return best;
        }

        /// <summary>Half-width of an axis-aligned box with extents <paramref name="e"/> projected on <paramref name="n"/>.</summary>
        private static float Support(Vector3 n, Vector3 e) => MathF.Abs(n.x) * e.x + MathF.Abs(n.y) * e.y + MathF.Abs(n.z) * e.z;

        /// <summary>Legacy helper: penetration depth along an axis (the cheaper of the two directions), -1 when clear.</summary>
        public float PenetrationAlong(Bounds box, int axis)
        {
            if (!Overlaps(box)) return -1f;
            return MathF.Min(SeparationAlong(box, axis, 1f), SeparationAlong(box, axis, -1f));
        }

        /// <summary>Which side of the box (along the axis) the shape is on, for push-out direction.</summary>
        public float CenterAlong(int axis)
        {
            var c = _bounds.center;
            return c[axis];
        }
    }
}
