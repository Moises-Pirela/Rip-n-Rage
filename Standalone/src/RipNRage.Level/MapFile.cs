using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RipNRage.Engine;

namespace RipNRage.Level
{
    /// <summary>
    /// Reads and writes levels in the classic Quake <c>.map</c> text format:
    /// <code>
    /// {
    /// "classname" "worldspawn"
    /// {
    /// ( x y z ) ( x y z ) ( x y z ) texture offsetX offsetY rotation scaleX scaleY
    /// ...
    /// }
    /// }
    /// { "classname" "info_player_start" "origin" "0 1 8" "angle" "180" }
    /// </code>
    /// Coordinates are this engine's world units (metres, Y up). Scale values are world units per
    /// texture repeat. Other tools can open the files, but expect the axis convention above.
    /// </summary>
    public static class MapFile
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static MapDocument Load(string path)
        {
            var doc = Parse(File.ReadAllText(path));
            doc.path = path;
            return doc;
        }

        public static void Save(MapDocument doc, string path)
        {
            File.WriteAllText(path, Write(doc));
            doc.path = path;
        }

        public static string Write(MapDocument doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Rip-n-Rage level (Quake .map format, metres, Y up, scale = units per texture repeat)");
            WriteEntity(sb, doc.worldspawn);
            foreach (var e in doc.entities) WriteEntity(sb, e);
            return sb.ToString();
        }

        private static void WriteEntity(StringBuilder sb, MapEntity e)
        {
            sb.AppendLine("{");
            foreach (var kv in e.properties) sb.AppendLine($"\"{kv.Key}\" \"{kv.Value}\"");
            foreach (var b in e.brushes)
            {
                sb.AppendLine("{");
                foreach (var f in b.faces)
                    sb.AppendLine($"( {V(f.p0)} ) ( {V(f.p1)} ) ( {V(f.p2)} ) {f.texture} {N(f.offset.x)} {N(f.offset.y)} {N(f.rotation)} {N(f.scale.x)} {N(f.scale.y)}");
                // vertex paint (engine extension): paint <faceIndex> <w> <h> <cell> <u0> <v0> <base64 rgb>
                for (int i = 0; i < b.faces.Count; i++)
                {
                    var g = b.faces[i].paint;
                    if (g == null || g.IsWhite) continue;
                    sb.AppendLine($"paint {i} {g.width} {g.height} {N(g.cell)} {N(g.u0)} {N(g.v0)} {g.Encode()}");
                }
                sb.AppendLine("}");
            }
            sb.AppendLine("}");
        }

        private static string V(Vector3 v) => $"{N(v.x)} {N(v.y)} {N(v.z)}";
        private static string N(float f) => f.ToString("0.####", Inv);

        // ------------------------------------------------------------------ parsing

        public static MapDocument Parse(string text)
        {
            var doc = new MapDocument();
            var tok = new Tokenizer(text);
            bool haveWorld = false;
            while (tok.Next(out string t))
            {
                if (t != "{") throw new InvalidDataException($"map: expected '{{' at line {tok.Line}");
                var e = ParseEntity(tok);
                if (e.classname.Equals("worldspawn", StringComparison.OrdinalIgnoreCase) && !haveWorld) { doc.worldspawn = e; haveWorld = true; }
                else doc.entities.Add(e);
            }
            if (!haveWorld) doc.worldspawn = new MapEntity { classname = "worldspawn" };
            return doc;
        }

        private static MapEntity ParseEntity(Tokenizer tok)
        {
            var e = new MapEntity();
            while (tok.Next(out string t))
            {
                if (t == "}") return e;
                if (t == "{") { e.brushes.Add(ParseBrush(tok)); continue; }
                // key value
                if (!tok.Next(out string value)) throw new InvalidDataException("map: key without value");
                e.properties[t] = value;
            }
            throw new InvalidDataException("map: unterminated entity");
        }

        private static Brush ParseBrush(Tokenizer tok)
        {
            var b = new Brush();
            while (true)
            {
                if (!tok.Next(out string t)) throw new InvalidDataException("map: unterminated brush");
                if (t == "}") break;
                if (t == "paint")
                {
                    int fi = (int)tok.NextFloat(), w = (int)tok.NextFloat(), h = (int)tok.NextFloat();
                    float cell = tok.NextFloat(), u0 = tok.NextFloat(), v0 = tok.NextFloat();
                    tok.Next(out string data);
                    if (fi >= 0 && fi < b.faces.Count) b.faces[fi].paint = PaintGrid.Decode(w, h, cell, u0, v0, data);
                    continue;
                }
                if (t != "(") throw new InvalidDataException($"map: expected '(' in brush at line {tok.Line}");
                var f = new Face();
                Vector3 p0 = ParsePoint(tok), p1, p2;
                Expect(tok, "("); p1 = ParsePoint(tok);
                Expect(tok, "("); p2 = ParsePoint(tok);
                f.SetPlane(p0, p1, p2);
                if (!tok.Next(out f.texture)) throw new InvalidDataException("map: face without texture");
                // Valve 220 format: texture [ ux uy uz uoff ] [ vx vy vz voff ] rot sx sy — skip the axis blocks, keep offsets
                if (tok.Peek() == "[")
                {
                    tok.Next(out _); tok.Next(out _); tok.Next(out _); tok.Next(out _); f.offset.x = tok.NextFloat(); Expect(tok, "]");
                    Expect(tok, "["); tok.Next(out _); tok.Next(out _); tok.Next(out _); f.offset.y = tok.NextFloat(); Expect(tok, "]");
                }
                else
                {
                    f.offset.x = tok.NextFloat();
                    f.offset.y = tok.NextFloat();
                }
                f.rotation = tok.NextFloat();
                f.scale.x = tok.NextFloat();
                f.scale.y = tok.NextFloat();
                // Quake 2 style trailing content/surface/value flags are optional
                while (tok.PeekIsNumber()) tok.NextFloat();
                if (f.scale.x == 0f) f.scale.x = 4f;
                if (f.scale.y == 0f) f.scale.y = 4f;
                b.faces.Add(f);
            }
            b.Rebuild();
            return b;
        }

