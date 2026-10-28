using System;
using System.Diagnostics;

namespace RipNRage.Engine.Graphics.Culling
{
    /// <summary>
    /// Tiny CPU depth buffer used for conservative occlusion culling (in the spirit of Intel's masked
    /// software occlusion culling, but scalar and simple).
    ///
    /// Every frame the level geometry flagged as <see cref="MeshRenderer.occluder"/> is rasterised into a
    /// low-resolution view-space depth buffer (pixel-centre coverage, perspective-correct depth, near-plane
    /// clipping). Candidate renderers are then tested by projecting their world AABB: an object is culled only
    /// when every pixel of its screen rectangle is covered by an occluder nearer than the box's closest point.
    /// Under-covering occluders and over-covering occludees keeps the test conservative (never hides a visible
    /// object), while still rejecting everything behind walls and floors.
    /// </summary>
    public sealed class OcclusionBuffer : IDisposable
    {
        private int _w, _h;
        private float[] _depth = Array.Empty<float>();
        private Matrix4x4 _view;
        private float _near = 0.05f;
        private float _px, _py;        // proj.m00, proj.m11
        private Vector3[] _scratch = new Vector3[1024];
        private readonly Vector3[] _poly = new Vector3[8];
        private readonly Vector3[] _clipped = new Vector3[8];
        private Texture2D _debugTex;
        private Image _debugImage;
        private readonly Stopwatch _sw = new Stopwatch();

        public int Width => _w;
        public int Height => _h;
        public int TrianglesRasterised { get; private set; }
        public int OccludersAdded { get; private set; }
        /// <summary>Milliseconds spent rasterising occluders + testing this frame.</summary>
        public double ElapsedMs => _sw.Elapsed.TotalMilliseconds;
        /// <summary>Skip triangles facing away from the camera (level brushes are closed, so back faces never occlude more than front faces).</summary>
        public bool BackfaceCull = true;

        /// <summary>Clears the buffer and records the camera for this frame.</summary>
        public void Begin(int width, int height, in Matrix4x4 view, in Matrix4x4 proj, float near)
        {
            _sw.Restart();
            if (width != _w || height != _h)
            {
                _w = width; _h = height;
                _depth = new float[_w * _h];
            }
            Array.Fill(_depth, float.PositiveInfinity);
            _view = view;
            _px = proj.m00; _py = proj.m11;
            _near = MathF.Max(near, 1E-4f);
            TrianglesRasterised = 0; OccludersAdded = 0;
        }

        /// <summary>Pauses the timer between the rasterisation and test phases (so ElapsedMs only counts culling work).</summary>
        public void Pause() => _sw.Stop();
        public void Resume() => _sw.Start();

        /// <summary>Rasterises a mesh's triangles (model → view) into the depth buffer.</summary>
        public void AddOccluder(Mesh mesh, in Matrix4x4 model)
        {
            if (mesh == null || mesh.vertices.Length == 0 || mesh.triangles.Length < 3) return;
            var verts = mesh.vertices;
            if (_scratch.Length < verts.Length) _scratch = new Vector3[Math.Max(verts.Length, _scratch.Length * 2)];
            Matrix4x4 mv = _view * model;
            for (int i = 0; i < verts.Length; i++) _scratch[i] = mv.MultiplyPoint3x4(verts[i]);
            var tris = mesh.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
                RasterTriangleView(_scratch[tris[i]], _scratch[tris[i + 1]], _scratch[tris[i + 2]]);
            OccludersAdded++;
        }

        private void RasterTriangleView(in Vector3 a, in Vector3 b, in Vector3 c)
        {
            // trivial reject: entirely behind the near plane
            if (a.z < _near && b.z < _near && c.z < _near) return;
            int n;
            if (a.z >= _near && b.z >= _near && c.z >= _near)
            {
                _poly[0] = a; _poly[1] = b; _poly[2] = c; n = 3;
            }
            else
            {
                _clipped[0] = a; _clipped[1] = b; _clipped[2] = c;
                n = ClipNear(_clipped, 3, _poly);
                if (n < 3) return;
            }
            // project to screen (y up) with 1/z for perspective-correct depth interpolation
            Span<float> sx = stackalloc float[8];
            Span<float> sy = stackalloc float[8];
            Span<float> iz = stackalloc float[8];
            for (int i = 0; i < n; i++)
            {
                float invz = 1f / _poly[i].z;
                sx[i] = (_px * _poly[i].x * invz * 0.5f + 0.5f) * _w;
                sy[i] = (_py * _poly[i].y * invz * 0.5f + 0.5f) * _h;
                iz[i] = invz;
            }
            for (int i = 1; i + 1 < n; i++)
                RasterTriangleScreen(sx[0], sy[0], iz[0], sx[i], sy[i], iz[i], sx[i + 1], sy[i + 1], iz[i + 1]);
        }

        /// <summary>Sutherland–Hodgman clip of a polygon against z >= near.</summary>
        private int ClipNear(Vector3[] src, int n, Vector3[] dst)
        {
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = src[i], q = src[(i + 1) % n];
                bool pin = p.z >= _near, qin = q.z >= _near;
                if (pin) dst[m++] = p;
                if (pin != qin)
                {
                    float t = (_near - p.z) / (q.z - p.z);
                    dst[m++] = p + (q - p) * t;
                }
            }
            return m;
        }

