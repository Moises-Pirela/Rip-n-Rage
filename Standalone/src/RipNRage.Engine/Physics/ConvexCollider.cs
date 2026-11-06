using System;

namespace RipNRage.Engine
{
    /// <summary>
    /// Convex polyhedron collider defined by world-space planes (Quake brushes). Static by nature: the planes are
    /// in world space, so the collider cannot follow a moving transform. The shape is a <see cref="ConvexHull"/>
    /// with bevel planes, which makes box sweeps against ramps and rotated brushes exact.
    /// </summary>
    public sealed class ConvexCollider : Collider
    {
        private ConvexHull _hull = ConvexHull.FromBounds(new Bounds(Vector3.zero, Vector3.zero));
        private Plane[] _faces = Array.Empty<Plane>();
        private Vector3[] _vertices = Array.Empty<Vector3>();

        public ConvexCollider() { isStatic = true; }

        /// <summary>The face planes the shape was built from.</summary>
        public Plane[] planes => _faces;
        /// <summary>The polygon vertices the shape was built from.</summary>
        public Vector3[] vertices => _vertices;

        /// <summary>Sets the shape from its face planes and polygon vertices (the vertices give the bounding box and the edge bevels).</summary>
        public void SetShape(Plane[] worldPlanes, Vector3[] worldVertices)
        {
            _faces = worldPlanes ?? Array.Empty<Plane>();
            _vertices = worldVertices ?? Array.Empty<Vector3>();
            _hull = ConvexHull.FromPlanes(_faces, _vertices, true);
            _shape = null;
            if (_vertices.Length == 0) _hull.bounds = new Bounds(transform.position, transform.position);
            RefreshBounds();
        }

        public override Bounds bounds => _hull.bounds;
        public override ConvexHull hull => _hull;

        private ConvexShape _shape;
        /// <summary>Face/edge topology of the brush for the rigid-body narrow phase (built on first use).</summary>
        public ConvexShape Shape => _shape ??= ConvexShape.FromPlanes(_faces, _vertices);
        public override CollisionShape shape => CollisionShape.Polytope(Shape);

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            hit = default;
            if (!_hull.Raycast(ray, maxDistance, out float t, out Vector3 n)) return false;
            hit = new RaycastHit { collider = this, point = ray.GetPoint(t), normal = n, distance = t };
            return true;
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            // iterative clamp against the faces (good enough for overlap queries)
            Vector3 q = p;
            for (int iter = 0; iter < 4; iter++)
                foreach (var pl in _faces) { float d = pl.Distance(q); if (d > 0f) q -= pl.normal * d; }
            return q;
        }

        /// <summary>True when an axis-aligned box overlaps this brush (exact).</summary>
        public bool Overlaps(Bounds box) => _hull.OverlapsBox(box.center, box.extents);

        /// <summary>Minimum translation vector that separates <paramref name="box"/> from this brush (<paramref name="normal"/> points out of the brush).</summary>
        public bool ComputeMtv(Bounds box, out Vector3 normal, out float depth) => _hull.BoxPenetration(box.center, box.extents, out normal, out depth);
    }
}
