using System;

namespace RipNRage.Engine
{
    /// <summary>
    /// Rotation quaternion with UnityEngine.Quaternion semantics:
    /// <c>a * b</c> applies <c>b</c> first, then <c>a</c>; <c>Euler(x,y,z)</c> rotates around
    /// Z, then X, then Y (Unity's ZXY order); <c>q * v</c> rotates a vector.
    /// </summary>
    [Serializable]
    public struct Quaternion : IEquatable<Quaternion>
    {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quaternion identity = new Quaternion(0, 0, 0, 1);

        public static Quaternion AngleAxis(float angleDeg, Vector3 axis)
        {
            axis = axis.normalized;
            float half = angleDeg * Mathf.Deg2Rad * 0.5f;
            float s = MathF.Sin(half);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(half));
        }

        /// <summary>Unity order: Z, then X, then Y (i.e. q = Ry * Rx * Rz).</summary>
        public static Quaternion Euler(float xDeg, float yDeg, float zDeg)
        {
            return AngleAxis(yDeg, Vector3.up) * AngleAxis(xDeg, Vector3.right) * AngleAxis(zDeg, Vector3.forward);
        }

        public static Quaternion Euler(Vector3 euler) => Euler(euler.x, euler.y, euler.z);

        public Vector3 eulerAngles
        {
            get => ToEuler(this);
            set => this = Euler(value);
        }

        /// <summary>Extracts ZXY euler angles in degrees, each normalised into [0,360) like Unity.</summary>
        private static Vector3 ToEuler(Quaternion q)
        {
            q = Normalize(q);
            // Rotation matrix elements (column-vector convention, R = Ry * Rx * Rz).
            float m02 = 2f * (q.x * q.z + q.w * q.y);
            float m12 = 2f * (q.y * q.z - q.w * q.x);
            float m22 = 1f - 2f * (q.x * q.x + q.y * q.y);
            float m10 = 2f * (q.x * q.y + q.w * q.z);
            float m11 = 1f - 2f * (q.x * q.x + q.z * q.z);
            float m00 = 1f - 2f * (q.y * q.y + q.z * q.z);
            float m20 = 2f * (q.x * q.z - q.w * q.y);

            float sx = -m12; // sin(x)
            float ex, ey, ez;
            if (sx < 0.9999999f && sx > -0.9999999f)
            {
                ex = MathF.Asin(sx);
                ey = MathF.Atan2(m02, m22);
                ez = MathF.Atan2(m10, m11);
            }
            else
            {
                // Gimbal lock: pitch is +-90, fold yaw into the remaining degree of freedom.
                ex = sx > 0 ? MathF.PI * 0.5f : -MathF.PI * 0.5f;
                ey = MathF.Atan2(-m20, m00);
                ez = 0f;
            }
            return new Vector3(Wrap360(ex * Mathf.Rad2Deg), Wrap360(ey * Mathf.Rad2Deg), Wrap360(ez * Mathf.Rad2Deg));
        }

        private static float Wrap360(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            // Kill negative-zero / tiny float noise so that values like 359.99999 stay stable.
            if (deg >= 360f) deg -= 360f;
            if (deg == 0f) deg = 0f; // normalise negative zero
            return deg;
        }

        public static Quaternion Normalize(Quaternion q)
        {
            float mag = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < Mathf.Epsilon) return identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        public Quaternion normalized => Normalize(this);

        public static Quaternion Inverse(Quaternion q)
        {
            float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (n < Mathf.Epsilon) return identity;
            float inv = 1f / n;
            return new Quaternion(-q.x * inv, -q.y * inv, -q.z * inv, q.w * inv);
        }

        public static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = MathF.Min(MathF.Abs(Dot(a, b)), 1f);
            return dot > 0.999999f ? 0f : MathF.Acos(dot) * 2f * Mathf.Rad2Deg;
        }

        /// <summary>Rotation looking along <paramref name="forward"/> with <paramref name="upwards"/> as the up hint.</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            forward = forward.normalized;
            if (forward.sqrMagnitude < 1E-08f) return identity;
            Vector3 right = Vector3.Cross(upwards, forward);
            if (right.sqrMagnitude < 1E-08f)
            {
                // forward parallel to up: pick any perpendicular axis
                right = Vector3.Cross(MathF.Abs(forward.y) < 0.99f ? Vector3.up : Vector3.right, forward);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);
            return FromMatrix(right, up, forward);
        }

        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);

        /// <summary>Builds a quaternion from orthonormal basis vectors (the matrix columns).</summary>
        private static Quaternion FromMatrix(Vector3 c0, Vector3 c1, Vector3 c2)
        {
            float m00 = c0.x, m01 = c1.x, m02 = c2.x;
            float m10 = c0.y, m11 = c1.y, m12 = c2.y;
            float m20 = c0.z, m21 = c1.z, m22 = c2.z;
            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Normalize(q);
        }

        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            from = from.normalized; to = to.normalized;
            float d = Vector3.Dot(from, to);
            if (d >= 0.999999f) return identity;
            if (d <= -0.999999f)
            {
                Vector3 axis = Vector3.Cross(Vector3.right, from);
                if (axis.sqrMagnitude < 1E-06f) axis = Vector3.Cross(Vector3.up, from);
                return AngleAxis(180f, axis);
            }
            Vector3 c = Vector3.Cross(from, to);
            float s = MathF.Sqrt((1f + d) * 2f);
            float inv = 1f / s;
            return Normalize(new Quaternion(c.x * inv, c.y * inv, c.z * inv, s * 0.5f));
        }

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));

        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            float dot = Dot(a, b);
            if (dot < 0f) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); dot = -dot; }
            if (dot > 0.9995f) return Normalize(LerpRaw(a, b, t));
            float theta0 = MathF.Acos(dot);
            float theta = theta0 * t;
            float sinTheta = MathF.Sin(theta);
            float sinTheta0 = MathF.Sin(theta0);
            float s0 = MathF.Cos(theta) - dot * sinTheta / sinTheta0;
            float s1 = sinTheta / sinTheta0;
            return new Quaternion(a.x * s0 + b.x * s1, a.y * s0 + b.y * s1, a.z * s0 + b.z * s1, a.w * s0 + b.w * s1);
        }

        public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t);
            if (Dot(a, b) < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            return Normalize(LerpRaw(a, b, t));
        }

        private static Quaternion LerpRaw(Quaternion a, Quaternion b, float t) =>
            new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);

        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle == 0f) return to;
            return SlerpUnclamped(from, to, MathF.Min(1f, maxDegreesDelta / angle));
        }

        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float x2 = q.x * 2f, y2 = q.y * 2f, z2 = q.z * 2f;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
            float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
            return new Vector3(
                (1f - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z,
                (xy + wz) * v.x + (1f - (xx + zz)) * v.y + (yz - wx) * v.z,
                (xz - wy) * v.x + (yz + wx) * v.y + (1f - (xx + yy)) * v.z);
        }

        public static bool operator ==(Quaternion a, Quaternion b) => Dot(a, b) > 0.999999f;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public bool Equals(Quaternion other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override bool Equals(object obj) => obj is Quaternion q && Equals(q);
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override string ToString() => $"({x:0.000}, {y:0.000}, {z:0.000}, {w:0.000})";
    }
}
