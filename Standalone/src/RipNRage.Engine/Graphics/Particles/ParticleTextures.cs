using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Graphics.Particles
{
    /// <summary>
    /// The built-in particle sprite sheet, generated at start-up (no art files needed): white/grey shapes with
    /// alpha so the emitter's colour defines the look. Registered with <see cref="SpriteLibrary"/> as the
    /// "Particles" atlas; effects reference them as <c>Particles/soft</c>, <c>Particles/smoke</c>, …
    /// </summary>
    public static class ParticleTextures
    {
        public const int Cell = 128;
        private const int Cols = 4, Rows = 4;
        private static SpriteAtlas _atlas;

        /// <summary>Names of every generated sprite (palette order).</summary>
        public static readonly string[] Names =
        {
            "soft", "dot", "smoke", "spark",
            "flare", "ring", "drop", "splat",
            "chunk", "shard", "splinter", "fire",
            "dust", "scorch", "bullet", "streak",
        };

        public static SpriteAtlas Atlas
        {
            get
            {
                if (_atlas != null) return _atlas;
                _atlas = Build();
                SpriteLibrary.RegisterAtlas("Particles", _atlas);
                return _atlas;
            }
        }

        private static SpriteAtlas Build()
        {
            int w = Cols * Cell, h = Rows * Cell;
            var img = new Image(w, h);
            var painters = new Dictionary<string, Func<float, float, System.Random, float, (float, float)>>
            {
                ["soft"] = Soft, ["dot"] = Dot, ["smoke"] = Smoke, ["spark"] = Spark,
                ["flare"] = Flare, ["ring"] = Ring, ["drop"] = Drop, ["splat"] = Splat,
                ["chunk"] = Chunk, ["shard"] = Shard, ["splinter"] = Splinter, ["fire"] = Fire,
                ["dust"] = Dust, ["scorch"] = Scorch, ["bullet"] = Bullet, ["streak"] = Streak,
            };
            var tex = (Texture2D)null;
            var rects = new List<(string name, Rect rect)>();
            for (int i = 0; i < Names.Length; i++)
            {
                int cx = (i % Cols) * Cell, cy = (i / Cols) * Cell; // image coordinates, top-left origin
                var rng = new System.Random(100 + i);
                var paint = painters[Names[i]];
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        // normalised coordinates -1..1, v up
                        float u = (x + 0.5f) / Cell * 2f - 1f;
                        float v = 1f - (y + 0.5f) / Cell * 2f;
                        var (lum, alpha) = paint(u, v, rng, i);
                        lum = Mathf.Clamp01(lum); alpha = Mathf.Clamp01(alpha);
                        int o = ((cy + y) * w + cx + x) * 4;
                        byte l = (byte)(lum * 255f);
                        img.Pixels[o] = l; img.Pixels[o + 1] = l; img.Pixels[o + 2] = l;
                        img.Pixels[o + 3] = (byte)(alpha * 255f);
                    }
                // sprite rect: bottom-left origin
                rects.Add((Names[i], new Rect(cx, h - cy - Cell, Cell, Cell)));
            }
            tex = Texture2D.Create(img, FilterMode.Trilinear, TextureWrapMode.Clamp, true, "Particles");
            var atlas = SpriteAtlas.Create("Particles", tex);
            foreach (var (name, rect) in rects) atlas.Add(name, rect, new Vector2(0.5f, 0.5f), Cell);
            return atlas;
        }

        // ------------------------------------------------------------------ shape painters (u, v in -1..1) → (luminance, alpha)

        private static float Len(float u, float v) => MathF.Sqrt(u * u + v * v);
        private static float Smooth(float edge0, float edge1, float x) { float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0)); return t * t * (3f - 2f * t); }

        /// <summary>Radial gaussian glow.</summary>
        private static (float, float) Soft(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float a = MathF.Exp(-d * d * 4.5f) * (1f - Smooth(0.8f, 1f, d));
            return (1f, a);
        }

        /// <summary>Hard disc with a slightly soft rim and a subtle highlight.</summary>
        private static (float, float) Dot(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float a = 1f - Smooth(0.78f, 0.9f, d);
            float lum = 0.82f + 0.18f * (1f - Smooth(0f, 0.7f, Len(u + 0.25f, v - 0.25f)));
            return (lum, a);
        }

        /// <summary>Puffy cloud: fBm noise masked by a radial falloff.</summary>
        private static (float, float) Smoke(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float n = Fbm(u * 2.2f + 10f, v * 2.2f + 3f, (int)seed, 4) * 0.5f + 0.5f;
            float mask = 1f - Smooth(0.25f, 1f, d + (n - 0.5f) * 0.5f);
            float a = mask * (0.45f + 0.55f * n);
            float lum = 0.75f + 0.25f * n;
            return (lum, a * 0.9f);
        }

        /// <summary>Horizontal streak with a hot core (stretched mode sparks).</summary>
        private static (float, float) Spark(float u, float v, System.Random rng, float seed)
        {
            float along = 1f - Smooth(0.7f, 1f, MathF.Abs(u));
            float across = MathF.Exp(-v * v * 40f);
            float core = MathF.Exp(-v * v * 200f) * (1f - Smooth(0.5f, 0.95f, MathF.Abs(u)));
            float a = Mathf.Clamp01(along * across + core);
            return (1f, a);
        }

        /// <summary>Four-point star glow (muzzle/impact flash).</summary>
        private static (float, float) Flare(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float glow = MathF.Exp(-d * d * 6f);
            float arms = MathF.Exp(-MathF.Abs(u) * 14f) * (1f - Smooth(0.3f, 1f, MathF.Abs(v))) + MathF.Exp(-MathF.Abs(v) * 14f) * (1f - Smooth(0.3f, 1f, MathF.Abs(u)));
            float a = Mathf.Clamp01(glow + arms * 0.9f) * (1f - Smooth(0.85f, 1f, d));
            return (1f, a);
        }

        /// <summary>Thin soft circle (shockwave, dust ring).</summary>
        private static (float, float) Ring(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float a = MathF.Exp(-(d - 0.72f) * (d - 0.72f) * 90f);
            return (1f, a * (1f - Smooth(0.9f, 1f, d)));
        }

        /// <summary>Teardrop pointing up (blood / water droplet).</summary>
        private static (float, float) Drop(float u, float v, System.Random rng, float seed)
        {
            // circle at the bottom, tapering tail upwards
            float r = 0.42f;
            float cy = -0.25f;
            float taper = Mathf.Clamp01((v - cy) / 0.95f);
            float width = r * (1f - taper * taper);
            float a = 1f - Smooth(width - 0.06f, width + 0.02f, MathF.Abs(u));
            if (v < cy) a = 1f - Smooth(r - 0.06f, r + 0.02f, Len(u, v - cy));
            if (v > 0.72f) a = 0f;
            float lum = 0.85f + 0.15f * (1f - Smooth(0f, 0.5f, Len(u + 0.15f, v - cy - 0.1f)));
            return (lum, a);
        }

        /// <summary>Irregular blob with satellite droplets (splat decal).</summary>
        private static (float, float) Splat(float u, float v, System.Random rng, float seed)
        {
            float ang = MathF.Atan2(v, u);
            float d = Len(u, v);
            float wobble = 0.55f + 0.18f * MathF.Sin(ang * 5f + seed) + 0.12f * MathF.Sin(ang * 9f + 1.7f) + 0.07f * MathF.Sin(ang * 17f);
            float a = 1f - Smooth(wobble - 0.05f, wobble + 0.03f, d);
            // satellites
            for (int k = 0; k < 7; k++)
            {
                float sa = k * 0.9f + seed * 0.3f;
                float sr = 0.7f + 0.22f * MathF.Sin(k * 2.3f);
                float sx = MathF.Cos(sa) * sr, sy = MathF.Sin(sa) * sr;
                float size = 0.06f + 0.05f * (0.5f + 0.5f * MathF.Sin(k * 1.3f));
                a = MathF.Max(a, 1f - Smooth(size, size + 0.03f, Len(u - sx, v - sy)));
            }
            float lum = 0.7f + 0.3f * (1f - Smooth(0f, 0.6f, d));
            return (lum, a * (1f - Smooth(0.93f, 1f, d)));
        }

        /// <summary>Angular rock / concrete chunk with a lit top edge.</summary>
        private static (float, float) Chunk(float u, float v, System.Random rng, float seed)
        {
            float ang = MathF.Atan2(v, u);
            float d = Len(u, v);
            float radius = 0.5f + 0.16f * MathF.Sin(ang * 3f + seed) + 0.1f * MathF.Sin(ang * 7f - 0.4f);
            float a = 1f - Smooth(radius - 0.03f, radius + 0.01f, d);
            float lum = 0.45f + 0.35f * Mathf.Clamp01(v * 0.8f + 0.5f) + 0.15f * (Fbm(u * 4f, v * 4f, 7, 3) * 0.5f + 0.5f);
            return (lum, a);
        }

        /// <summary>Glass shard: a thin triangle, bright edges, translucent middle.</summary>
        private static (float, float) Shard(float u, float v, System.Random rng, float seed)
        {
            // triangle with vertices (0, 0.85), (-0.45, -0.7), (0.3, -0.8)
            bool inside = InTriangle(u, v, 0f, 0.85f, -0.45f, -0.7f, 0.3f, -0.8f, out float edgeDist);
            if (!inside) return (1f, 0f);
            float edge = 1f - Smooth(0f, 0.08f, edgeDist);
            float a = 0.35f + 0.65f * edge;
            float lum = 0.8f + 0.2f * edge;
            return (lum, a);
        }

        /// <summary>Thin wood splinter (long, slightly curved sliver).</summary>
        private static (float, float) Splinter(float u, float v, System.Random rng, float seed)
        {
            float curve = 0.08f * MathF.Sin(v * 2.5f);
            float halfW = 0.09f * (1f - MathF.Abs(v) * 0.8f) + 0.02f;
            float a = 1f - Smooth(halfW - 0.02f, halfW + 0.01f, MathF.Abs(u - curve));
            if (MathF.Abs(v) > 0.92f) a = 0f;
            float grain = Fbm(u * 10f, v * 40f, 3, 2) * 0.5f + 0.5f;
            float lum = 0.55f + 0.35f * grain;
            return (lum, a);
        }

        /// <summary>Flame lick: noisy blob, bright at the bottom, ragged on top.</summary>
        private static (float, float) Fire(float u, float v, System.Random rng, float seed)
        {
            float n = Fbm(u * 3f + 5f, v * 3f - 9f, 11, 4) * 0.5f + 0.5f;
            float shapeY = Mathf.Clamp01((v + 0.9f) / 1.8f);                  // 0 bottom .. 1 top
            float width = 0.75f * (1f - shapeY * shapeY) + 0.05f;
            float body = 1f - Smooth(width - 0.25f, width + 0.05f, MathF.Abs(u) + (n - 0.5f) * 0.45f * shapeY);
            body *= 1f - Smooth(0.55f, 1f, shapeY + (n - 0.5f) * 0.6f);
            body *= Smooth(-1f, -0.6f, v);
            float lum = 0.6f + 0.4f * (1f - shapeY) + 0.2f * n;
            return (lum, Mathf.Clamp01(body));
        }

        /// <summary>Very soft, grainy puff (dust clouds).</summary>
        private static (float, float) Dust(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float n = Fbm(u * 5f + 20f, v * 5f + 8f, 21, 3) * 0.5f + 0.5f;
            float a = (1f - Smooth(0.2f, 1f, d)) * (0.25f + 0.75f * n) * 0.7f;
            return (0.9f + 0.1f * n, a);
        }

        /// <summary>Dark blotch with a ragged edge (scorch mark decal; tint it black).</summary>
        private static (float, float) Scorch(float u, float v, System.Random rng, float seed)
        {
            float d = Len(u, v);
            float n = Fbm(u * 3f - 4f, v * 3f + 6f, 31, 4) * 0.5f + 0.5f;
            float a = 1f - Smooth(0.35f, 1f, d + (n - 0.5f) * 0.6f);
            a *= 0.55f + 0.45f * n;
            return (0.15f + 0.2f * (1f - a), a);
        }

        /// <summary>Capsule pointing along +u (tracer head / bullet).</summary>
        private static (float, float) Bullet(float u, float v, System.Random rng, float seed)
        {
            float hx = 0.7f, r = 0.18f;
            float cx = Mathf.Clamp(u, -hx, hx);
            float d = Len(u - cx, v);
            float a = 1f - Smooth(r - 0.05f, r + 0.02f, d);
            float lum = 0.7f + 0.3f * (1f - Smooth(0f, r, MathF.Abs(v + 0.05f)));
            return (lum, a);
        }

        /// <summary>Long soft streak fading towards the tail (-u): tracer trails, blood streaks.</summary>
        private static (float, float) Streak(float u, float v, System.Random rng, float seed)
        {
            float head = Mathf.Clamp01((u + 1f) * 0.5f);             // 0 at the tail, 1 at the head
            float across = MathF.Exp(-v * v * 30f * (0.4f + head));
            float along = head * head * (1f - Smooth(0.85f, 1f, u));
            return (1f, Mathf.Clamp01(across * along));
        }

        private static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy, out float edgeDist)
        {
            float d1 = Side(px, py, ax, ay, bx, by), d2 = Side(px, py, bx, by, cx, cy), d3 = Side(px, py, cx, cy, ax, ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            edgeDist = MathF.Min(MathF.Abs(d1) / Len(bx - ax, by - ay), MathF.Min(MathF.Abs(d2) / Len(cx - bx, cy - by), MathF.Abs(d3) / Len(ax - cx, ay - cy)));
            return !(neg && pos);
        }

        private static float Side(float px, float py, float ax, float ay, float bx, float by) => (px - bx) * (ay - by) - (ax - bx) * (py - by);

        // ------------------------------------------------------------------ noise

        private static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x, y, seed + i * 17) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>2D value noise in [-1, 1].</summary>
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
