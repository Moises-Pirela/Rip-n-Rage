using System;
using System.Collections.Generic;
using RipNRage.Engine;

namespace RipNRage.Level
{
    /// <summary>
    /// Vertex-paint data of one face: an RGB grid in the face's texture-axis space (u,v in world
    /// units, anchored at u0/v0 with square cells). Independent of the polygon vertices, so brushes
    /// can be moved/resized without losing the paint; cells outside the grid read as white.
    /// </summary>
    public sealed class PaintGrid
    {
        public int width, height;
        public float cell = 1f;
        public float u0, v0;
        public byte[] rgb; // width*height*3, row-major (v rows)

        public PaintGrid(int width, int height, float cell, float u0, float v0)
        {
            this.width = Math.Max(1, width); this.height = Math.Max(1, height); this.cell = cell; this.u0 = u0; this.v0 = v0;
            rgb = new byte[this.width * this.height * 3];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = 255;
        }

        public PaintGrid Clone()
        {
            var g = new PaintGrid(width, height, cell, u0, v0);
            Array.Copy(rgb, g.rgb, rgb.Length);
            return g;
        }

        public bool IsWhite
        {
            get { foreach (var b in rgb) if (b != 255) return false; return true; }
        }

        /// <summary>Creates a grid covering the face polygon at the given cell size (capped at 128 cells per side).</summary>
        public static PaintGrid ForFace(Face f, float cell)
        {
            BrushGeometry.TextureAxes(f.plane.normal, out var u, out var v);
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var p in f.polygon)
            {
                float a = Vector3.Dot(p, u), b = Vector3.Dot(p, v);
                uMin = MathF.Min(uMin, a); uMax = MathF.Max(uMax, a); vMin = MathF.Min(vMin, b); vMax = MathF.Max(vMax, b);
            }
            if (uMin == float.MaxValue) return new PaintGrid(1, 1, cell, 0, 0);
            int w = Math.Clamp((int)MathF.Ceiling((uMax - uMin) / cell) + 1, 1, 128);
            int h = Math.Clamp((int)MathF.Ceiling((vMax - vMin) / cell) + 1, 1, 128);
            // recompute the effective cell size if the cap was hit so the grid still covers the face
            float c = MathF.Max(cell, MathF.Max((uMax - uMin) / 127f, (vMax - vMin) / 127f));
            return new PaintGrid(w, h, c, uMin, vMin);
        }

        /// <summary>Bilinear sample at texture-space coordinates (world units).</summary>
        public Color Sample(float u, float v)
        {
            float fx = (u - u0) / cell, fy = (v - v0) / cell;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            Color c00 = Get(x0, y0), c10 = Get(x0 + 1, y0), c01 = Get(x0, y0 + 1), c11 = Get(x0 + 1, y0 + 1);
            return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
        }

        public Color Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return Color.white;
            int o = (y * width + x) * 3;
            return new Color(rgb[o] / 255f, rgb[o + 1] / 255f, rgb[o + 2] / 255f, 1f);
        }

        public void Set(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int o = (y * width + x) * 3;
            rgb[o] = (byte)(Mathf.Clamp01(c.r) * 255f); rgb[o + 1] = (byte)(Mathf.Clamp01(c.g) * 255f); rgb[o + 2] = (byte)(Mathf.Clamp01(c.b) * 255f);
        }

        /// <summary>Blends <paramref name="color"/> into the cells within <paramref name="radius"/> of (u,v) with a smooth falloff.</summary>
        public void Splat(float u, float v, float radius, Color color, float strength)
        {
            int cx0 = (int)MathF.Floor((u - radius - u0) / cell), cx1 = (int)MathF.Ceiling((u + radius - u0) / cell);
            int cy0 = (int)MathF.Floor((v - radius - v0) / cell), cy1 = (int)MathF.Ceiling((v + radius - v0) / cell);
            for (int y = Math.Max(0, cy0); y <= Math.Min(height - 1, cy1); y++)
                for (int x = Math.Max(0, cx0); x <= Math.Min(width - 1, cx1); x++)
                {
                    float du = u0 + x * cell - u, dv = v0 + y * cell - v;
                    float d = MathF.Sqrt(du * du + dv * dv);
                    if (d > radius) continue;
                    float w = 1f - d / radius;
                    w = w * w * (3f - 2f * w) * strength;
                    Set(x, y, Color.Lerp(Get(x, y), color, Mathf.Clamp01(w)));
                }
        }

        /// <summary>URL-safe base64 ('-' and '_'): standard base64 contains "//" which the .map tokenizer treats as a comment.</summary>
        public string Encode() => Convert.ToBase64String(rgb).Replace('+', '-').Replace('/', '_');

        public static PaintGrid Decode(int w, int h, float cell, float u0, float v0, string base64)
        {
            var g = new PaintGrid(w, h, cell, u0, v0);
            try
            {
                var data = Convert.FromBase64String(base64.Replace('-', '+').Replace('_', '/'));
                Array.Copy(data, g.rgb, Math.Min(data.Length, g.rgb.Length));
            }
            catch (FormatException) { }
            return g;
        }
    }
}
