using System;

namespace RipNRage.Engine
{
    /// <summary>Plane in Hessian normal form: dot(n, p) = d. Points with dot(n,p) &lt; d are "behind" (inside a brush).</summary>
    public struct Plane
    {
        public Vector3 normal;
        public float dist;

        public Plane(Vector3 normal, float dist) { this.normal = normal; this.dist = dist; }

        /// <summary>Plane through three points; the normal follows the Quake .map convention (clockwise seen from outside).</summary>
        public static Plane FromPoints(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(c - a, b - a).normalized;
            return new Plane(n, Vector3.Dot(n, a));
        }

        public float Distance(Vector3 p) => Vector3.Dot(normal, p) - dist;
        public Vector3 Project(Vector3 p) => p - normal * Distance(p);
        public Plane Flipped => new Plane(-normal, -dist);

        /// <summary>Ray/plane intersection parameter (false if parallel).</summary>
        public bool Raycast(Ray ray, out float t)
        {
            float denom = Vector3.Dot(normal, ray.direction);
            if (MathF.Abs(denom) < 1E-8f) { t = 0f; return false; }
            t = (dist - Vector3.Dot(normal, ray.origin)) / denom;
            return true;
        }

        public override string ToString() => $"Plane(n {normal}, d {dist:0.###})";
    }
}
