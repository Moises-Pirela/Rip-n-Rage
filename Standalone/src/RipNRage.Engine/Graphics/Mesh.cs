using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Static triangle mesh (position / normal / uv) uploaded to a VAO.</summary>
    public sealed class Mesh : EngineObject, IDisposable
    {
        public Vector3[] vertices = Array.Empty<Vector3>();
        public Vector3[] normals = Array.Empty<Vector3>();
        public Vector2[] uv = Array.Empty<Vector2>();
        /// <summary>Optional per-vertex colours (vertex painting); null/short = white.</summary>
        public Color[] colors;
        public int[] triangles = Array.Empty<int>();

        public Vector3 boundsMin, boundsMax;

        private uint _vao, _vbo, _ebo;
        private int _indexCount;
        private bool _uploaded;

        public int IndexCount => _indexCount;

        public void RecalculateNormals()
        {
            normals = new Vector3[vertices.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
        }

        public void RecalculateBounds()
        {
            if (vertices.Length == 0) { boundsMin = boundsMax = Vector3.zero; return; }
            boundsMin = boundsMax = vertices[0];
            foreach (var v in vertices) { boundsMin = Vector3.Min(boundsMin, v); boundsMax = Vector3.Max(boundsMax, v); }
        }

        public unsafe void Upload()
        {
            if (_uploaded) Dispose();
            int n = vertices.Length;
            const int S = 12;
            float[] data = new float[n * S];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = vertices[i];
                Vector3 nn = i < normals.Length ? normals[i] : Vector3.up;
                Vector2 t = i < uv.Length ? uv[i] : Vector2.zero;
                Color c = colors != null && i < colors.Length ? colors[i] : Color.white;
                data[i * S] = p.x; data[i * S + 1] = p.y; data[i * S + 2] = p.z;
                data[i * S + 3] = nn.x; data[i * S + 4] = nn.y; data[i * S + 5] = nn.z;
                data[i * S + 6] = t.x; data[i * S + 7] = t.y;
                data[i * S + 8] = c.r; data[i * S + 9] = c.g; data[i * S + 10] = c.b; data[i * S + 11] = c.a;
            }
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            fixed (float* p = data) GL.BufferData(GL.ARRAY_BUFFER, data.Length * sizeof(float), p, GL.STATIC_DRAW);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            uint[] idx = new uint[triangles.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = (uint)triangles[i];
            fixed (uint* p = idx) GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL.STATIC_DRAW);
            int stride = S * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, GL.FLOAT, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 4, GL.FLOAT, false, stride, 8 * sizeof(float));
            GL.BindVertexArray(0);
            _indexCount = idx.Length;
            _uploaded = true;
            RecalculateBounds();
        }

        public void Draw()
        {
            if (!_uploaded) Upload();
            GL.BindVertexArray(_vao);
            GL.DrawElements(GL.TRIANGLES, _indexCount, GL.UNSIGNED_INT, 0);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (!_uploaded) return;
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
            _uploaded = false;
        }

        // ---------------------------------------------------------------- builders

        /// <summary>Axis-aligned box centred at <paramref name="center"/>. UVs in world units / texelsPerUnit.</summary>
        public static Mesh CreateBox(Vector3 center, Vector3 size, float uvScale = 1f)
        {
            var m = new Mesh { name = "Box" };
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 n, Vector3 u, Vector3 v, float su, float sv)
            {
                int b = verts.Count;
                Vector3 c = center + Vector3.Scale(n, h);
                Vector3 uu = Vector3.Scale(u, h), vv = Vector3.Scale(v, h);
                verts.Add(c - uu - vv); uvs.Add(new Vector2(0, 0));
                verts.Add(c + uu - vv); uvs.Add(new Vector2(su * uvScale, 0));
                verts.Add(c + uu + vv); uvs.Add(new Vector2(su * uvScale, sv * uvScale));
                verts.Add(c - uu + vv); uvs.Add(new Vector2(0, sv * uvScale));
                for (int i = 0; i < 4; i++) norms.Add(n);
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            // For each face u x v must equal -n so the (0,2,1)/(0,3,2) winding is clockwise from outside.
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            m.vertices = verts.ToArray(); m.normals = norms.ToArray(); m.uv = uvs.ToArray(); m.triangles = tris.ToArray();
            return m;
        }

        /// <summary>Horizontal quad (XZ plane, facing up).</summary>
        public static Mesh CreatePlane(Vector3 center, float sizeX, float sizeZ, float uvScale = 1f)
        {
            var m = new Mesh { name = "Plane" };
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
            m.vertices = new[]
            {
                center + new Vector3(-hx, 0, -hz), center + new Vector3(hx, 0, -hz),
                center + new Vector3(hx, 0, hz), center + new Vector3(-hx, 0, hz)
            };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { new Vector2(0, 0), new Vector2(sizeX * uvScale, 0), new Vector2(sizeX * uvScale, sizeZ * uvScale), new Vector2(0, sizeZ * uvScale) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        /// <summary>
        /// Loads a Wavefront OBJ (triangulates polygons). <paramref name="scale"/> converts the
        /// file's units to world units; <paramref name="uvScale"/> scales the texture coordinates
        /// (TrenchBroom exports UVs in texels). <paramref name="mirrorX"/> negates X to convert the
        /// right-handed OBJ space into the left-handed world, exactly like Unity's importer does.
        /// </summary>
        public static Mesh LoadObj(string path, float scale = 1f, float uvScale = 1f, bool mirrorX = true)
        {
            var positions = new List<Vector3>(); var texcoords = new List<Vector2>(); var normalsIn = new List<Vector3>();
            var outVerts = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUVs = new List<Vector2>(); var tris = new List<int>();
            var cache = new Dictionary<string, int>();

            foreach (var raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v":
                        positions.Add(new Vector3(F(p[1]) * scale * (mirrorX ? -1f : 1f), F(p[2]) * scale, F(p[3]) * scale));
                        break;
                    case "vt":
                        texcoords.Add(new Vector2(F(p[1]) * uvScale, F(p[2]) * uvScale));
                        break;
                    case "vn":
                        normalsIn.Add(new Vector3(F(p[1]) * (mirrorX ? -1f : 1f), F(p[2]), F(p[3])));
                        break;
                    case "f":
                    {
                        var face = new List<int>();
                        for (int i = 1; i < p.Length; i++)
                        {
                            if (!cache.TryGetValue(p[i], out int idx))
                            {
                                var parts = p[i].Split('/');
                                int vi = int.Parse(parts[0], CultureInfo.InvariantCulture) - 1;
                                int ti = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1], CultureInfo.InvariantCulture) - 1 : -1;
                                int ni = parts.Length > 2 && parts[2].Length > 0 ? int.Parse(parts[2], CultureInfo.InvariantCulture) - 1 : -1;
                                idx = outVerts.Count;
                                outVerts.Add(positions[vi]);
                                outUVs.Add(ti >= 0 ? texcoords[ti] : Vector2.zero);
                                outNormals.Add(ni >= 0 ? normalsIn[ni] : Vector3.up);
                                cache[p[i]] = idx;
                            }
                            face.Add(idx);
                        }
                        for (int i = 1; i + 1 < face.Count; i++)
                        {
                            // OBJ is right-handed/CCW. Interpreting the raw coordinates in our left-handed world
                            // already yields clockwise front faces; mirroring X flips that, so swap the winding back.
                            if (mirrorX) { tris.Add(face[0]); tris.Add(face[i + 1]); tris.Add(face[i]); }
                            else { tris.Add(face[0]); tris.Add(face[i]); tris.Add(face[i + 1]); }
                        }
                        break;
                    }
                }
            }

            var m = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            m.vertices = outVerts.ToArray(); m.normals = outNormals.ToArray(); m.uv = outUVs.ToArray(); m.triangles = tris.ToArray();
            if (normalsIn.Count == 0) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
