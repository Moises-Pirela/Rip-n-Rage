using System;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Small generated textures for level geometry that had no exported material.</summary>
    public static class ProceduralTextures
    {
        /// <summary>Weathered wooden planks: horizontal boards with seams, grain and per-board tint.</summary>
        public static Texture2D WoodPlanks(int size = 512, int planks = 8, int seed = 7)
        {
            var rng = new System.Random(seed);
            var img = new Image(size, size);
            int plankH = size / planks;
            float[] tint = new float[planks];
            int[] offset = new int[planks];
            for (int i = 0; i < planks; i++) { tint[i] = 0.85f + (float)rng.NextDouble() * 0.3f; offset[i] = rng.Next(size); }

            for (int y = 0; y < size; y++)
            {
                int plank = y / plankH;
                int yInPlank = y % plankH;
                for (int x = 0; x < size; x++)
                {
                    // grain: stretched noise along X
                    float g = Noise((x + offset[plank]) * 0.02f, y * 0.35f, seed) * 0.5f + 0.5f;
                    float g2 = Noise((x + offset[plank]) * 0.11f, y * 0.9f, seed + 1) * 0.5f + 0.5f;
                    float shade = 0.72f + g * 0.22f + g2 * 0.08f;
                    shade *= tint[plank];
                    // seam between boards + board-end joints
                    if (yInPlank < 2 || yInPlank >= plankH - 1) shade *= 0.45f;
                    int joint = (x + offset[plank]) % (size / 2);
                    if (joint < 2) shade *= 0.5f;
                    // subtle nail marks
                    if (joint > 8 && joint < 12 && (yInPlank == plankH / 3 || yInPlank == plankH * 2 / 3)) shade *= 0.6f;

                    float r = 0.62f * shade, gg = 0.45f * shade, b = 0.30f * shade;
                    int o = (y * size + x) * 4;
                    img.Pixels[o] = (byte)Mathf.Clamp(r * 255f, 0, 255);
                    img.Pixels[o + 1] = (byte)Mathf.Clamp(gg * 255f, 0, 255);
                    img.Pixels[o + 2] = (byte)Mathf.Clamp(b * 255f, 0, 255);
                    img.Pixels[o + 3] = 255;
                }
            }
            return Texture2D.Create(img, FilterMode.Trilinear, TextureWrapMode.Repeat, true, "WoodPlanks");
        }

        /// <summary>Soft sand: low-frequency value noise with a few speckles.</summary>
        public static Texture2D Sand(int size = 256, int seed = 3)
        {
            var img = new Image(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = Noise(x * 0.05f, y * 0.05f, seed) * 0.5f + 0.5f;
                    float n2 = Noise(x * 0.4f, y * 0.4f, seed + 5) * 0.5f + 0.5f;
                    float shade = 0.82f + n * 0.12f + n2 * 0.06f;
                    int o = (y * size + x) * 4;
                    img.Pixels[o] = (byte)Mathf.Clamp(0.93f * shade * 255f, 0, 255);
                    img.Pixels[o + 1] = (byte)Mathf.Clamp(0.85f * shade * 255f, 0, 255);
                    img.Pixels[o + 2] = (byte)Mathf.Clamp(0.66f * shade * 255f, 0, 255);
                    img.Pixels[o + 3] = 255;
                }
            return Texture2D.Create(img, FilterMode.Trilinear, TextureWrapMode.Repeat, true, "Sand");
        }

        /// <summary>Tileable 2D value noise in [-1, 1].</summary>
        private static float Noise(float x, float y, int seed)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.LerpUnclamped(Mathf.LerpUnclamped(a, b, u), Mathf.LerpUnclamped(c, d, u), v);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 32767.5f - 1f;
            }
        }
    }
}
