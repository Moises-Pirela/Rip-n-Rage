using System;
using System.Collections.Generic;
using RipNRage.Engine;

namespace RipNRage.Level
{
    /// <summary>Convex-brush geometry: face polygons by half-space clipping, Quake texture axes, picking.</summary>
    public static class BrushGeometry
    {
        private const float Huge = 65536f;
        private const float Epsilon = 1E-4f;

        /// <summary>Builds each face's polygon by clipping a huge quad on its plane with every other plane.</summary>
        public static void BuildPolygons(Brush brush)
        {
            foreach (var face in brush.faces)
            {
                var poly = BasePolygon(face.plane);
                foreach (var other in brush.faces)
                {
                    if (other == face) continue;
                    poly = Clip(poly, other.plane);
                    if (poly.Count < 3) break;
                }
                face.polygon = poly.Count >= 3 ? Dedupe(poly) : new List<Vector3>();
            }
        }

        private static List<Vector3> BasePolygon(Plane p)
        {
            Vector3 n = p.normal;
            Vector3 t = MathF.Abs(n.y) < 0.9f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.Cross(n, Vector3.right).normalized;
            Vector3 b = Vector3.Cross(n, t).normalized;
            Vector3 o = n * p.dist;
            // clockwise seen from the front (outside) so front faces match the engine's CW convention
            return new List<Vector3> { o + (-t - b) * Huge, o + (t - b) * Huge, o + (t + b) * Huge, o + (-t + b) * Huge };
        }

        /// <summary>Keeps the part of the polygon behind the plane (Sutherland–Hodgman).</summary>
        public static List<Vector3> Clip(List<Vector3> poly, Plane plane)
        {
            var result = new List<Vector3>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = plane.Distance(a), db = plane.Distance(b);
                bool ina = da <= Epsilon, inb = db <= Epsilon;
                if (ina) result.Add(a);
                if (ina != inb)
                {
                    float t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
            return result;
        }

        private static List<Vector3> Dedupe(List<Vector3> poly)
        {
            var r = new List<Vector3>(poly.Count);
            foreach (var v in poly)
                if (r.Count == 0 || (r[r.Count - 1] - v).sqrMagnitude > 1E-6f) r.Add(v);
            if (r.Count > 1 && (r[0] - r[r.Count - 1]).sqrMagnitude <= 1E-6f) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>Quake texture axes for a face normal: the dominant axis picks the projection plane.</summary>
        public static void TextureAxes(Vector3 n, out Vector3 u, out Vector3 v)
        {
            float ax = MathF.Abs(n.x), ay = MathF.Abs(n.y), az = MathF.Abs(n.z);
            if (ay >= ax && ay >= az) { u = Vector3.right; v = Vector3.forward; }       // floors / ceilings
            else if (ax >= az) { u = Vector3.forward; v = Vector3.up; }                 // walls facing ±x
            else { u = Vector3.right; v = Vector3.up; }                                 // walls facing ±z
        }

        /// <summary>Texture coordinate (in repeats) of a world point on a face.</summary>
        public static Vector2 TexCoord(Face f, Vector3 p)
        {
            TextureAxes(f.plane.normal, out var u, out var v);
            float su = Math.Max(f.scale.x, 1E-4f), sv = Math.Max(f.scale.y, 1E-4f);
            float a = Vector3.Dot(p, u), b = Vector3.Dot(p, v);
            if (f.rotation != 0f)
            {
                float r = f.rotation * Mathf.Deg2Rad, c = MathF.Cos(r), s = MathF.Sin(r);
                float a2 = a * c - b * s, b2 = a * s + b * c;
                a = a2; b = b2;
            }
            return new Vector2(a / su + f.offset.x, b / sv + f.offset.y);
        }

        /// <summary>Fan triangulation of a convex polygon: returns index triples.</summary>
        public static IEnumerable<(int, int, int)> Triangulate(int count)
        {
            for (int i = 1; i + 1 < count; i++) yield return (0, i, i + 1);
        }

        /// <summary>Ray/brush intersection (slab test over the planes). Returns the entry distance and the face hit.</summary>
        public static bool Raycast(Brush brush, Ray ray, out float tEnter, out Face hitFace)
        {
            tEnter = 0f; hitFace = null;
            float tExit = float.MaxValue;
            foreach (var f in brush.faces)
            {
                float denom = Vector3.Dot(f.plane.normal, ray.direction);
                float dist = f.plane.Distance(ray.origin);
                if (MathF.Abs(denom) < 1E-8f)
                {
                    if (dist > Epsilon) return false; // parallel and outside
                    continue;
                }
                float t = -dist / denom;
                if (denom < 0f) { if (t > tEnter) { tEnter = t; hitFace = f; } }  // entering
                else { if (t < tExit) tExit = t; }                                // leaving
                if (tEnter > tExit) return false;
            }
            return hitFace != null && tEnter >= 0f;
        }

        /// <summary>Axis-aligned plane hit used for drawing brushes on the ground (y = height).</summary>
        public static bool RaycastGround(Ray ray, float height, out Vector3 point)
        {
            point = default;
            if (MathF.Abs(ray.direction.y) < 1E-6f) return false;
            float t = (height - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            point = ray.GetPoint(t);
            return true;
        }

        public static Vector3 Snap(Vector3 v, float grid) => grid <= 0f ? v : new Vector3(MathF.Round(v.x / grid) * grid, MathF.Round(v.y / grid) * grid, MathF.Round(v.z / grid) * grid);
    }
}
