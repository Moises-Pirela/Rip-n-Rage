using System.Collections.Generic;

namespace RipNRage.Engine
{
    public struct Keyframe
    {
        public float time, value, inTangent, outTangent;
        public Keyframe(float time, float value) { this.time = time; this.value = value; inTangent = outTangent = 0f; }
        public Keyframe(float time, float value, float inTangent, float outTangent) { this.time = time; this.value = value; this.inTangent = inTangent; this.outTangent = outTangent; }
    }

    /// <summary>Hermite curve with the UnityEngine.AnimationCurve API subset used by the game.</summary>
    public sealed class AnimationCurve
    {
        public readonly List<Keyframe> keys = new List<Keyframe>();

        public AnimationCurve(params Keyframe[] keyframes) { keys.AddRange(keyframes); keys.Sort((a, b) => a.time.CompareTo(b.time)); }

        public static AnimationCurve Linear(float t0, float v0, float t1, float v1)
        {
            float slope = (v1 - v0) / (t1 - t0);
            return new AnimationCurve(new Keyframe(t0, v0, slope, slope), new Keyframe(t1, v1, slope, slope));
        }

        public static AnimationCurve EaseInOut(float t0, float v0, float t1, float v1) =>
            new AnimationCurve(new Keyframe(t0, v0, 0f, 0f), new Keyframe(t1, v1, 0f, 0f));

        public float Evaluate(float t)
        {
            if (keys.Count == 0) return 0f;
            if (keys.Count == 1 || t <= keys[0].time) return keys[0].value;
            if (t >= keys[keys.Count - 1].time) return keys[keys.Count - 1].value;
            int i = 0;
            while (i < keys.Count - 2 && t > keys[i + 1].time) i++;
            Keyframe a = keys[i], b = keys[i + 1];
            float dt = b.time - a.time;
            float u = (t - a.time) / dt;
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f, h10 = u3 - 2f * u2 + u, h01 = -2f * u3 + 3f * u2, h11 = u3 - u2;
            return h00 * a.value + h10 * dt * a.outTangent + h01 * b.value + h11 * dt * b.inTangent;
        }
    }
}
