using System;
using System.Collections;

namespace RipNRage.Engine
{
    /// <summary>Base for yield instructions understood by the coroutine scheduler.</summary>
    public abstract class YieldInstruction { }

    public sealed class WaitForSeconds : YieldInstruction
    {
        public readonly float seconds;
        public WaitForSeconds(float seconds) { this.seconds = seconds; }
    }

    public sealed class WaitForSecondsRealtime : YieldInstruction
    {
        public readonly float seconds;
        public WaitForSecondsRealtime(float seconds) { this.seconds = seconds; }
    }

    public sealed class WaitUntil : YieldInstruction
    {
        public readonly Func<bool> predicate;
        public WaitUntil(Func<bool> predicate) { this.predicate = predicate; }
    }

    public sealed class WaitWhile : YieldInstruction
    {
        public readonly Func<bool> predicate;
        public WaitWhile(Func<bool> predicate) { this.predicate = predicate; }
    }

    public sealed class WaitForEndOfFrame : YieldInstruction { }
    public sealed class WaitForFixedUpdate : YieldInstruction { }

    /// <summary>Handle to a running coroutine (returned by Behaviour.StartCoroutine).</summary>
    public sealed class Coroutine : YieldInstruction
    {
        internal readonly Behaviour owner;
        internal readonly IEnumerator routine;
        internal bool finished;

        // scheduling state
        internal float wakeTime = -1f;
        internal bool realtime;
        internal Func<bool> waitUntil;
        internal bool waitUntilExpected;
        internal Coroutine waitFor;

        internal Coroutine(Behaviour owner, IEnumerator routine)
        {
            this.owner = owner;
            this.routine = routine;
        }

        /// <summary>Advances the coroutine. Returns false when it has completed.</summary>
        internal bool Step()
        {
            if (finished) return false;

            if (waitFor != null)
            {
                if (!waitFor.finished) return true;
                waitFor = null;
            }
            if (waitUntil != null)
            {
                bool v;
                try { v = waitUntil(); }
                catch (Exception e) { Debug.LogError($"Coroutine predicate threw: {e}"); finished = true; return false; }
                if (v != waitUntilExpected) return true;
                waitUntil = null;
            }
            if (wakeTime >= 0f)
            {
                float now = realtime ? Time.unscaledTime : Time.time;
                if (now < wakeTime) return true;
                wakeTime = -1f;
            }

            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e)
            {
                Debug.LogError($"Coroutine on {owner?.GetType().Name} threw: {e}");
                finished = true;
                return false;
            }

            if (!more)
            {
                finished = true;
                return false;
            }

            object y = routine.Current;
            switch (y)
            {
                case null: break; // resume next frame
                case WaitForSeconds w: wakeTime = Time.time + w.seconds; realtime = false; break;
                case WaitForSecondsRealtime w: wakeTime = Time.unscaledTime + w.seconds; realtime = true; break;
                case WaitUntil w: waitUntil = w.predicate; waitUntilExpected = true; break;
                case WaitWhile w: waitUntil = w.predicate; waitUntilExpected = false; break;
                case Coroutine c: waitFor = c; break;
                case IEnumerator nested: waitFor = Scene.Current.StartCoroutine(owner, nested); break;
                default: break; // unknown instruction: behave like null (next frame)
            }
            return true;
        }
    }
}
