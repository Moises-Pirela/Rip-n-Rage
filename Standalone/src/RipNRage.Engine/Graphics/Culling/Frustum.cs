using System;

namespace RipNRage.Engine.Graphics.Culling
{
    /// <summary>
    /// Six view-frustum planes extracted from a view-projection matrix (Gribb/Hartmann).
    /// A point p is inside plane i when dot(n_i, p) + d_i >= 0.
    /// </summary>
    public struct Frustum
    {
        // left, right, bottom, top, near, far
        private Vector3 _n0, _n1, _n2, _n3, _n4, _n5;
        private float _d0, _d1, _d2, _d3, _d4, _d5;

        public static Frustum FromViewProj(in Matrix4x4 m)
        {
            // rows of the clip matrix (clip = M * p)
            Vector4 r0 = new Vector4(m.m00, m.m01, m.m02, m.m03);
            Vector4 r1 = new Vector4(m.m10, m.m11, m.m12, m.m13);
            Vector4 r2 = new Vector4(m.m20, m.m21, m.m22, m.m23);
            Vector4 r3 = new Vector4(m.m30, m.m31, m.m32, m.m33);
            var f = new Frustum();
            Set(r3 + r0, out f._n0, out f._d0);
            Set(r3 - r0, out f._n1, out f._d1);
            Set(r3 + r1, out f._n2, out f._d2);
            Set(r3 - r1, out f._n3, out f._d3);
            Set(r3 + r2, out f._n4, out f._d4);
            Set(r3 - r2, out f._n5, out f._d5);
            return f;
        }

        private static void Set(Vector4 p, out Vector3 n, out float d)
        {
            n = new Vector3(p.x, p.y, p.z);
            float len = n.magnitude;
            if (len < 1E-12f) { n = Vector3.zero; d = 1f; return; }
            n /= len; d = p.w / len;
        }

        /// <summary>True when the axis-aligned box touches or is inside the frustum (conservative p-vertex test).</summary>
        public bool IntersectsAABB(in Vector3 min, in Vector3 max)
        {
            return Inside(_n0, _d0, min, max) && Inside(_n1, _d1, min, max) && Inside(_n2, _d2, min, max)
                && Inside(_n3, _d3, min, max) && Inside(_n4, _d4, min, max) && Inside(_n5, _d5, min, max);
        }

        private static bool Inside(in Vector3 n, float d, in Vector3 min, in Vector3 max)
        {
            // most-positive vertex along the plane normal
            float px = n.x >= 0f ? max.x : min.x;
            float py = n.y >= 0f ? max.y : min.y;
            float pz = n.z >= 0f ? max.z : min.z;
            return n.x * px + n.y * py + n.z * pz + d >= 0f;
        }

        public bool IntersectsSphere(in Vector3 c, float r)
        {
            return Vector3.Dot(_n0, c) + _d0 >= -r && Vector3.Dot(_n1, c) + _d1 >= -r && Vector3.Dot(_n2, c) + _d2 >= -r
                && Vector3.Dot(_n3, c) + _d3 >= -r && Vector3.Dot(_n4, c) + _d4 >= -r && Vector3.Dot(_n5, c) + _d5 >= -r;
        }

        public bool ContainsPoint(in Vector3 p) => IntersectsSphere(p, 0f);
    }
}
