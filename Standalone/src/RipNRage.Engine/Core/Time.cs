namespace RipNRage.Engine
{
    /// <summary>Frame timing, same API surface as UnityEngine.Time.</summary>
    public static class Time
    {
        public static float deltaTime { get; internal set; }
        public static float unscaledDeltaTime { get; internal set; }
        public static float smoothDeltaTime { get; internal set; }
        public static float fixedDeltaTime { get; set; } = 0.02f;
        public static float time { get; internal set; }
        public static float unscaledTime { get; internal set; }
        public static float fixedTime { get; internal set; }
        public static float timeScale { get; set; } = 1f;
        public static int frameCount { get; internal set; }
        public static float realtimeSinceStartup { get; internal set; }
        /// <summary>Longest simulation step a frame may take; a hitch longer than this slows the game down instead of letting things move metres per step.</summary>
        public static float maximumDeltaTime { get; set; } = 0.1f;

        /// <summary>True while FixedUpdate callbacks are running (deltaTime reports fixedDeltaTime then, like Unity).</summary>
        public static bool inFixedTimeStep { get; internal set; }

        internal static void Advance(float realDt)
        {
            realtimeSinceStartup += realDt;
            unscaledDeltaTime = realDt > maximumDeltaTime ? maximumDeltaTime : realDt;
            unscaledTime += unscaledDeltaTime;
            deltaTime = unscaledDeltaTime * timeScale;
            time += deltaTime;
            smoothDeltaTime = smoothDeltaTime <= 0f ? deltaTime : smoothDeltaTime * 0.8f + deltaTime * 0.2f;
            frameCount++;
        }
    }
}
