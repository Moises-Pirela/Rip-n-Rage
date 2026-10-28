using System;

namespace RipNRage.Engine
{
    /// <summary>
    /// Column-major 4x4 matrix (Unity / OpenGL layout). Element (row r, column c) is stored at
    /// index <c>c * 4 + r</c>, so the raw array can be uploaded to OpenGL without transposing.
    /// Vectors are treated as columns: <c>M * v</c>.
    /// </summary>
    public struct Matrix4x4
    {
        public float m00, m10, m20, m30; // column 0
        public float m01, m11, m21, m31; // column 1
        public float m02, m12, m22, m32; // column 2
        public float m03, m13, m23, m33; // column 3

        public static Matrix4x4 identity
        {
            get
            {
                var m = new Matrix4x4();
                m.m00 = m.m11 = m.m22 = m.m33 = 1f;
                return m;
            }
        }

        public static Matrix4x4 zero => new Matrix4x4();

        public float this[int row, int col]
        {
            get => ToArray()[col * 4 + row];
        }

        public float[] ToArray() => new[]
        {
            m00, m10, m20, m30,
            m01, m11, m21, m31,
            m02, m12, m22, m32,
            m03, m13, m23, m33
        };

        public unsafe void CopyTo(float* dst)
        {
            dst[0] = m00; dst[1] = m10; dst[2] = m20; dst[3] = m30;
            dst[4] = m01; dst[5] = m11; dst[6] = m21; dst[7] = m31;
            dst[8] = m02; dst[9] = m12; dst[10] = m22; dst[11] = m32;
            dst[12] = m03; dst[13] = m13; dst[14] = m23; dst[15] = m33;
        }

        public Vector3 GetColumn3(int i)
        {
            switch (i)
            {
                case 0: return new Vector3(m00, m10, m20);
                case 1: return new Vector3(m01, m11, m21);
                case 2: return new Vector3(m02, m12, m22);
                default: return new Vector3(m03, m13, m23);
            }
        }

        public Vector3 GetPosition() => new Vector3(m03, m13, m23);

        public static Matrix4x4 Translate(Vector3 v)
        {
            var m = identity;
            m.m03 = v.x; m.m13 = v.y; m.m23 = v.z;
            return m;
        }

        public static Matrix4x4 Scale(Vector3 s)
        {
            var m = identity;
            m.m00 = s.x; m.m11 = s.y; m.m22 = s.z;
            return m;
        }

        public static Matrix4x4 Rotate(Quaternion q)
        {
            float x2 = q.x * 2f, y2 = q.y * 2f, z2 = q.z * 2f;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
            float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
            var m = identity;
            m.m00 = 1f - (yy + zz); m.m01 = xy - wz; m.m02 = xz + wy;
            m.m10 = xy + wz; m.m11 = 1f - (xx + zz); m.m12 = yz - wx;
            m.m20 = xz - wy; m.m21 = yz + wx; m.m22 = 1f - (xx + yy);
            return m;
        }

        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s)
        {
            var m = Rotate(q);
            m.m00 *= s.x; m.m10 *= s.x; m.m20 *= s.x;
            m.m01 *= s.y; m.m11 *= s.y; m.m21 *= s.y;
            m.m02 *= s.z; m.m12 *= s.z; m.m22 *= s.z;
            m.m03 = pos.x; m.m13 = pos.y; m.m23 = pos.z;
            return m;
        }

        /// <summary>
        /// Left-handed perspective projection producing OpenGL clip space (NDC z in [-1,1],
        /// +Z forward, depth increases with distance). Matches Unity's camera convention.
        /// </summary>
        public static Matrix4x4 Perspective(float fovYDeg, float aspect, float near, float far)
        {
            float f = 1f / MathF.Tan(fovYDeg * Mathf.Deg2Rad * 0.5f);
            var m = zero;
            m.m00 = f / aspect;
            m.m11 = f;
            m.m22 = (far + near) / (far - near);
            m.m23 = -2f * far * near / (far - near);
            m.m32 = 1f;
            return m;
        }

        /// <summary>Orthographic projection (screen-space UI: x right, y up, z into the screen).</summary>
        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float near, float far)
        {
            var m = identity;
            m.m00 = 2f / (right - left);
            m.m11 = 2f / (top - bottom);
            m.m22 = 2f / (far - near);
            m.m03 = -(right + left) / (right - left);
            m.m13 = -(top + bottom) / (top - bottom);
            m.m23 = -(far + near) / (far - near);
            return m;
        }

        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 r;
            r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30;
            r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31;
            r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32;
            r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33;

            r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30;
            r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31;
            r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32;
            r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33;

            r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30;
            r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31;
            r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32;
            r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33;

            r.m30 = a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30;
            r.m31 = a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31;
            r.m32 = a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32;
            r.m33 = a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33;
            return r;
        }

        public Vector3 MultiplyPoint(Vector3 v)
        {
            float x = m00 * v.x + m01 * v.y + m02 * v.z + m03;
            float y = m10 * v.x + m11 * v.y + m12 * v.z + m13;
            float z = m20 * v.x + m21 * v.y + m22 * v.z + m23;
            float w = m30 * v.x + m31 * v.y + m32 * v.z + m33;
            if (MathF.Abs(w) > 1E-08f && MathF.Abs(w - 1f) > 1E-08f) { x /= w; y /= w; z /= w; }
            return new Vector3(x, y, z);
        }

        public Vector3 MultiplyPoint3x4(Vector3 v) => new Vector3(
            m00 * v.x + m01 * v.y + m02 * v.z + m03,
            m10 * v.x + m11 * v.y + m12 * v.z + m13,
            m20 * v.x + m21 * v.y + m22 * v.z + m23);

        public Vector3 MultiplyVector(Vector3 v) => new Vector3(
            m00 * v.x + m01 * v.y + m02 * v.z,
            m10 * v.x + m11 * v.y + m12 * v.z,
            m20 * v.x + m21 * v.y + m22 * v.z);

        public Matrix4x4 inverse => Inverse(this);

        public static Matrix4x4 Inverse(Matrix4x4 m)
        {
            float[] a = m.ToArray();
            float[] inv = new float[16];

            inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
            inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
            inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
            inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];
            inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
            inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
            inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
            inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
            inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
            inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
            inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
            inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
            inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
            inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
            inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
            inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];

            float det = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];
            if (MathF.Abs(det) < 1E-12f) return identity;
            det = 1f / det;
            return FromArray(inv, det);
        }

        private static Matrix4x4 FromArray(float[] a, float scale)
        {
            Matrix4x4 m;
            m.m00 = a[0] * scale; m.m10 = a[1] * scale; m.m20 = a[2] * scale; m.m30 = a[3] * scale;
            m.m01 = a[4] * scale; m.m11 = a[5] * scale; m.m21 = a[6] * scale; m.m31 = a[7] * scale;
            m.m02 = a[8] * scale; m.m12 = a[9] * scale; m.m22 = a[10] * scale; m.m32 = a[11] * scale;
            m.m03 = a[12] * scale; m.m13 = a[13] * scale; m.m23 = a[14] * scale; m.m33 = a[15] * scale;
            return m;
        }

        public override string ToString() =>
            $"[{m00:0.00} {m01:0.00} {m02:0.00} {m03:0.00}]\n[{m10:0.00} {m11:0.00} {m12:0.00} {m13:0.00}]\n[{m20:0.00} {m21:0.00} {m22:0.00} {m23:0.00}]\n[{m30:0.00} {m31:0.00} {m32:0.00} {m33:0.00}]";
    }
}
