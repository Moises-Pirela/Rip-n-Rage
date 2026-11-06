using System;

namespace RipNRage.Engine
{
    public enum ShapeType { Box, Sphere, Capsule, Hull, Triangle }

    /// <summary>
    /// A convex shape in world space, the common currency of every query: a polytopal <i>core</i> (a point for a
    /// sphere, a segment for a capsule, the eight corners of a box, the vertices of a brush or triangle) plus a
    /// <see cref="radius"/> that rounds it. Distances, overlaps and sweeps between any two of them go through
    /// <see cref="ConvexDistance"/> (GJK on the cores, radii subtracted), so sphere, capsule, box, brush and
    /// triangle all collide with each other exactly.
    /// </summary>
    public struct CollisionShape
    {
        public ShapeType type;
        public Vector3 center;
        /// <summary>Orientation of a box or capsule (identity for the others).</summary>
        public Quaternion rotation;
        public Vector3 halfExtents;     // box
        public float radius;            // sphere / capsule
        public float halfHeight;        // capsule: half length of the core segment along the local Y axis
        public ConvexShape polytope;    // hull / triangle

        public static CollisionShape Sphere(Vector3 center, float radius) =>
            new CollisionShape { type = ShapeType.Sphere, center = center, rotation = Quaternion.identity, radius = MathF.Max(radius, 0f) };

        public static CollisionShape Capsule(Vector3 center, Quaternion rotation, float radius, float halfHeight) =>
            new CollisionShape { type = ShapeType.Capsule, center = center, rotation = rotation, radius = MathF.Max(radius, 0f), halfHeight = MathF.Max(halfHeight, 0f) };

        public static CollisionShape Box(Vector3 center, Quaternion rotation, Vector3 halfExtents) =>
            new CollisionShape { type = ShapeType.Box, center = center, rotation = rotation, halfExtents = Vector3.Max(halfExtents, Vector3.zero) };

        public static CollisionShape AxisAlignedBox(Vector3 center, Vector3 halfExtents) => Box(center, Quaternion.identity, halfExtents);
        public static CollisionShape FromBounds(Bounds b) => Box(b.center, Quaternion.identity, b.extents);

        public static CollisionShape Polytope(ConvexShape s, bool triangle = false) =>
            new CollisionShape { type = triangle ? ShapeType.Triangle : ShapeType.Hull, center = s.center, rotation = Quaternion.identity, polytope = s };

        /// <summary>Rounding radius of the core (sphere / capsule); 0 for the polytopes.</summary>
        public float Radius => type == ShapeType.Sphere || type == ShapeType.Capsule ? radius : 0f;
        public bool isRounded => type == ShapeType.Sphere || type == ShapeType.Capsule;
        public bool isPolytope => type == ShapeType.Hull || type == ShapeType.Triangle;

        /// <summary>True for an unrotated box (the fast exact box-vs-hull path applies).</summary>
        public bool isAxisAlignedBox
        {
            get
            {
                if (type != ShapeType.Box) return false;
                float d = MathF.Abs(rotation.w);
                return d > 0.99999f || MathF.Abs(rotation.x) < 1E-04f && MathF.Abs(rotation.y) < 1E-04f && MathF.Abs(rotation.z) < 1E-04f;
            }
        }

        /// <summary>Capsule axis (unit, world).</summary>
        public Vector3 Axis => rotation * Vector3.up;

