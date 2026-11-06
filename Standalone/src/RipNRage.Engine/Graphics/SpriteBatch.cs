using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Batches world sprites that share a texture, material and lighting state into one draw: their quads (or
    /// tight polygon meshes) are written in world space into one vertex buffer — position, uv, per-vertex tint and
    /// the billboard's lighting normal — and uploaded once per flush with an orphaning <c>glBufferData</c>, so a
    /// thousand ragdoll pieces cost a handful of draw calls instead of a thousand buffer updates (each of which
    /// stalls on drivers that synchronise on <c>glBufferSubData</c>). Sprites with a custom material or a non-empty
    /// property block (hit flash, dissolve) are not batchable and are drawn one by one in order between batches.
    /// A batch is also closed when its bounds would grow past <see cref="MaxExtent"/>, so per-draw light selection
    /// stays local.
    /// </summary>
    public sealed unsafe class SpriteBatch : IDisposable
    {
        public const int Stride = 12; // floats: pos3 uv2 color4 normal3
        /// <summary>Largest edge (m) a batch's world bounds may reach before a new batch starts.</summary>
        public float MaxExtent = 40f;

        public struct Batch
        {
            public Texture2D texture;
            public Material material;
            public bool lit;
            public int indexStart, indexCount;
            public Vector3 min, max;
            public int sprites;
        }

        private float[] _verts = new float[4096 * Stride];
        private uint[] _indices = new uint[4096 * 6 / 4 * 4];
        private int _vertCount, _indexCount;
        private readonly List<Batch> _batches = new List<Batch>(16);
        private Batch _current;
        private bool _open;
        private uint _vao, _vbo, _ebo;
        private int _vboCapacity, _eboCapacity;

        public IReadOnlyList<Batch> Batches => _batches;
        public int SpriteCount { get; private set; }

        public SpriteBatch()
        {
            _vao = GL.GenVertexArray(); _vbo = GL.GenBuffer(); _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            int s = Stride * sizeof(float);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, false, s, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, GL.FLOAT, false, s, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, GL.FLOAT, false, s, 5 * sizeof(float));
            GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 3, GL.FLOAT, false, s, 9 * sizeof(float));
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            GL.BindVertexArray(0);
        }

        /// <summary>A sprite that can go into a batch: default material (the built-in sprite shader), no per-object properties, no mesh larger than the scratch.</summary>
        public static bool Batchable(SpriteRenderer sr)
        {
            var mat = sr.sharedMaterial;
            if (mat != SpriteRenderer.DefaultSpriteMaterial && (mat == null || mat.shader != SpriteRenderer.DefaultSpriteMaterial.shader || !mat.IsEmpty)) return false;
            return sr.propertyBlock == null || sr.propertyBlock.IsEmpty;
        }

        public void Begin()
        {
            _vertCount = 0; _indexCount = 0; _batches.Clear(); _open = false; SpriteCount = 0;
        }

        /// <summary>Appends a sprite (world-space geometry, billboarded towards <paramref name="cam"/>); starts a new batch when the state differs.</summary>
        public void Add(SpriteRenderer sr, Camera cam, bool lit, Vector3 normal)
        {
            Sprite sp = sr.sprite;
            var mat = sr.sharedMaterial;
            Vector3 pos = sr.transform.position;
            Matrix4x4 model;
            if (sr.billboard)
            {
                Vector3 toCam = cam.transform.position - pos; toCam.y = 0f;
                Quaternion rot = toCam.sqrMagnitude > 1E-06f ? Quaternion.LookRotation(-toCam.normalized) : Quaternion.identity;
                if (sr.billboardRoll != 0f) rot = rot * Quaternion.AngleAxis(sr.billboardRoll, Vector3.forward);
                model = Matrix4x4.TRS(pos, rot, sr.transform.lossyScale);
            }
            else model = sr.transform.localToWorldMatrix;

            int n = sp.HasMesh && !sr.flipX && !sr.flipY ? sp.vertices.Length : 4;
            int ni = sp.HasMesh && !sr.flipX && !sr.flipY ? sp.triangles.Length : 6;
            Ensure(n, ni);

            // first pass over the vertices to get the bounds (cheap: n is small)
            Vector3 bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bmax = -bmin;
            int baseVert = _vertCount;
            Color tint = sr.color;
            if (n == 4)
            {
                sp.GetLocalCorners(out Vector2 min, out Vector2 max);
                sp.GetUVs(out float u0, out float v0, out float u1, out float v1);
                if (sr.flipX) { float t = u0; u0 = u1; u1 = t; }
                if (sr.flipY) { float t = v0; v0 = v1; v1 = t; }
                Put(model, min.x, min.y, u0, v0, tint, normal, ref bmin, ref bmax);
                Put(model, max.x, min.y, u1, v0, tint, normal, ref bmin, ref bmax);
                Put(model, min.x, max.y, u0, v1, tint, normal, ref bmin, ref bmax);
                Put(model, max.x, max.y, u1, v1, tint, normal, ref bmin, ref bmax);
                uint b = (uint)baseVert;
                _indices[_indexCount++] = b; _indices[_indexCount++] = b + 1; _indices[_indexCount++] = b + 2;
                _indices[_indexCount++] = b + 1; _indices[_indexCount++] = b + 3; _indices[_indexCount++] = b + 2;
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    sp.GetMeshVertex(i, out Vector2 p, out Vector2 uv);
                    Put(model, p.x, p.y, uv.x, uv.y, tint, normal, ref bmin, ref bmax);
                }
                uint b = (uint)baseVert;
                for (int i = 0; i < ni; i++) _indices[_indexCount++] = b + (uint)sp.triangles[i];
            }

            bool sameState = _open && _current.texture == sp.texture && _current.material == mat && _current.lit == lit;
            if (sameState)
            {
                Vector3 nmin = Vector3.Min(_current.min, bmin), nmax = Vector3.Max(_current.max, bmax);
                Vector3 size = nmax - nmin;
                if (MathF.Max(size.x, MathF.Max(size.y, size.z)) > MaxExtent) sameState = false;
                else { _current.min = nmin; _current.max = nmax; }
            }
            if (!sameState)
            {
                if (_open) _batches.Add(_current);
                _current = new Batch { texture = sp.texture, material = mat, lit = lit, indexStart = _indexCount - ni, indexCount = 0, min = bmin, max = bmax };
                _open = true;
            }
            _current.indexCount += ni;
            _current.sprites++;
            SpriteCount++;
        }

        private void Put(in Matrix4x4 m, float x, float y, float u, float v, Color c, Vector3 n, ref Vector3 bmin, ref Vector3 bmax)
        {
            Vector3 w = m.MultiplyPoint3x4(new Vector3(x, y, 0f));
            int o = _vertCount * Stride;
            _verts[o] = w.x; _verts[o + 1] = w.y; _verts[o + 2] = w.z;
            _verts[o + 3] = u; _verts[o + 4] = v;
            _verts[o + 5] = c.r; _verts[o + 6] = c.g; _verts[o + 7] = c.b; _verts[o + 8] = c.a;
            _verts[o + 9] = n.x; _verts[o + 10] = n.y; _verts[o + 11] = n.z;
            _vertCount++;
            bmin = Vector3.Min(bmin, w); bmax = Vector3.Max(bmax, w);
        }

        private void Ensure(int verts, int indices)
        {
            if ((_vertCount + verts) * Stride > _verts.Length) Array.Resize(ref _verts, Math.Max(_verts.Length * 2, (_vertCount + verts) * Stride));
            if (_indexCount + indices > _indices.Length) Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + indices));
        }

        /// <summary>Closes the current batch and uploads the geometry (orphaning the buffers: no stall on a buffer still in use).</summary>
        public void End()
        {
            if (_open) { _batches.Add(_current); _open = false; }
            if (_vertCount == 0) return;
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            int vbytes = _vertCount * Stride * sizeof(float);
            fixed (float* p = _verts)
            {
                if (vbytes > _vboCapacity) { GL.BufferData(GL.ARRAY_BUFFER, _verts.Length * sizeof(float), null, GL.STREAM_DRAW); _vboCapacity = _verts.Length * sizeof(float); }
                else GL.BufferData(GL.ARRAY_BUFFER, _vboCapacity, null, GL.STREAM_DRAW); // orphan
                GL.BufferSubData(GL.ARRAY_BUFFER, 0, vbytes, p);
            }
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            int ibytes = _indexCount * sizeof(uint);
            fixed (uint* p = _indices)
            {
                if (ibytes > _eboCapacity) { GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, _indices.Length * sizeof(uint), null, GL.STREAM_DRAW); _eboCapacity = _indices.Length * sizeof(uint); }
                else GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, _eboCapacity, null, GL.STREAM_DRAW);
                GL.BufferSubData(GL.ELEMENT_ARRAY_BUFFER, 0, ibytes, p);
            }
            GL.BindVertexArray(0);
        }

        /// <summary>Binds the batch geometry; call <see cref="Draw"/> per batch afterwards.</summary>
        public void Bind() => GL.BindVertexArray(_vao);

        public void Draw(in Batch b)
        {
            GL.DrawElements(GL.TRIANGLES, b.indexCount, GL.UNSIGNED_INT, b.indexStart * sizeof(uint));
        }

        public void Dispose()
        {
            if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
            if (_ebo != 0) { GL.DeleteBuffer(_ebo); _ebo = 0; }
        }
    }
}