        private static Vector3 ParsePoint(Tokenizer tok)
        {
            float x = tok.NextFloat(), y = tok.NextFloat(), z = tok.NextFloat();
            Expect(tok, ")");
            return new Vector3(x, y, z);
        }

        private static void Expect(Tokenizer tok, string s)
        {
            if (!tok.Next(out string t) || t != s) throw new InvalidDataException($"map: expected '{s}' at line {tok.Line}");
        }

        private sealed class Tokenizer
        {
            private readonly string _s; private int _i;
            public int Line { get; private set; } = 1;
            public Tokenizer(string s) { _s = s; }

            private void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '\n') { Line++; _i++; }
                    else if (char.IsWhiteSpace(c)) _i++;
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; }
                    else break;
                }
            }

            public string Peek()
            {
                int save = _i; int line = Line;
                bool ok = Next(out string t);
                _i = save; Line = line;
                return ok ? t : null;
            }

            public bool PeekIsNumber()
            {
                string p = Peek();
                return p != null && float.TryParse(p, NumberStyles.Float, Inv, out _);
            }

            public bool Next(out string token)
            {
                SkipWs();
                token = null;
                if (_i >= _s.Length) return false;
                char c = _s[_i];
                if (c == '"')
                {
                    int end = _s.IndexOf('"', _i + 1);
                    if (end < 0) end = _s.Length;
                    token = _s.Substring(_i + 1, end - _i - 1);
                    _i = end + 1;
                    return true;
                }
                if (c == '{' || c == '}' || c == '(' || c == ')' || c == '[' || c == ']') { token = c.ToString(); _i++; return true; }
                int start = _i;
                while (_i < _s.Length && !char.IsWhiteSpace(_s[_i]) && "{}()[]".IndexOf(_s[_i]) < 0) _i++;
                token = _s.Substring(start, _i - start);
                return true;
            }

            public float NextFloat()
            {
                if (!Next(out string t) || !float.TryParse(t, NumberStyles.Float, Inv, out float f))
                    throw new InvalidDataException($"map: expected number at line {Line} (got '{t}')");
                return f;
            }
        }

        // ------------------------------------------------------------------ OBJ brush import

        /// <summary>
        /// Imports brushes from a Wavefront OBJ whose objects are convex solids (TrenchBroom exports),
        /// one brush per 'o' group: each polygon becomes a face plane. <paramref name="scale"/> and
        /// <paramref name="mirrorX"/> match the engine's mesh importer.
        /// </summary>
        public static List<Brush> ImportObjBrushes(string objPath, float scale, bool mirrorX, Vector3 offset, string texture, float textureScale)
        {
            var verts = new List<Vector3>();
            var groups = new List<List<int[]>>();
            List<int[]> cur = null;
            foreach (var raw in File.ReadLines(objPath))
            {
                var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v")
                    verts.Add(new Vector3(float.Parse(p[1], Inv) * scale * (mirrorX ? -1f : 1f), float.Parse(p[2], Inv) * scale, float.Parse(p[3], Inv) * scale) + offset);
                else if (p[0] == "o") { cur = new List<int[]>(); groups.Add(cur); }
                else if (p[0] == "f" && cur != null)
                {
                    var idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++) idx[i - 1] = int.Parse(p[i].Split('/')[0], Inv) - 1;
                    cur.Add(idx);
                }
            }
            var brushes = new List<Brush>();
            foreach (var faces in groups)
            {
                var b = new Brush();
                var seen = new List<Plane>();
                foreach (var poly in faces)
                {
                    if (poly.Length < 3) continue;
                    // OBJ faces are counter-clockwise from outside; mirroring X flips that.
                    Vector3 a = verts[poly[0]], bb = verts[poly[1]], c = verts[poly[2]];
                    Plane pl = mirrorX ? Plane.FromPoints(a, bb, c) : Plane.FromPoints(a, c, bb);
                    bool dup = false;
                    foreach (var s in seen) if (Vector3.Dot(s.normal, pl.normal) > 0.9999f && MathF.Abs(s.dist - pl.dist) < 1E-3f) { dup = true; break; }
                    if (dup) continue;
                    seen.Add(pl);
                    var f = new Face { texture = texture, scale = new Vector2(textureScale, textureScale) };
                    if (mirrorX) f.SetPlane(a, bb, c); else f.SetPlane(a, c, bb);
                    b.faces.Add(f);
                }
                b.Rebuild();
                if (b.IsValid) brushes.Add(b);
            }
            return brushes;
        }
    }
}
