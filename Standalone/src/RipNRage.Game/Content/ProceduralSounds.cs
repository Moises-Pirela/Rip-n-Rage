using System;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Sound effects synthesised in code for the parts of the game that have no recorded asset yet
    /// (the player's death, menu navigation). Each clip is generated once, on first use, straight
    /// into an OpenAL buffer through <see cref="AudioClip.Create"/>; no files are involved.
    /// </summary>
    public static class ProceduralSounds
    {
        private const int SampleRate = 22050;

        private static AudioClip _playerDeath, _menuMove, _menuSelect, _menuBack, _styleUp, _styleS, _enemyAim, _enemyShot, _enemyAlert, _dash;

        /// <summary>Rising "charging" whine: an enemy is about to fire.</summary>
        public static AudioClip EnemyAim => _enemyAim ??= Build("EnemyAim", 0.35f, (t, noise) =>
        {
            float f = 300f + 900f * (t / 0.35f);
            return (MathF.Sin(2f * MathF.PI * f * t) * 0.6f + noise * 0.08f) * MathF.Min(1f, t * 12f) * 0.45f;
        });

        /// <summary>Short low snap for an enemy shot.</summary>
        public static AudioClip EnemyShot => _enemyShot ??= Build("EnemyShot", 0.18f, (t, noise) =>
        {
            float v = noise * MathF.Exp(-t * 40f) * 0.9f;
            v += MathF.Sin(2f * MathF.PI * (180f - 120f * t / 0.18f) * t) * MathF.Exp(-t * 18f) * 0.7f;
            return v * 0.6f;
        });

        /// <summary>Two-note "noticed you" sting.</summary>
        public static AudioClip EnemyAlert => _enemyAlert ??= Build("EnemyAlert", 0.3f, (t, noise) =>
        {
            float a = MathF.Sin(2f * MathF.PI * 440f * t) * MathF.Exp(-t * 20f);
            float b = t > 0.1f ? MathF.Sin(2f * MathF.PI * 660f * (t - 0.1f)) * MathF.Exp(-(t - 0.1f) * 14f) : 0f;
            return (a + b) * 0.4f;
        });

        /// <summary>Air whoosh for the dash.</summary>
        public static AudioClip Dash => _dash ??= Build("Dash", 0.3f, (t, noise) =>
        {
            float env = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.3f));
            return noise * env * env * 0.5f;
        });

        /// <summary>Rising three-note sting for a style rank-up.</summary>
        public static AudioClip StyleUp => _styleUp ??= Build("StyleUp", 0.35f, (t, noise) =>
        {
            float v = 0f;
            float[] f = { 523f, 659f, 784f };
            for (int i = 0; i < 3; i++) { float u = t - i * 0.08f; if (u >= 0f) v += MathF.Sin(2f * MathF.PI * f[i] * u) * MathF.Exp(-u * 14f); }
            return v * 0.35f;
        });

        /// <summary>Bigger, brighter sting with a shimmer for reaching S.</summary>
        public static AudioClip StyleS => _styleS ??= Build("StyleS", 0.8f, (t, noise) =>
        {
            float v = 0f;
            float[] f = { 523f, 659f, 784f, 1046f };
            for (int i = 0; i < 4; i++) { float u = t - i * 0.07f; if (u >= 0f) v += MathF.Sin(2f * MathF.PI * f[i] * u) * MathF.Exp(-u * 6f); }
            v += MathF.Sin(2f * MathF.PI * 2093f * t) * MathF.Exp(-t * 5f) * 0.15f * (0.5f + 0.5f * MathF.Sin(t * 60f));
            return v * 0.3f;
        });

        /// <summary>Heavy double thud with a falling pitch and a short noise tail: the player hitting the floor.</summary>
        public static AudioClip PlayerDeath => _playerDeath ??= Build("PlayerDeath", 1.8f, (t, noise) =>
        {
            float v = Thud(t, 0.00f, 150f, 42f, 0.55f) * 0.95f;      // impact
            v += Thud(t, 0.62f, 115f, 38f, 0.60f) * 0.60f;           // second, weaker beat
            v += Thud(t, 1.20f, 90f, 35f, 0.55f) * 0.35f;            // fading heartbeat
            v += noise * 0.16f * MathF.Exp(-t * 4f);                 // dust / cloth tail
            return v;
        });

        /// <summary>Short click for moving the highlight in a menu.</summary>
        public static AudioClip MenuMove => _menuMove ??= Build("MenuMove", 0.07f, (t, noise) =>
            MathF.Sin(2f * MathF.PI * 820f * t) * MathF.Exp(-t * 60f) * 0.45f);

        /// <summary>Two-note confirmation blip.</summary>
        public static AudioClip MenuSelect => _menuSelect ??= Build("MenuSelect", 0.22f, (t, noise) =>
        {
            float a = MathF.Sin(2f * MathF.PI * 660f * t) * MathF.Exp(-t * 28f);
            float b = t > 0.07f ? MathF.Sin(2f * MathF.PI * 990f * (t - 0.07f)) * MathF.Exp(-(t - 0.07f) * 22f) : 0f;
            return (a + b) * 0.4f;
        });

        /// <summary>Descending blip for cancel / back.</summary>
        public static AudioClip MenuBack => _menuBack ??= Build("MenuBack", 0.18f, (t, noise) =>
            MathF.Sin(2f * MathF.PI * (520f - 220f * t / 0.18f) * t) * MathF.Exp(-t * 24f) * 0.4f);

        /// <summary>Decaying sine whose frequency sweeps from <paramref name="f0"/> to <paramref name="f1"/> over <paramref name="duration"/>.</summary>
        private static float Thud(float t, float start, float f0, float f1, float duration)
        {
            float u = t - start;
            if (u < 0f || u >= duration) return 0f;
            float k = u / duration;
            // phase of a linear chirp: 2π (f0 u + (f1 - f0) u² / (2 d))
            float phase = 2f * MathF.PI * (f0 * u + (f1 - f0) * u * u / (2f * duration));
            float env = MathF.Exp(-k * 5f) * (1f - k);
            return MathF.Sin(phase) * env;
        }

        /// <summary>Samples <paramref name="generator"/>(time, white noise) at 22.05 kHz mono with a soft clipper and a fade-out.</summary>
        private static AudioClip Build(string name, float seconds, Func<float, float, float> generator)
        {
            int count = Math.Max(1, (int)(SampleRate * seconds));
            var samples = new short[count];
            var rng = new System.Random(1234);
            int fadeStart = Math.Max(0, count - SampleRate / 50); // last 20 ms
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float v = generator(t, noise);
                v = MathF.Tanh(v * 1.2f);
                if (i >= fadeStart) v *= (count - i) / (float)Math.Max(1, count - fadeStart);
                samples[i] = (short)Mathf.Clamp(v * 32767f, -32768f, 32767f);
            }
            return AudioClip.Create(name, samples, SampleRate, 1);
        }
    }
}
