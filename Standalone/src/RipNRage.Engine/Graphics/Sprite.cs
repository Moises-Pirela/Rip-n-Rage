using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// A rectangular region of a texture with a pivot, in the UnityEngine.Sprite sense.
    /// <c>rect</c> is in pixels with a bottom-left origin (Unity / TexturePacker convention).
    /// </summary>
    public sealed class Sprite : EngineObject
    {
        public Texture2D texture { get; }
        public Rect rect { get; }
        /// <summary>Normalised pivot inside the rect (0..1, bottom-left origin).</summary>
        public Vector2 pivotNormalized { get; }
        public float pixelsPerUnit { get; }

        /// <summary>
        /// Optional tight polygon mesh (TexturePacker polygon packing / Unity "tight" sprite mesh):
        /// vertices in pixels relative to the rect's bottom-left corner, plus a triangle index list.
        /// When null the sprite is a simple quad.
        /// </summary>
        public Vector2[] vertices { get; private set; }
        public int[] triangles { get; private set; }
        public bool HasMesh => vertices != null && triangles != null && triangles.Length >= 3;

        public Sprite(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit = 100f, string name = null)
        {
            this.texture = texture;
            this.rect = rect;
            pivotNormalized = pivot;
            this.pixelsPerUnit = pixelsPerUnit;
            this.name = name ?? texture.name;
        }

        public void SetMesh(Vector2[] verts, int[] tris)
        {
            vertices = verts;
            triangles = tris;
        }

        /// <summary>Mesh vertex in local units (pivot-relative) and its UV.</summary>
        public void GetMeshVertex(int i, out Vector2 position, out Vector2 uv)
        {
            Vector2 v = vertices[i];
            Vector2 p = pivot;
            position = new Vector2((v.x - p.x) / pixelsPerUnit, (v.y - p.y) / pixelsPerUnit);
            uv = new Vector2((rect.x + v.x) / texture.width, (rect.y + v.y) / texture.height);
        }

        /// <summary>Creates a sprite covering the whole texture.</summary>
        public static Sprite FromTexture(Texture2D tex, Vector2? pivot = null, float pixelsPerUnit = 100f) =>
            new Sprite(tex, new Rect(0, 0, tex.width, tex.height), pivot ?? new Vector2(0.5f, 0.5f), pixelsPerUnit, tex.name);

        /// <summary>Pivot in pixels relative to the rect's bottom-left corner.</summary>
        public Vector2 pivot => new Vector2(rect.width * pivotNormalized.x, rect.height * pivotNormalized.y);

        /// <summary>World-unit size of the sprite quad.</summary>
        public Vector2 size => new Vector2(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit);

        /// <summary>Local-space bounds (min/max corners) of the quad around the pivot, in world units.</summary>
        public void GetLocalCorners(out Vector2 min, out Vector2 max)
        {
            Vector2 p = pivot / pixelsPerUnit;
            Vector2 s = size;
            min = new Vector2(-p.x, -p.y);
            max = new Vector2(s.x - p.x, s.y - p.y);
        }

        /// <summary>UV rectangle (u0,v0,u1,v1) with v0 at the bottom (GL convention, matching the flipped upload).</summary>
        public void GetUVs(out float u0, out float v0, out float u1, out float v1)
        {
            u0 = rect.x / texture.width;
            u1 = (rect.x + rect.width) / texture.width;
            v0 = rect.y / texture.height;
            v1 = (rect.y + rect.height) / texture.height;
        }

        public override string ToString() => $"Sprite({name} {rect})";
    }

    /// <summary>
    /// Parses TexturePacker "Unity" sprite sheet data files (<c>.tpsheet</c>) that ship with the
    /// game's art, producing named sprites over the atlas texture. Format per line:
    /// <c>name;x;y;w;h; pivotX;pivotY; borderL;borderT;borderR;borderB; [vertices...]</c>
    /// (x,y = bottom-left origin, as Unity imports them).
    /// </summary>
    public sealed class SpriteAtlas
    {
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public Texture2D Texture { get; private set; }
        public string Name { get; private set; }
        public IReadOnlyDictionary<string, Sprite> Sprites => _sprites;

        public Sprite this[string name] => Get(name);

        public Sprite Get(string spriteName)
        {
            if (_sprites.TryGetValue(spriteName, out var s)) return s;
            // TexturePacker sometimes leaves stray spaces in names ("rocketlauncher _01").
            foreach (var kv in _sprites)
                if (kv.Key.Replace(" ", "") .Equals(spriteName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) return kv.Value;
            throw new KeyNotFoundException($"Sprite '{spriteName}' not found in atlas '{Name}'");
        }

        public bool TryGet(string spriteName, out Sprite sprite) => _sprites.TryGetValue(spriteName, out sprite);

        /// <summary>Creates an empty atlas over a texture so sprites generated in code can be registered like TexturePacker ones.</summary>
        public static SpriteAtlas Create(string name, Texture2D texture) => new SpriteAtlas { Name = name, Texture = texture };

        /// <summary>Adds (or replaces) a sprite; <paramref name="rect"/> is in pixels with a bottom-left origin.</summary>
        public Sprite Add(string spriteName, Rect rect, Vector2? pivot = null, float pixelsPerUnit = 100f)
        {
            var s = new Sprite(Texture, rect, pivot ?? new Vector2(0.5f, 0.5f), pixelsPerUnit, spriteName);
            _sprites[spriteName] = s;
            return s;
        }

        public static SpriteAtlas Load(string tpsheetPath, float pixelsPerUnit = 100f, FilterMode filter = FilterMode.Trilinear)
        {
            var atlas = new SpriteAtlas { Name = Path.GetFileNameWithoutExtension(tpsheetPath) };
            string dir = Path.GetDirectoryName(tpsheetPath) ?? ".";
            string textureFile = null;
            int sheetW = 0, sheetH = 0;
            var entries = new List<string[]>();

            foreach (var rawLine in File.ReadAllLines(tpsheetPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith(":"))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(1, eq - 1);
                    string val = line.Substring(eq + 1);
                    if (key == "texture") textureFile = val;
                    else if (key == "size")
                    {
                        var parts = val.Split('x');
                        sheetW = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        sheetH = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                entries.Add(line.Split(';'));
            }

            if (textureFile == null) throw new InvalidDataException("tpsheet has no :texture line: " + tpsheetPath);
            atlas.Texture = Texture2D.Load(Path.Combine(dir, textureFile), filter);

            foreach (var f in entries)
            {
                if (f.Length < 7) continue;
                string name = f[0].Trim();
                float x = F(f[1]), y = F(f[2]), w = F(f[3]), h = F(f[4]);
                float px = F(f[5]), py = F(f[6]);
                var sprite = new Sprite(atlas.Texture, new Rect(x, y, w, h), new Vector2(px, py), pixelsPerUnit, name);

                // Optional polygon mesh: 4 border values, then vertexCount, x/y pairs, triangleCount, indices.
                // TexturePacker packs sprites by polygon, so bounding rects may overlap; the mesh is authoritative.
                try
                {
                    int k = 11;
                    if (f.Length > k + 1)
                    {
                        int vcount = (int)F(f[k]); k++;
                        var verts = new Vector2[vcount];
                        for (int i = 0; i < vcount; i++) { verts[i] = new Vector2(F(f[k]), F(f[k + 1])); k += 2; }
                        if (f.Length > k)
                        {
                            int triCount = (int)F(f[k]); k++; // number of triangles, 3 indices each
                            int icount = Mathf.Min(triCount * 3, f.Length - k);
                            var tris = new int[icount];
                            for (int i = 0; i < icount; i++, k++) tris[i] = (int)F(f[k]);
                            if (vcount >= 3 && icount >= 3) sprite.SetMesh(verts, tris);
                        }
                    }
                }
                catch (FormatException) { /* no mesh data: plain quad */ }

                atlas._sprites[name] = sprite;
            }
            return atlas;
        }

        private static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }
}
