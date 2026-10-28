using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Behaviours implementing this get a chance to draw screen-space UI every frame.</summary>
    public interface IUiDrawer
    {
        void DrawUi(UiBatch ui);
    }

    /// <summary>
    /// Immediate-mode screen-space sprite batcher (pixel coordinates, origin bottom-left).
    /// Batches consecutive quads sharing a texture into one draw call.
    /// </summary>
    public sealed unsafe class UiBatch : IDisposable
    {
        private const int MaxVerts = 16384;
        private const int MaxIndices = MaxVerts * 3;
        private readonly float[] _verts = new float[MaxVerts * 8];
        private readonly uint[] _indices = new uint[MaxIndices];
        private int _vertCount, _indexCount;
        private Texture2D _currentTexture;
        private uint _vao, _vbo, _ebo;
        private readonly Shader _shader;
        private BitmapFont _font;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Reference resolution used by <see cref="Scale"/> so HUD layouts stay proportional.</summary>
        public float ReferenceHeight = 1080f;
        public float Scale => Height / ReferenceHeight;

        public BitmapFont Font => _font ??= BitmapFont.CreateBuiltin();

        public UiBatch(Shader shader)
        {
            _shader = shader;
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _verts.Length * sizeof(float), null, GL.DYNAMIC_DRAW);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, _indices.Length * sizeof(uint), null, GL.DYNAMIC_DRAW);
            int stride = 8 * sizeof(float);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 2 * sizeof(float));
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, GL.FLOAT, false, stride, 4 * sizeof(float));
            GL.BindVertexArray(0);
        }

        internal void Begin(int width, int height)
        {
            Width = width; Height = height;
            _vertCount = 0; _indexCount = 0;
            _currentTexture = null;
            _clips.Clear();
            GL.Disable(GL.SCISSOR_TEST);
        }

        /// <summary>Draws a sprite stretched into a pixel rectangle.</summary>
        public void Draw(Sprite sprite, Rect dest, Color color)
        {
            sprite.GetUVs(out float u0, out float v0, out float u1, out float v1);
            DrawRaw(sprite.texture, dest, u0, v0, u1, v1, color);
        }

        /// <summary>Draws a sprite at its native pixel size scaled by <paramref name="scale"/>, anchored at its pivot.</summary>
        public void DrawSprite(Sprite sprite, Vector2 pivotPosition, float scale, Color color)
        {
            Vector2 p = sprite.pivot;
            var dest = new Rect(pivotPosition.x - p.x * scale, pivotPosition.y - p.y * scale, sprite.rect.width * scale, sprite.rect.height * scale);
            Draw(sprite, dest, color);
        }

        /// <summary>Draws the sprite clipped to <paramref name="fill"/> (0..1) from the left/bottom, like Image.fillAmount (Horizontal/Vertical).</summary>
        public void DrawFilled(Sprite sprite, Rect dest, float fill, Color color, bool vertical = false)
        {
            fill = Mathf.Clamp01(fill);
            if (fill <= 0f) return;
            sprite.GetUVs(out float u0, out float v0, out float u1, out float v1);
            if (!vertical)
                DrawRaw(sprite.texture, new Rect(dest.x, dest.y, dest.width * fill, dest.height), u0, v0, u0 + (u1 - u0) * fill, v1, color);
            else
                DrawRaw(sprite.texture, new Rect(dest.x, dest.y, dest.width, dest.height * fill), u0, v0, u1, v0 + (v1 - v0) * fill, color);
        }

        /// <summary>
        /// Unity Image "Radial360" fill: a wedge starting at the bottom of the rect sweeping
        /// clockwise (or counter-clockwise) through <paramref name="fill"/> * 360 degrees.
        /// </summary>
        public void DrawRadialFilled(Sprite sprite, Rect dest, float fill, bool clockwise, Color color)
        {
            fill = Mathf.Clamp01(fill);
            if (fill <= 0f) return;
            sprite.GetUVs(out float u0, out float v0, out float u1, out float v1);
            Texture2D tex = sprite.texture;
            Vector2 c = dest.center;
            float hw = dest.width * 0.5f, hh = dest.height * 0.5f;
            Vector2 UvAt(Vector2 p) => new Vector2(u0 + (u1 - u0) * (p.x - dest.x) / dest.width, v0 + (v1 - v0) * (p.y - dest.y) / dest.height);
            Vector2 Edge(float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                Vector2 d = clockwise ? new Vector2(-MathF.Sin(r), -MathF.Cos(r)) : new Vector2(MathF.Sin(r), -MathF.Cos(r));
                float t = MathF.Min(MathF.Abs(d.x) > 1E-06f ? hw / MathF.Abs(d.x) : float.MaxValue, MathF.Abs(d.y) > 1E-06f ? hh / MathF.Abs(d.y) : float.MaxValue);
                return c + d * t;
            }
            float total = fill * 360f;
            const float step = 6f;
            int segments = Mathf.CeilToInt(total / step);
            EnsureCapacity(segments * 3 + 3, segments * 3, tex);
            uint centerIdx = PushVertex(c, UvAt(c), color);
            float prevDeg = 0f;
            Vector2 prevP = Edge(0f);
            uint prevIdx = PushVertex(prevP, UvAt(prevP), color);
            for (int i = 1; i <= segments; i++)
            {
                float deg = MathF.Min(i * step, total);
                // include the corner diagonals so the boundary stays exact on the rectangle
                Vector2 p = Edge(deg);
                uint idx = PushVertex(p, UvAt(p), color);
                PushTriangle(centerIdx, prevIdx, idx);
                prevIdx = idx; prevDeg = deg;
            }
        }

        public void FillRect(Rect dest, Color color) => DrawRaw(Texture2D.White, dest, 0, 0, 1, 1, color);

        /// <summary>Multiplies the framebuffer by <paramref name="color"/> (post-processing colour filter).</summary>
        public void FillRectMultiply(Rect dest, Color color)
        {
            Flush();
            GL.BlendFunc(GL.DST_COLOR, GL.ZERO);
            DrawRaw(Texture2D.White, dest, 0, 0, 1, 1, new Color(color.r, color.g, color.b, 1f));
            Flush();
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        }

        private static Texture2D _vignette;
        /// <summary>Radial darkening towards the screen edges, tinted by <paramref name="color"/> (alpha = intensity).</summary>
        public void DrawVignette(Color color, float intensity)
        {
            if (intensity <= 0.001f) return;
            if (_vignette == null)
            {
                const int n = 256;
                var img = new Image(n, n);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = MathF.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((d - 0.35f) / 0.85f);
                        a = a * a;
                        int o = (y * n + x) * 4;
                        img.Pixels[o] = img.Pixels[o + 1] = img.Pixels[o + 2] = 255;
                        img.Pixels[o + 3] = (byte)(a * 255f);
                    }
                _vignette = Texture2D.Create(img, FilterMode.Bilinear, TextureWrapMode.Clamp, false, "Vignette");
            }
            DrawRaw(_vignette, new Rect(0, 0, Width, Height), 0, 0, 1, 1, new Color(color.r, color.g, color.b, color.a * intensity));
        }

        public void DrawText(string text, Vector2 pos, float scale, Color color) => Font.Draw(this, text, pos, scale, color);

        private readonly Stack<Rect> _clips = new Stack<Rect>();
        /// <summary>Restricts subsequent drawing to <paramref name="r"/> (intersected with the current clip). Pair with <see cref="PopClip"/>.</summary>
        public void PushClip(Rect r)
        {
            if (_clips.Count > 0)
            {
                var c = _clips.Peek();
                float x0 = MathF.Max(r.x, c.x), y0 = MathF.Max(r.y, c.y);
                float x1 = MathF.Min(r.xMax, c.xMax), y1 = MathF.Min(r.yMax, c.yMax);
                r = new Rect(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));
            }
            Flush();
            _clips.Push(r);
            ApplyClip();
        }

        public void PopClip()
        {
            Flush();
            if (_clips.Count > 0) _clips.Pop();
            ApplyClip();
        }

        private void ApplyClip()
        {
            if (_clips.Count == 0) { GL.Disable(GL.SCISSOR_TEST); return; }
            var r = _clips.Peek();
            GL.Enable(GL.SCISSOR_TEST);
            GL.Scissor((int)MathF.Floor(r.x), (int)MathF.Floor(r.y), (int)MathF.Ceiling(r.width), (int)MathF.Ceiling(r.height));
        }

        /// <summary>True when the point is inside the current clip rectangle (or no clip is active).</summary>
        public bool ClipContains(Vector2 p) => _clips.Count == 0 || _clips.Peek().Contains(p);

        public Vector2 MeasureText(string text, float scale) => Font.Measure(text, scale);

        public void DrawRaw(Texture2D tex, Rect dest, float u0, float v0, float u1, float v1, Color color)
        {
            EnsureCapacity(4, 6, tex);
            uint a = PushVertex(new Vector2(dest.x, dest.y), new Vector2(u0, v0), color);
            uint b = PushVertex(new Vector2(dest.x + dest.width, dest.y), new Vector2(u1, v0), color);
            uint cc = PushVertex(new Vector2(dest.x + dest.width, dest.y + dest.height), new Vector2(u1, v1), color);
            uint d = PushVertex(new Vector2(dest.x, dest.y + dest.height), new Vector2(u0, v1), color);
            PushTriangle(a, b, cc);
            PushTriangle(a, cc, d);
        }

        private void EnsureCapacity(int verts, int indices, Texture2D tex)
        {
            if (tex != _currentTexture || _vertCount + verts > MaxVerts || _indexCount + indices > MaxIndices)
            {
                Flush();
                _currentTexture = tex;
            }
        }

        private uint PushVertex(Vector2 p, Vector2 uv, Color color)
        {
            int k = _vertCount * 8;
            _verts[k] = p.x; _verts[k + 1] = p.y; _verts[k + 2] = uv.x; _verts[k + 3] = uv.y;
            _verts[k + 4] = color.r; _verts[k + 5] = color.g; _verts[k + 6] = color.b; _verts[k + 7] = color.a;
            return (uint)_vertCount++;
        }

        private void PushTriangle(uint a, uint b, uint c)
        {
            _indices[_indexCount++] = a; _indices[_indexCount++] = b; _indices[_indexCount++] = c;
        }

        internal void Flush()
        {
            if (_indexCount == 0 || _currentTexture == null) { _vertCount = 0; _indexCount = 0; return; }
            _shader.Use();
            _shader.SetMatrix("uProj", Matrix4x4.Ortho(0, Width, 0, Height, -1, 1));
            _shader.SetInt("uTex", 0);
            _currentTexture.Bind(0);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            fixed (float* p = _verts) GL.BufferSubData(GL.ARRAY_BUFFER, 0, _vertCount * 8 * sizeof(float), p);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            fixed (uint* p = _indices) GL.BufferSubData(GL.ELEMENT_ARRAY_BUFFER, 0, _indexCount * sizeof(uint), p);
            GL.DrawElements(GL.TRIANGLES, _indexCount, GL.UNSIGNED_INT, 0);
            GL.BindVertexArray(0);
            _vertCount = 0; _indexCount = 0;
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
        }
    }
}
