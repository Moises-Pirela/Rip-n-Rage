using System;
using System.Collections.Generic;
using System.Globalization;
using RipNRage.Engine;

namespace RipNRage.Level
{
    /// <summary>One face of a brush: the plane plus Quake-style texture mapping.</summary>
    public sealed class Face
    {
        public Plane plane;
        /// <summary>Three points that define the plane (kept so files round-trip exactly).</summary>
        public Vector3 p0, p1, p2;
        public string texture = "planks";
        public Vector2 offset;            // in texture repeats
        public float rotation;            // degrees, in the face's texture plane
        public Vector2 scale = new Vector2(4f, 4f); // world units per texture repeat

        /// <summary>Polygon of this face after clipping by the brush (filled by BrushGeometry).</summary>
        public List<Vector3> polygon = new List<Vector3>();
        /// <summary>Vertex paint (null = unpainted).</summary>
        public PaintGrid paint;

        public Face Clone() => new Face { plane = plane, p0 = p0, p1 = p1, p2 = p2, texture = texture, offset = offset, rotation = rotation, scale = scale, paint = paint?.Clone() };

        public void SetPlane(Vector3 a, Vector3 b, Vector3 c) { p0 = a; p1 = b; p2 = c; plane = Plane.FromPoints(a, b, c); }

        /// <summary>Rebuilds the three defining points from the plane (after moving a face along its normal).</summary>
        public void RebuildPointsFromPlane()
        {
            Vector3 n = plane.normal;
            Vector3 t = MathF.Abs(n.y) < 0.9f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.Cross(n, Vector3.right).normalized;
            Vector3 b = Vector3.Cross(n, t).normalized;
            Vector3 o = n * plane.dist;
            // keep the clockwise-from-outside orientation expected by Plane.FromPoints
            p0 = o; p1 = o + b * 8f; p2 = o + t * 8f;
            if (Vector3.Dot(Plane.FromPoints(p0, p1, p2).normal, n) < 0f) { var tmp = p1; p1 = p2; p2 = tmp; }
        }
    }

    /// <summary>Convex brush: the intersection of the half-spaces behind its faces.</summary>
    public sealed class Brush
    {
        public readonly List<Face> faces = new List<Face>();
        public Vector3 boundsMin, boundsMax;
        public bool IsAxisAligned { get; private set; }
        /// <summary>Layer / contents flags (e.g. "clip", "water"): stored as worldspawn-independent brush property through the texture name.</summary>
        public bool IsClip => faces.Count > 0 && faces[0].texture.Equals("clip", StringComparison.OrdinalIgnoreCase);
        public bool IsSky => faces.Count > 0 && faces[0].texture.Equals("sky", StringComparison.OrdinalIgnoreCase);

        public Brush Clone()
        {
            var b = new Brush();
            foreach (var f in faces) b.faces.Add(f.Clone());
            b.Rebuild();
            return b;
        }

