using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Tweening
{
    public enum Ease { Linear, InSine, OutSine, InOutSine, InQuad, OutQuad, InOutQuad }

    /// <summary>A running interpolation (tiny stand-in for the DOTween Tweener the UI/camera code used).</summary>
    public sealed class Tween
    {
        internal Action<float> apply;
        internal float duration;
        internal float elapsed;
        internal Ease ease = Ease.Linear;
        internal object target;
        internal bool complete;

        public Action onComplete;

        public Tween SetEase(Ease e) { ease = e; return this; }
        public Tween OnComplete(Action a) { onComplete += a; return this; }
        public bool IsActive => !complete;

        public void Kill() { complete = true; TweenRunner.Remove(this); }

        /// <summary>Jumps to the end immediately (DOTween's DOComplete).</summary>
        public void Complete()
        {
            if (complete) return;
            apply(1f);
            complete = true;
            TweenRunner.Remove(this);
            onComplete?.Invoke();
        }

        internal static float Evaluate(Ease e, float t)
        {
            switch (e)
            {
                case Ease.InSine: return 1f - MathF.Cos(t * MathF.PI * 0.5f);
                case Ease.OutSine: return MathF.Sin(t * MathF.PI * 0.5f);
                case Ease.InOutSine: return -(MathF.Cos(MathF.PI * t) - 1f) * 0.5f;
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) * 0.5f;
                default: return t;
            }
        }
    }

    /// <summary>Ticks all tweens once per frame (called by the application loop).</summary>
    public static class TweenRunner
    {
        private static readonly List<Tween> Active = new List<Tween>();

        public static Tween To(Func<float> getter, Action<float> setter, float end, float duration, object target = null)
        {
            float start = getter();
            return Add(t => setter(Mathf.LerpUnclamped(start, end, t)), duration, target);
        }

        public static Tween To(Func<Vector2> getter, Action<Vector2> setter, Vector2 end, float duration, object target = null)
        {
            Vector2 start = getter();
            return Add(t => setter(Vector2.LerpUnclamped(start, end, t)), duration, target);
        }

        public static Tween To(Func<Vector3> getter, Action<Vector3> setter, Vector3 end, float duration, object target = null)
        {
            Vector3 start = getter();
            return Add(t => setter(Vector3.LerpUnclamped(start, end, t)), duration, target);
        }

        public static Tween Rotate(Func<Quaternion> getter, Action<Quaternion> setter, Quaternion end, float duration, object target = null)
        {
            Quaternion start = getter();
            return Add(t => setter(Quaternion.Slerp(start, end, t)), duration, target);
        }

        private static Tween Add(Action<float> apply, float duration, object target)
        {
            var tw = new Tween { apply = apply, duration = Mathf.Max(duration, 0f), target = target };
            if (tw.duration <= 0f) { apply(1f); tw.complete = true; tw.onComplete?.Invoke(); return tw; }
            Active.Add(tw);
            return tw;
        }

        internal static void Remove(Tween t) => Active.Remove(t);

        /// <summary>Completes every tween targeting <paramref name="target"/> (DOTween's DOComplete(target)).</summary>
        public static void CompleteAll(object target)
        {
            foreach (var t in Active.ToArray()) if (t.target == target) t.Complete();
        }

        public static void KillAll(object target)
        {
            foreach (var t in Active.ToArray()) if (t.target == target) t.Kill();
        }

        public static void Update(float dt)
        {
            foreach (var t in Active.ToArray())
            {
                if (t.complete) { Active.Remove(t); continue; }
                t.elapsed += dt;
                float k = Mathf.Clamp01(t.elapsed / t.duration);
                try { t.apply(Tween.Evaluate(t.ease, k)); }
                catch (Exception e) { Debug.LogError("Tween apply threw: " + e); t.complete = true; }
                if (k >= 1f)
                {
                    t.complete = true;
                    Active.Remove(t);
                    try { t.onComplete?.Invoke(); }
                    catch (Exception e) { Debug.LogError("Tween onComplete threw: " + e); }
                }
            }
        }
    }

    /// <summary>DOTween-style extension methods used by the game.</summary>
    public static class TweenExtensions
    {
        /// <summary>Smoothly rotates the transform to look at a world point (DOTween's DOLookAt).</summary>
        public static Tween DOLookAt(this Transform t, Vector3 worldPoint, float duration)
        {
            Vector3 dir = worldPoint - t.position;
            if (dir.sqrMagnitude < 1E-08f) return TweenRunner.Rotate(() => t.rotation, r => t.rotation = r, t.rotation, 0f, t);
            Quaternion target = Quaternion.LookRotation(dir);
            return TweenRunner.Rotate(() => t.rotation, r => t.rotation = r, target, duration, t);
        }

        public static Tween DOLocalMove(this Transform t, Vector3 localPos, float duration) =>
            TweenRunner.To(() => t.localPosition, v => t.localPosition = v, localPos, duration, t);

        public static void DOComplete(this Transform t) => TweenRunner.CompleteAll(t);
        public static void DOKill(this Transform t) => TweenRunner.KillAll(t);
    }
}
