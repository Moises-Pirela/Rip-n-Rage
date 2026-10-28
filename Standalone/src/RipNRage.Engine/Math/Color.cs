using System;

namespace RipNRage.Engine
{
    [Serializable]
    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }

        public static readonly Color white = new Color(1, 1, 1, 1);
        public static readonly Color black = new Color(0, 0, 0, 1);
        public static readonly Color clear = new Color(0, 0, 0, 0);
        public static readonly Color red = new Color(1, 0, 0, 1);
        public static readonly Color green = new Color(0, 1, 0, 1);
        public static readonly Color blue = new Color(0, 0, 1, 1);
        public static readonly Color yellow = new Color(1, 0.92f, 0.016f, 1);
        public static readonly Color cyan = new Color(0, 1, 1, 1);
        public static readonly Color magenta = new Color(1, 0, 1, 1);
        public static readonly Color gray = new Color(0.5f, 0.5f, 0.5f, 1);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator *(Color a, float f) => new Color(a.r * f, a.g * f, a.b * f, a.a * f);
        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);

        public override string ToString() => $"RGBA({r:0.000}, {g:0.000}, {b:0.000}, {a:0.000})";
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new Vector2(x + width * 0.5f, y + height * 0.5f);
        public Vector2 size => new Vector2(width, height);
        public bool Contains(Vector2 p) => p.x >= x && p.x < x + width && p.y >= y && p.y < y + height;
        public override string ToString() => $"(x:{x}, y:{y}, w:{width}, h:{height})";
    }
}
