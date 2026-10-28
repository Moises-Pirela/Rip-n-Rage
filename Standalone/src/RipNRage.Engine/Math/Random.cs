namespace RipNRage.Engine
{
    /// <summary>Drop-in replacement for UnityEngine.Random (the subset the game uses).</summary>
    public static class Random
    {
        private static System.Random _rng = new System.Random();

        public static void InitState(int seed) => _rng = new System.Random(seed);

        /// <summary>Random float in [min, max] (inclusive) like Unity.</summary>
        public static float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        /// <summary>Random int in [min, max) (max exclusive) like Unity.</summary>
        public static int Range(int min, int max) => max <= min ? min : _rng.Next(min, max);

        public static float value => (float)_rng.NextDouble();

        public static Vector3 insideUnitSphere
        {
            get
            {
                while (true)
                {
                    var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
                    if (v.sqrMagnitude <= 1f) return v;
                }
            }
        }

        public static Vector3 onUnitSphere => insideUnitSphere.normalized;
    }
}