        /// <summary>Farthest point of the core along <paramref name="dir"/>.</summary>
        public Vector3 Support(Vector3 dir)
        {
            switch (type)
            {
                case ShapeType.Sphere: return center;
                case ShapeType.Capsule:
                {
                    Vector3 ax = Axis;
                    return center + ax * (Vector3.Dot(dir, ax) >= 0f ? halfHeight : -halfHeight);
                }
                case ShapeType.Box:
                {
                    Vector3 ax = rotation * Vector3.right, ay = rotation * Vector3.up, az = rotation * Vector3.forward;
                    return center + ax * (Vector3.Dot(dir, ax) >= 0f ? halfExtents.x : -halfExtents.x)
                                  + ay * (Vector3.Dot(dir, ay) >= 0f ? halfExtents.y : -halfExtents.y)
                                  + az * (Vector3.Dot(dir, az) >= 0f ? halfExtents.z : -halfExtents.z);
                }
                default: return polytope != null ? polytope.Support(dir) : center;
            }
        }

        /// <summary>Radius of the sphere around <see cref="center"/> that contains the whole (rounded) shape.</summary>
        public float BoundingRadius
        {
            get
            {
                switch (type)
                {
                    case ShapeType.Sphere: return radius;
                    case ShapeType.Capsule: return halfHeight + radius;
                    case ShapeType.Box: return halfExtents.magnitude;
                    default: return polytope != null ? polytope.radius : 0f;
                }
            }
        }

        /// <summary>Smallest extent of the shape (used to decide when a motion is "fast" for continuous collision).</summary>
        public float MinExtent
        {
            get
            {
                switch (type)
                {
                    case ShapeType.Sphere: return radius;
                    case ShapeType.Capsule: return radius;
                    case ShapeType.Box: return MathF.Min(halfExtents.x, MathF.Min(halfExtents.y, halfExtents.z));
                    default: return polytope != null ? polytope.radius * 0.5f : 0.1f;
                }
            }
        }

        /// <summary>World-space bounds of the rounded shape.</summary>
        public Bounds bounds
        {
            get
            {
                switch (type)
                {
                    case ShapeType.Sphere: return Bounds.FromCenterExtents(center, new Vector3(radius, radius, radius));
                    case ShapeType.Capsule:
                    {
                        Vector3 h = Axis * halfHeight;
                        Vector3 e = new Vector3(MathF.Abs(h.x) + radius, MathF.Abs(h.y) + radius, MathF.Abs(h.z) + radius);
                        return Bounds.FromCenterExtents(center, e);
                    }
                    case ShapeType.Box:
                    {
                        Vector3 ax = rotation * Vector3.right, ay = rotation * Vector3.up, az = rotation * Vector3.forward;
                        Vector3 e = new Vector3(
                            MathF.Abs(ax.x) * halfExtents.x + MathF.Abs(ay.x) * halfExtents.y + MathF.Abs(az.x) * halfExtents.z,
                            MathF.Abs(ax.y) * halfExtents.x + MathF.Abs(ay.y) * halfExtents.y + MathF.Abs(az.y) * halfExtents.z,
                            MathF.Abs(ax.z) * halfExtents.x + MathF.Abs(ay.z) * halfExtents.y + MathF.Abs(az.z) * halfExtents.z);
                        return Bounds.FromCenterExtents(center, e);
                    }
                    default:
                    {
                        if (polytope == null || polytope.vertices.Length == 0) return new Bounds(center, center);
                        Vector3 min = polytope.vertices[0], max = min;
                        for (int i = 1; i < polytope.vertices.Length; i++) { min = Vector3.Min(min, polytope.vertices[i]); max = Vector3.Max(max, polytope.vertices[i]); }
                        return new Bounds(min, max);
                    }
                }
            }
        }

        /// <summary>The same shape moved by <paramref name="delta"/> (polytopes are not moved: they are static geometry).</summary>
        public CollisionShape Translated(Vector3 delta)
        {
            var s = this;
            s.center += delta;
            return s;
        }

        /// <summary>The same shape at another pose (boxes, spheres, capsules).</summary>
        public CollisionShape At(Vector3 newCenter, Quaternion newRotation)
        {
            var s = this;
            s.center = newCenter; s.rotation = newRotation;
            return s;
        }

        public override string ToString() => $"{type} @ {center} r={radius} h={halfExtents}";
    }
}