        private void RasterTriangleScreen(float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            float area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
            if (area == 0f || float.IsNaN(area)) return;
            // the engine's front faces are clockwise on screen (GL.FrontFace(CW)) → negative signed area with y up
            if (BackfaceCull && area > 0f) return;
            TrianglesRasterised++;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
            int maxX = Math.Min(_w - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))));
            int maxY = Math.Min(_h - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
            if (minX > maxX || minY > maxY) return;
            float invArea = 1f / area;
            // edge function increments
            float e0dx = (y1 - y2), e0dy = (x2 - x1); // w0 (for vertex 0) = edge(1,2)
            float e1dx = (y2 - y0), e1dy = (x0 - x2); // w1 = edge(2,0)
            float e2dx = (y0 - y1), e2dy = (x1 - x0); // w2 = edge(0,1)
            float px0 = minX + 0.5f, py0 = minY + 0.5f;
            float w0row = (x1 - px0) * (y2 - py0) - (y1 - py0) * (x2 - px0);
            float w1row = (x2 - px0) * (y0 - py0) - (y2 - py0) * (x0 - px0);
            float w2row = (x0 - px0) * (y1 - py0) - (y0 - py0) * (x1 - px0);
            // edge functions above are for a point p: w0 = (x1-px)*(y2-py) - (y1-py)*(x2-px); d/dpx = -(y2-py) + (y1-py) = y1 - y2; d/dpy = -(x1-px) + (x2-px) = x2 - x1
            bool neg = area < 0f;
            for (int y = minY; y <= maxY; y++)
            {
                float w0 = w0row, w1 = w1row, w2 = w2row;
                int row = y * _w;
                for (int x = minX; x <= maxX; x++)
                {
                    bool inside = neg ? (w0 <= 0f && w1 <= 0f && w2 <= 0f) : (w0 >= 0f && w1 >= 0f && w2 >= 0f);
                    if (inside)
                    {
                        float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                        float invz = b0 * z0 + b1 * z1 + b2 * z2;
                        if (invz > 0f)
                        {
                            float z = 1f / invz;
                            int idx = row + x;
                            if (z < _depth[idx]) _depth[idx] = z;
                        }
                    }
                    w0 += e0dx; w1 += e1dx; w2 += e2dx;
                }
                w0row += e0dy; w1row += e1dy; w2row += e2dy;
            }
        }

        /// <summary>
        /// Conservative visibility test for a world-space AABB: false only when every covered pixel holds an
        /// occluder nearer than the box's nearest point.
        /// </summary>
        public bool IsVisible(in Vector3 min, in Vector3 max)
        {
            if (_w == 0) return true;
            float rx0 = float.MaxValue, ry0 = float.MaxValue, rx1 = float.MinValue, ry1 = float.MinValue, minZ = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z);
                Vector3 v = _view.MultiplyPoint3x4(c);
                if (v.z < _near) return true; // straddles / behind the near plane: cannot be occluded
                float invz = 1f / v.z;
                float sx = (_px * v.x * invz * 0.5f + 0.5f) * _w;
                float sy = (_py * v.y * invz * 0.5f + 0.5f) * _h;
                if (sx < rx0) rx0 = sx; if (sx > rx1) rx1 = sx;
                if (sy < ry0) ry0 = sy; if (sy > ry1) ry1 = sy;
                if (v.z < minZ) minZ = v.z;
            }
            int x0 = Math.Max(0, (int)MathF.Floor(rx0)), x1 = Math.Min(_w - 1, (int)MathF.Ceiling(rx1));
            int y0 = Math.Max(0, (int)MathF.Floor(ry0)), y1 = Math.Min(_h - 1, (int)MathF.Ceiling(ry1));
            if (x0 > x1 || y0 > y1) return true; // off screen: leave it to the frustum test
            for (int y = y0; y <= y1; y++)
            {
                int row = y * _w;
                for (int x = x0; x <= x1; x++)
                    if (_depth[row + x] >= minZ) return true;
            }
            return false;
        }

        /// <summary>Point-light sphere test: visible unless the whole projected disc is hidden.</summary>
        public bool IsVisible(in Vector3 center, float radius)
        {
            return IsVisible(center - new Vector3(radius, radius, radius), center + new Vector3(radius, radius, radius));
        }

        /// <summary>Uploads the depth buffer as a greyscale texture (white = near, black = empty) for the debug view.</summary>
        public Texture2D GetDebugTexture(float visibleRange = 40f)
        {
            if (_w == 0) return Texture2D.White;
            if (_debugImage == null || _debugImage.Width != _w || _debugImage.Height != _h) _debugImage = new Image(_w, _h);
            var px = _debugImage.Pixels;
            for (int y = 0; y < _h; y++)
            {
                int srcRow = (_h - 1 - y) * _w; // image is top row first
                for (int x = 0; x < _w; x++)
                {
                    float z = _depth[srcRow + x];
                    byte v = 0;
                    if (!float.IsPositiveInfinity(z)) v = (byte)(255f * Mathf.Clamp01(1f - (z - _near) / visibleRange) * 0.85f + 38f);
                    int o = (y * _w + x) * 4;
                    px[o] = v; px[o + 1] = v; px[o + 2] = v; px[o + 3] = 255;
                }
            }
            _debugTex?.Dispose();
            _debugTex = Texture2D.Create(_debugImage, FilterMode.Point, TextureWrapMode.Clamp, false, "OcclusionDebug");
            return _debugTex;
        }

        public void Dispose()
        {
            _debugTex?.Dispose();
            _debugTex = null;
        }
    }
}