        /// <summary>Axis-aligned box brush from min/max corners with one texture on every face.</summary>
        public static Brush Box(Vector3 min, Vector3 max, string texture, Vector2? scale = null)
        {
            var b = new Brush();
            void AddFace(Vector3 a, Vector3 c, Vector3 d)
            {
                var f = new Face { texture = texture };
                if (scale.HasValue) f.scale = scale.Value;
                f.SetPlane(a, c, d);
                b.faces.Add(f);
            }
            // points chosen so Plane.FromPoints yields outward normals
            AddFace(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, max.z)); // top (+y)
            AddFace(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, min.z)); // bottom (-y)
            AddFace(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, min.z)); // +x
            AddFace(new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, max.z)); // -x
            AddFace(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, min.y, max.z)); // +z
            AddFace(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(min.x, max.y, min.z)); // -z
            b.Rebuild();
            return b;
        }

        /// <summary>Recomputes face polygons, bounds and the axis-aligned flag.</summary>
        public void Rebuild()
        {
            BrushGeometry.BuildPolygons(this);
            boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            boundsMax = -boundsMin;
            IsAxisAligned = true;
            foreach (var f in faces)
            {
                var n = f.plane.normal;
                int nonZero = (MathF.Abs(n.x) > 1E-4f ? 1 : 0) + (MathF.Abs(n.y) > 1E-4f ? 1 : 0) + (MathF.Abs(n.z) > 1E-4f ? 1 : 0);
                if (nonZero != 1) IsAxisAligned = false;
                foreach (var v in f.polygon) { boundsMin = Vector3.Min(boundsMin, v); boundsMax = Vector3.Max(boundsMax, v); }
            }
            if (boundsMin.x == float.MaxValue) { boundsMin = boundsMax = Vector3.zero; }
        }

        public Vector3 Center => (boundsMin + boundsMax) * 0.5f;
        public bool IsValid => faces.Count >= 4 && boundsMax.x - boundsMin.x > 1E-4f && boundsMax.y - boundsMin.y > 1E-4f && boundsMax.z - boundsMin.z > 1E-4f;

        public void Translate(Vector3 delta)
        {
            foreach (var f in faces) f.SetPlane(f.p0 + delta, f.p1 + delta, f.p2 + delta);
            Rebuild();
        }

        /// <summary>Rotates the brush around a pivot by a quaternion (used for 90° turns around Y).</summary>
        public void Rotate(Quaternion q, Vector3 pivot)
        {
            foreach (var f in faces) f.SetPlane(q * (f.p0 - pivot) + pivot, q * (f.p1 - pivot) + pivot, q * (f.p2 - pivot) + pivot);
            Rebuild();
        }

        /// <summary>Moves one face along its normal (push/pull). Returns false if the brush would collapse.</summary>
        public bool MoveFace(Face f, float amount)
        {
            var saved = f.Clone();
            Vector3 d = f.plane.normal * amount;
            f.SetPlane(f.p0 + d, f.p1 + d, f.p2 + d);
            Rebuild();
            if (!IsValid || f.polygon.Count < 3)
            {
                f.plane = saved.plane; f.p0 = saved.p0; f.p1 = saved.p1; f.p2 = saved.p2;
                Rebuild();
                return false;
            }
            return true;
        }

        public bool Contains(Vector3 p, float epsilon = 1E-4f)
        {
            foreach (var f in faces) if (f.plane.Distance(p) > epsilon) return false;
            return true;
        }
    }

    /// <summary>Point entity (or the worldspawn) with Quake-style key/values.</summary>
    public sealed class MapEntity
    {
        public readonly Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Brushes owned by this entity (only worldspawn uses them in this engine).</summary>
        public readonly List<Brush> brushes = new List<Brush>();

        public string classname { get => Get("classname", ""); set => properties["classname"] = value; }

        public string Get(string key, string def = "") => properties.TryGetValue(key, out var v) ? v : def;
        public float GetFloat(string key, float def = 0f) => properties.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
        public int GetInt(string key, int def = 0) => (int)GetFloat(key, def);
        public void Set(string key, string value) => properties[key] = value;
        public void SetFloat(string key, float value) => properties[key] = value.ToString("0.###", CultureInfo.InvariantCulture);

        public Vector3 origin
        {
            get => GetVector("origin");
            set => properties["origin"] = $"{value.x.ToString("0.###", CultureInfo.InvariantCulture)} {value.y.ToString("0.###", CultureInfo.InvariantCulture)} {value.z.ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        /// <summary>Yaw in degrees (Quake "angle").</summary>
        public float angle { get => GetFloat("angle"); set => SetFloat("angle", value); }

        public Vector3 GetVector(string key, Vector3 def = default)
        {
            if (!properties.TryGetValue(key, out var v)) return def;
            var p = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return def;
            return new Vector3(F(p[0]), F(p[1]), F(p[2]));
        }

        public Color GetColor(string key, Color def)
        {
            if (!properties.TryGetValue(key, out var v)) return def;
            var p = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return def;
            return new Color(F(p[0]), F(p[1]), F(p[2]), p.Length > 3 ? F(p[3]) : 1f);
        }

        private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        public MapEntity Clone()
        {
            var e = new MapEntity();
            foreach (var kv in properties) e.properties[kv.Key] = kv.Value;
            foreach (var b in brushes) e.brushes.Add(b.Clone());
            return e;
        }
    }

    /// <summary>A whole level: worldspawn (with all world brushes) + point entities.</summary>
    public sealed class MapDocument
    {
        public MapEntity worldspawn = new MapEntity { classname = "worldspawn" };
        public readonly List<MapEntity> entities = new List<MapEntity>();
        public string path;

        public List<Brush> Brushes => worldspawn.brushes;

        public MapDocument Clone()
        {
            var d = new MapDocument { worldspawn = worldspawn.Clone(), path = path };
            foreach (var e in entities) d.entities.Add(e.Clone());
            return d;
        }

        public (Vector3 min, Vector3 max) Bounds
        {
            get
            {
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                foreach (var b in Brushes) { min = Vector3.Min(min, b.boundsMin); max = Vector3.Max(max, b.boundsMax); }
                if (min.x == float.MaxValue) return (Vector3.zero, Vector3.zero);
                return (min, max);
            }
        }
    }
}
