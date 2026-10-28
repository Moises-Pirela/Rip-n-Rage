using System;

namespace RipNRage.Engine
{
    /// <summary>Left-handed, Y-up vector (Unity convention: +Z is forward).</summary>
    [Serializable]
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0f; }

        public static readonly Vector3 zero = new Vector3(0, 0, 0);
        public static readonly Vector3 one = new Vector3(1, 1, 1);
        public static readonly Vector3 up = new Vector3(0, 1, 0);
        public static readonly Vector3 down = new Vector3(0, -1, 0);
        public static readonly Vector3 left = new Vector3(-1, 0, 0);
        public static readonly Vector3 right = new Vector3(1, 0, 0);
        public static readonly Vector3 forward = new Vector3(0, 0, 1);
        public static readonly Vector3 back = new Vector3(0, 0, -1);

        public float this[int i]
        {
            get => i == 0 ? x : i == 1 ? y : z;
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }

        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;

        public Vector3 normalized
        {
            get
            {
                float m = magnitude;
                return m > 1E-05f ? this / m : zero;
            }
        }

        public void Normalize()
        {
            float m = magnitude;
            if (m > 1E-05f) { x /= m; y /= m; z /= m; } else { x = 0; y = 0; z = 0; }
        }

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) =>
            new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        /// <summary>Spherical interpolation (same semantics as Unity's Vector3.Slerp).</summary>
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            float ma = a.magnitude, mb = b.magnitude;
            if (ma < 1E-06f || mb < 1E-06f) return Lerp(a, b, t);
            float dot = Mathf.Clamp(Dot(a, b) / (ma * mb), -1f, 1f);
            float theta = MathF.Acos(dot) * t;
            Vector3 relative = (b - a * dot).normalized;
            float mag = Mathf.Lerp(ma, mb, t);
            return (a.normalized * MathF.Cos(theta) + relative * MathF.Sin(theta)) * mag;
        }

        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            Vector3 d = target - current;
            float m = d.magnitude;
            if (m <= maxDistanceDelta || m < 1E-06f) return target;
            return current + d / m * maxDistanceDelta;
        }

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 Normalize(Vector3 v) => v.normalized;

        public static Vector3 Project(Vector3 vector, Vector3 onNormal)
        {
            float sqrMag = Dot(onNormal, onNormal);
            if (sqrMag < Mathf.Epsilon) return zero;
            return onNormal * Dot(vector, onNormal) / sqrMag;
        }

        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) => vector - Project(vector, planeNormal);

        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal) => -2f * Dot(inNormal, inDirection) * inNormal + inDirection;

        public static float Angle(Vector3 from, Vector3 to)
        {
            float denominator = MathF.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < 1E-15f) return 0f;
            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1f, 1f);
            return MathF.Acos(dot) * Mathf.Rad2Deg;
        }

        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            float unsigned = Angle(from, to);
            Vector3 c = Cross(from, to);
            float sign = MathF.Sign(axis.x * c.x + axis.y * c.y + axis.z * c.z);
            return unsigned * (sign == 0 ? 1 : sign);
        }

        public static Vector3 ClampMagnitude(Vector3 v, float maxLength)
        {
            if (v.sqrMagnitude > maxLength * maxLength) return v.normalized * maxLength;
            return v;
        }

        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y), MathF.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y), MathF.Max(a.z, b.z));

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static explicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00})";
    }
}
