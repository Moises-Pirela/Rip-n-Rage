using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;

namespace RipNRage.Level
{
    /// <summary>Turns brushes into renderable meshes, one mesh per texture name (batched), with Quake texture projection.</summary>
    public static class LevelMeshBuilder
    {
        private sealed class Acc
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<Color> col = new List<Color>(); public readonly List<int> tris = new List<int>();
        }

        public static Dictionary<string, Mesh> Build(IEnumerable<Brush> brushes, bool skipClip = true)
        {
            var result = new Dictionary<string, Mesh>();
            foreach (var (tex, mesh) in BuildChunked(brushes, 0f, skipClip)) result[tex] = mesh;
            return result;
        }

        /// <summary>
        /// One mesh per (texture, spatial chunk). Brushes are binned by their centre into cubes of <paramref name="chunkSize"/>
        /// metres (0 = a single chunk) so the renderer's frustum/occlusion culling can skip whole parts of the level.
        /// </summary>
        public static List<(string texture, Mesh mesh)> BuildChunked(IEnumerable<Brush> brushes, float chunkSize, bool skipClip = true)
        {
            var byKey = new Dictionary<(string, int, int, int), Acc>();
            var order = new List<(string, int, int, int)>();
            foreach (var b in brushes)
            {
                if (skipClip && b.IsClip) continue;
                int cx = 0, cy = 0, cz = 0;
                if (chunkSize > 0f)
                {
                    Vector3 c = b.Center;
                    cx = (int)MathF.Floor(c.x / chunkSize); cy = (int)MathF.Floor(c.y / chunkSize); cz = (int)MathF.Floor(c.z / chunkSize);
                }
                foreach (var f in b.faces)
                {
                    if (f.polygon.Count < 3) continue;
                    if (skipClip && f.texture.Equals("clip", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var key = (f.texture, cx, cy, cz);
                    if (!byKey.TryGetValue(key, out var acc)) { byKey[key] = acc = new Acc(); order.Add(key); }
                    AppendFace(f, acc);
                }
            }
            var result = new List<(string, Mesh)>();
            foreach (var key in order)
            {
                var a = byKey[key];
                var m = new Mesh { name = "level_" + key.Item1, vertices = a.v.ToArray(), normals = a.n.ToArray(), uv = a.uv.ToArray(), colors = a.col.ToArray(), triangles = a.tris.ToArray() };
                m.RecalculateBounds();
                result.Add((key.Item1, m));
            }
            return result;
        }

        /// <summary>Appends a face: a single polygon when unpainted, or a grid of cells (each clipped to the polygon) carrying the paint colours.</summary>
        private static void AppendFace(Face f, Acc acc)
        {
            if (f.paint == null || f.paint.IsWhite)
            {
                AddPolygon(f, f.polygon, acc, null);
                return;
            }
            var g = f.paint;
            BrushGeometry.TextureAxes(f.plane.normal, out var u, out var v);
            // polygon extents in (u,v)
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (var p in f.polygon)
            {
                float a = Vector3.Dot(p, u), bb = Vector3.Dot(p, v);
                uMin = MathF.Min(uMin, a); uMax = MathF.Max(uMax, a); vMin = MathF.Min(vMin, bb); vMax = MathF.Max(vMax, bb);
            }
            // edge half-planes (in the face plane) to clip cells against the polygon
            var edgePlanes = new List<Plane>();
            for (int i = 0; i < f.polygon.Count; i++)
            {
                Vector3 a = f.polygon[i], bb = f.polygon[(i + 1) % f.polygon.Count];
                Vector3 edgeN = Vector3.Cross(f.plane.normal, bb - a).normalized; // points outward for clockwise polygons
                if (Vector3.Dot(edgeN, f.polygon[(i + 2) % f.polygon.Count] - a) > 0f) edgeN = -edgeN;
                edgePlanes.Add(new Plane(edgeN, Vector3.Dot(edgeN, a)));
            }
            float cell = g.cell;
            float startU = MathF.Floor(uMin / cell) * cell, startV = MathF.Floor(vMin / cell) * cell;
            Vector3 origin = f.plane.normal * f.plane.dist;
            Vector3 Point(float cu, float cv)
            {
                Vector3 p = origin + u * (cu - Vector3.Dot(origin, u)) + v * (cv - Vector3.Dot(origin, v));
                return f.plane.Project(p);
            }
            for (float cu = startU; cu < uMax; cu += cell)
                for (float cv = startV; cv < vMax; cv += cell)
                {
                    var quad = new List<Vector3> { Point(cu, cv), Point(cu, cv + cell), Point(cu + cell, cv + cell), Point(cu + cell, cv) };
                    // match the face polygon's winding (clockwise from outside: edge cross product points along the normal)
                    if (Vector3.Dot(Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]), f.plane.normal) < 0f) quad.Reverse();
                    foreach (var pl in edgePlanes) { quad = BrushGeometry.Clip(quad, pl); if (quad.Count < 3) break; }
                    if (quad.Count < 3) continue;
                    AddPolygon(f, quad, acc, g);
                }
        }

        private static void AddPolygon(Face f, List<Vector3> poly, Acc acc, PaintGrid g)
        {
            int baseIdx = acc.v.Count;
            BrushGeometry.TextureAxes(f.plane.normal, out var u, out var v);
            foreach (var p in poly)
            {
                acc.v.Add(p);
                acc.n.Add(f.plane.normal);
                acc.uv.Add(BrushGeometry.TexCoord(f, p));
                acc.col.Add(g != null ? g.Sample(Vector3.Dot(p, u), Vector3.Dot(p, v)) : Color.white);
            }
            foreach (var (a, bb, c) in BrushGeometry.Triangulate(poly.Count))
            {
                acc.tris.Add(baseIdx + a); acc.tris.Add(baseIdx + bb); acc.tris.Add(baseIdx + c);
            }
        }

        /// <summary>Mesh for a single brush (editor previews / selection).</summary>
        public static Mesh BuildSingle(Brush b)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            foreach (var f in b.faces)
            {
                if (f.polygon.Count < 3) continue;
                int baseIdx = v.Count;
                foreach (var p in f.polygon) { v.Add(p); n.Add(f.plane.normal); uv.Add(BrushGeometry.TexCoord(f, p)); }
                foreach (var (a, bb, c) in BrushGeometry.Triangulate(f.polygon.Count)) { tris.Add(baseIdx + a); tris.Add(baseIdx + bb); tris.Add(baseIdx + c); }
            }
            var m = new Mesh { name = "brush", vertices = v.ToArray(), normals = n.ToArray(), uv = uv.ToArray(), triangles = tris.ToArray() };
            m.RecalculateBounds();
            return m;
        }
    }
}
