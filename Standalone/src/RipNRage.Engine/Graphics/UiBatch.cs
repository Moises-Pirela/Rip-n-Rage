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
    /// Optional companion of <see cref="IUiDrawer"/>: drawers are sorted by this value (stable, ascending)
    /// before drawing, so full-screen overlays (death screen, menus) reliably draw on top of the HUD
    /// regardless of the order their GameObjects were created in. Drawers without it use <see cref="UiLayers.Default"/>.
    /// </summary>
    public interface IUiLayer
    {
        int UiLayer { get; }
    }

    /// <summary>Well-known UI layer values (lower draws first).</summary>
    public static class UiLayers
    {
        public const int Background = -100;
        public const int Default = 0;
        public const int Hud = 0;
        public const int Debug = 50;
        public const int Overlay = 100;
        public const int Menu = 200;
        public const int Modal = 300;
    }

    /// <summary>
    /// Immediate-mode screen-space sprite batcher (pixel coordinates, origin bottom-left).
    /// Batches consecutive quads sharing a texture into one draw call.
    /// </summary>
    public sealed unsafe class UiBatch : IDisposable
    {
        // The whole pass is recorded on the CPU and submitted with ONE buffer upload in Flush(). Uploading per draw
        // call made the driver wait for the GPU to finish reading the previous contents of the buffer (~0.4 ms per
        // draw call on macOS, 19 ms a frame with the profiler overlay); one upload into fresh storage never waits.
        private float[] _verts = new float[16384 * 8];
        private uint[] _indices = new uint[16384 * 6];
        private int _vertCount, _indexCount;

        /// <summary>A recorded draw (a run of indices sharing a texture) or a scissor change.</summary>
        private struct Command
        {
            public Texture2D texture;   // null = scissor command
            public int indexStart, indexCount;
            public bool clip;            // scissor commands: enable + rect, or disable
            public int sx, sy, sw, sh;
        }
        private Command[] _commands = new Command[64];
        private int _commandCount;
        private int _runStart;           // first index of the draw being accumulated
        private bool _stateBound;
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

        // ---- statistics (read by the profiler summary): draw calls and geometry submitted per frame
        public static long TotalFlushes, TotalQuads, TotalBytes, TotalFrames;
        public int FlushesThisFrame { get; private set; }

        public UiBatch(Shader shader)
        {
            _shader = shader;
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            int stride = 8 * sizeof(float);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 2 * sizeof(float));
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, GL.FLOAT, false, stride, 4 * sizeof(float));
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Retargets the batch at a framebuffer of this size (pixels). Public so an editor can run a second UI pass
        /// into an offscreen <see cref="PostFx.RenderTarget"/> — an ImGui viewport panel does this.
        /// </summary>
        public void Begin(int width, int height)
        {
            Width = width; Height = height;
            _vertCount = 0; _indexCount = 0; _commandCount = 0; _runStart = 0;
            _currentTexture = null;
            _stateBound = false;
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

        /// <summary>Solid rectangle. Samples the white texel in the font atlas, so it batches with text.</summary>
        public void FillRect(Rect dest, Color color)
        {
            var f = Font;
            DrawRaw(f.Texture, dest, f.WhiteU, f.WhiteV, f.WhiteU, f.WhiteV, color);
        }

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
            _clips.Push(r);
            RecordClip();
        }

        public void PopClip()
        {
            if (_clips.Count > 0) _clips.Pop();
            RecordClip();
        }

        /// <summary>Ends the current draw and records the scissor state now on top of the clip stack.</summary>
        private void RecordClip()
        {
            CloseRun();
            var c = new Command { clip = _clips.Count > 0 };
            if (c.clip)
            {
                var r = _clips.Peek();
                c.sx = (int)MathF.Floor(r.x); c.sy = (int)MathF.Floor(r.y);
                c.sw = (int)MathF.Ceiling(r.width); c.sh = (int)MathF.Ceiling(r.height);
            }
            AddCommand(c);
        }

        /// <summary>True when the point is inside the current clip rectangle (or no clip is active).</summary>
        public bool ClipContains(Vector2 p) => _clips.Count == 0 || _clips.Peek().Contains(p);

        public Vector2 MeasureText(string text, float scale) => Font.Measure(text, scale);

        /// <summary>Solid line segment of the given thickness (pixels).</summary>
        public void DrawLine(Vector2 a, Vector2 b, float thickness, Color color)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1E-04f) return;
            Vector2 n = new Vector2(-d.y / len, d.x / len) * (thickness * 0.5f);
            var f = Font;
            var white = new Vector2(f.WhiteU, f.WhiteV);
            EnsureCapacity(4, 6, f.Texture);
            uint i0 = PushVertex(a + n, white, color);
            uint i1 = PushVertex(b + n, white, color);
            uint i2 = PushVertex(b - n, white, color);
            uint i3 = PushVertex(a - n, white, color);
            PushTriangle(i0, i1, i2);
            PushTriangle(i0, i2, i3);
        }

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
            if (tex != _currentTexture) { CloseRun(); _currentTexture = tex; }
            if (_vertCount + verts > _verts.Length / 8) Array.Resize(ref _verts, Math.Max(_verts.Length * 2, (_vertCount + verts) * 8));
            if (_indexCount + indices > _indices.Length) Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + indices));
        }

        /// <summary>Records the indices accumulated since the last texture / scissor change as one draw.</summary>
        private void CloseRun()
        {
            int count = _indexCount - _runStart;
            if (count > 0 && _currentTexture != null)
                AddCommand(new Command { texture = _currentTexture, indexStart = _runStart, indexCount = count });
            _runStart = _indexCount;
        }

        private void AddCommand(in Command c)
        {
            if (_commandCount == _commands.Length) Array.Resize(ref _commands, _commands.Length * 2);
            _commands[_commandCount++] = c;
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

        /// <summary>
        /// Submits everything recorded since <see cref="Begin"/> (or the last Flush): one upload of the whole pass's
        /// vertices and indices into fresh buffer storage, then the recorded draws and scissor changes in order.
        /// </summary>
        public void Flush()
        {
            CloseRun();
            if (_commandCount == 0) { _vertCount = 0; _indexCount = 0; _runStart = 0; return; }

            GL.BindVertexArray(_vao); // the VAO holds the element buffer binding
            if (_indexCount > 0)
            {
                // BufferData with data = orphan + upload in one call: the driver hands back new storage, so this never
                // waits for draws from the previous frame that still read the old contents
                GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
                fixed (float* p = _verts) GL.BufferData(GL.ARRAY_BUFFER, (nint)_vertCount * 8 * sizeof(float), p, GL.STREAM_DRAW);
                fixed (uint* p = _indices) GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, (nint)_indexCount * sizeof(uint), p, GL.STREAM_DRAW);
                TotalBytes += _vertCount * 8 * sizeof(float) + _indexCount * sizeof(uint);
            }
            _shader.Use(); // cheap; the program may have changed since the last pass
            if (!_stateBound)
            {
                _shader.SetMatrix("uProj", Matrix4x4.Ortho(0, Width, 0, Height, -1, 1));
                _shader.SetInt("uTex", 0);
                _stateBound = true;
            }

            Texture2D bound = null;
            for (int i = 0; i < _commandCount; i++)
            {
                ref Command c = ref _commands[i];
                if (c.texture == null)
                {
                    if (c.clip) { GL.Enable(GL.SCISSOR_TEST); GL.Scissor(c.sx, c.sy, Math.Max(0, c.sw), Math.Max(0, c.sh)); }
                    else GL.Disable(GL.SCISSOR_TEST);
                    continue;
                }
                if (c.texture != bound) { c.texture.Bind(0); bound = c.texture; }
                GL.DrawElements(GL.TRIANGLES, c.indexCount, GL.UNSIGNED_INT, c.indexStart * sizeof(uint));
                FlushesThisFrame++; TotalFlushes++; TotalQuads += c.indexCount / 6;
            }
            GL.BindVertexArray(0);

            _vertCount = 0; _indexCount = 0; _runStart = 0; _commandCount = 0;
            _currentTexture = null;
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
        }
    }
}
