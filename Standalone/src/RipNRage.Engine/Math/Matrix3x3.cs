using System;

namespace RipNRage.Engine
{
    /// <summary>Small 3x3 matrix (row-major fields) for effective-mass computations in the constraint solver.</summary>
    public struct Matrix3x3
    {
        public float m00, m01, m02, m10, m11, m12, m20, m21, m22;

        public static Matrix3x3 Identity => new Matrix3x3 { m00 = 1f, m11 = 1f, m22 = 1f };

        public static Matrix3x3 Scalar(float s) => new Matrix3x3 { m00 = s, m11 = s, m22 = s };

        /// <summary>Matrix with the given columns.</summary>
        public static Matrix3x3 FromColumns(Vector3 c0, Vector3 c1, Vector3 c2) => new Matrix3x3
        {
            m00 = c0.x, m10 = c0.y, m20 = c0.z,
            m01 = c1.x, m11 = c1.y, m21 = c1.z,
            m02 = c2.x, m12 = c2.y, m22 = c2.z,
        };

        /// <summary>Rotation matrix of a unit quaternion.</summary>
        public static Matrix3x3 FromQuaternion(Quaternion q)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
            float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
            return new Matrix3x3
            {
                m00 = 1f - 2f * (yy + zz), m01 = 2f * (xy - wz), m02 = 2f * (xz + wy),
                m10 = 2f * (xy + wz), m11 = 1f - 2f * (xx + zz), m12 = 2f * (yz - wx),
                m20 = 2f * (xz - wy), m21 = 2f * (yz + wx), m22 = 1f - 2f * (xx + yy),
            };
        }

        /// <summary>R · diag(d) · Rᵀ — a diagonal tensor rotated into world space.</summary>
        public static Matrix3x3 RotateDiagonal(Matrix3x3 r, Vector3 d)
        {
            // (R D) Rᵀ with D diagonal: column j of R scaled by d[j], then times Rᵀ
            float a00 = r.m00 * d.x, a01 = r.m01 * d.y, a02 = r.m02 * d.z;
            float a10 = r.m10 * d.x, a11 = r.m11 * d.y, a12 = r.m12 * d.z;
            float a20 = r.m20 * d.x, a21 = r.m21 * d.y, a22 = r.m22 * d.z;
            return new Matrix3x3
            {
                m00 = a00 * r.m00 + a01 * r.m01 + a02 * r.m02, m01 = a00 * r.m10 + a01 * r.m11 + a02 * r.m12, m02 = a00 * r.m20 + a01 * r.m21 + a02 * r.m22,
                m10 = a10 * r.m00 + a11 * r.m01 + a12 * r.m02, m11 = a10 * r.m10 + a11 * r.m11 + a12 * r.m12, m12 = a10 * r.m20 + a11 * r.m21 + a12 * r.m22,
                m20 = a20 * r.m00 + a21 * r.m01 + a22 * r.m02, m21 = a20 * r.m10 + a21 * r.m11 + a22 * r.m12, m22 = a20 * r.m20 + a21 * r.m21 + a22 * r.m22,
            };
        }

        /// <summary>Transposed product Rᵀ · v (the inverse rotation for a rotation matrix).</summary>
        public Vector3 MultiplyTransposed(Vector3 v) => new Vector3(
            m00 * v.x + m10 * v.y + m20 * v.z,
            m01 * v.x + m11 * v.y + m21 * v.z,
            m02 * v.x + m12 * v.y + m22 * v.z);

        public Vector3 Multiply(Vector3 v) => new Vector3(
            m00 * v.x + m01 * v.y + m02 * v.z,
            m10 * v.x + m11 * v.y + m12 * v.z,
            m20 * v.x + m21 * v.y + m22 * v.z);

        public static Matrix3x3 operator +(Matrix3x3 a, Matrix3x3 b) => new Matrix3x3
        {
            m00 = a.m00 + b.m00, m01 = a.m01 + b.m01, m02 = a.m02 + b.m02,
            m10 = a.m10 + b.m10, m11 = a.m11 + b.m11, m12 = a.m12 + b.m12,
            m20 = a.m20 + b.m20, m21 = a.m21 + b.m21, m22 = a.m22 + b.m22,
        };

        public float Determinant =>
            m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);

        /// <summary>Inverse by cofactors; the zero matrix when singular.</summary>
        public Matrix3x3 Inverse()
        {
            float det = Determinant;
            if (MathF.Abs(det) < 1E-12f) return new Matrix3x3();
            float inv = 1f / det;
            return new Matrix3x3
            {
                m00 = (m11 * m22 - m12 * m21) * inv, m01 = (m02 * m21 - m01 * m22) * inv, m02 = (m01 * m12 - m02 * m11) * inv,
                m10 = (m12 * m20 - m10 * m22) * inv, m11 = (m00 * m22 - m02 * m20) * inv, m12 = (m02 * m10 - m00 * m12) * inv,
                m20 = (m10 * m21 - m11 * m20) * inv, m21 = (m01 * m20 - m00 * m21) * inv, m22 = (m00 * m11 - m01 * m10) * inv,
            };
        }
    }
}
