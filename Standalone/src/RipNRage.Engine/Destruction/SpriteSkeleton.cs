using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Destruction
{
    /// <summary>
    /// How a sprite breaks apart: a tree of parts, each a polygon cut out of the sprite with a pivot where it hangs
    /// off its parent. It is not an animation skeleton — nothing is posed with it — it only says where to slice and
    /// what stays attached to what when the sprite is turned into a ragdoll or blown into pieces.
    /// Coordinates are normalised (0..1) inside the sheet cell, bottom-left origin, so one view serves every frame
    /// of that side. Files live in <c>skeletons/*.json</c> (see <see cref="SkeletonLibrary"/>).
    /// </summary>
    public sealed class SpriteSkeleton
    {
        public string name = "skeleton";
        /// <summary>Texture path relative to the assets folder (e.g. "Sprites/Enemies/Rifleman/Rifleman_Idle.png").</summary>
        public string texture = "";
        /// <summary>Sheet grid: columns are frames, rows are sides (row 0 = the top row of the image).</summary>
        public int frames = 1;
        public int sides = 1;
        public float pixelsPerUnit = 100f;
        /// <summary>Thickness (world units) of the parts' collision boxes.</summary>
        public float depth = 0.15f;
        /// <summary>Seconds a ragdoll made from this skeleton stays in the world (0 = forever).</summary>
        public float lifetime = 20f;
        public List<SkeletonView> views = new List<SkeletonView>();

        /// <summary>The view drawn for a sheet side (the view with that side, else the first one).</summary>
        public SkeletonView ViewForSide(int side)
        {
            SkeletonView best = null;
            foreach (var v in views)
            {
                if (v.side == side && (best == null || v.frame <= best.frame)) best = v;
            }
            if (best == null && views.Count > 0) best = views[0];
            return best;
        }

        public SkeletonView GetView(string viewName)
        {
            foreach (var v in views) if (string.Equals(v.name, viewName, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        public SpriteSkeleton Clone() => JsonSerializer.Deserialize<SpriteSkeleton>(JsonSerializer.Serialize(this, EngineJson.Options), EngineJson.Options);
    }

    /// <summary>The parts of one side of the sheet (the reference cell is side / frame).</summary>
    public sealed class SkeletonView
    {
        public string name = "front";
        public int side;
        public int frame;
        public List<SkeletonPart> parts = new List<SkeletonPart>();

        public SkeletonPart Find(string partName)
        {
            foreach (var p in parts) if (string.Equals(p.name, partName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>Parts without a parent.</summary>
        public IEnumerable<SkeletonPart> Roots()
        {
            foreach (var p in parts) if (string.IsNullOrEmpty(p.parent) || Find(p.parent) == null) yield return p;
        }
    }

    public enum PartShape { Box, Capsule }

    public sealed class SkeletonPart
    {
        public string name = "part";
        /// <summary>Parent part (empty = root).</summary>
        public string parent = "";
        /// <summary>Joint position (normalised cell coordinates): where this part hangs off its parent.</summary>
        public Vector2 pivot = new Vector2(0.5f, 0.5f);
        /// <summary>Outline of the piece (normalised cell coordinates, 3+ points, concave allowed).</summary>
        public List<Vector2> polygon = new List<Vector2>();
        public float mass = 2f;
        public PartShape shape = PartShape.Box;
        /// <summary>Impulse (N·s) that tears the joint to the parent; 0 = never (ragdoll stays whole).</summary>
        public float breakImpulse = 0f;
        /// <summary>Starts detached (a loose piece instead of a jointed limb).</summary>
        public bool loose = false;

        public Vector2[] PolygonArray() => polygon.ToArray();

        /// <summary>Bounds of the polygon in normalised coordinates.</summary>
        public Rect Bounds()
        {
            if (polygon.Count == 0) return new Rect(pivot.x, pivot.y, 0f, 0f);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in polygon) { minX = MathF.Min(minX, p.x); minY = MathF.Min(minY, p.y); maxX = MathF.Max(maxX, p.x); maxY = MathF.Max(maxY, p.y); }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    /// <summary>
    /// Loads, saves and hot-reloads the <c>skeletons/*.json</c> files and resolves their textures.
    /// </summary>
    public static class SkeletonLibrary
    {
        public static readonly Dictionary<string, SpriteSkeleton> Skeletons = new Dictionary<string, SpriteSkeleton>(StringComparer.OrdinalIgnoreCase);
        public static string Directory { get; private set; }
        public static event Action Reloaded;
        private static readonly Dictionary<string, DateTime> _stamps = new Dictionary<string, DateTime>();
        private static double _nextPoll;
        public static bool SuppressPolling;

        public static SpriteSkeleton Get(string name) => name != null && Skeletons.TryGetValue(name, out var s) ? s : null;

        /// <summary>Finds the skeletons folder: explicit path, RIPNRAGE_SKELETONS, or a "skeletons" directory up the tree.</summary>
        public static string Locate(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && System.IO.Directory.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_SKELETONS");
            if (!string.IsNullOrEmpty(env) && System.IO.Directory.Exists(env)) return env;
            foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "skeletons");
                    if (System.IO.Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        public static void SetDirectory(string dir)
        {
            Directory = dir;
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        }

        public static string FileOf(string name) => Path.Combine(Directory ?? ".", name + ".json");

        public static void Register(SpriteSkeleton s) { if (s != null && !string.IsNullOrEmpty(s.name)) Skeletons[s.name] = s; }

        public static void Save(SpriteSkeleton s)
        {
            if (Directory == null) throw new InvalidOperationException("SkeletonLibrary has no directory");
            string path = FileOf(s.name);
            File.WriteAllText(path, JsonSerializer.Serialize(s, EngineJson.Options));
            _stamps[path] = File.GetLastWriteTimeUtc(path);
            Register(s);
        }

        public static void Delete(string name)
        {
            Skeletons.Remove(name);
            string path = FileOf(name);
            if (File.Exists(path)) File.Delete(path);
            _stamps.Remove(path);
        }

        public static int LoadAll(bool quiet = false)
        {
            if (Directory == null || !System.IO.Directory.Exists(Directory)) return 0;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<SpriteSkeleton>(File.ReadAllText(f), EngineJson.Options);
                    if (s == null) continue;
                    if (string.IsNullOrEmpty(s.name)) s.name = Path.GetFileNameWithoutExtension(f);
                    Register(s);
                    _stamps[f] = File.GetLastWriteTimeUtc(f);
                    n++;
                }
                catch (Exception e) { Debug.LogError($"skeleton {Path.GetFileName(f)}: {e.Message}"); }
            }
            if (!quiet) Debug.LogWarning($"Skeletons: {n} loaded from {Directory}");
            Reloaded?.Invoke();
            return n;
        }

        /// <summary>Call once per frame: reloads changed/new files (polled twice a second).</summary>
        public static void Poll(double now)
        {
            if (Directory == null || SuppressPolling || now < _nextPoll) return;
            _nextPoll = now + 0.5;
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return;
                bool changed = false;
                foreach (var f in System.IO.Directory.GetFiles(Directory, "*.json"))
                {
                    var stamp = File.GetLastWriteTimeUtc(f);
                    if (!_stamps.TryGetValue(f, out var seen) || seen != stamp) { changed = true; break; }
                }
                if (changed) { LoadAll(quiet: true); Debug.LogWarning("skeletons reloaded"); }
            }
            catch (IOException) { }
        }

        /// <summary>The sheet texture of a skeleton (cached by the texture loader).</summary>
        public static Texture2D TextureOf(SpriteSkeleton s)
        {
            if (s == null || string.IsNullOrEmpty(s.texture)) return null;
            try { return Texture2D.Load(Application.AssetPath(s.texture)); }
            catch (Exception e) { Debug.LogError($"skeleton {s.name}: texture {s.texture}: {e.Message}"); return null; }
        }

        /// <summary>Sprite of one cell of the skeleton's sheet.</summary>
        public static Sprite CellSprite(SpriteSkeleton s, int side, int frame)
        {
            var tex = TextureOf(s);
            if (tex == null) return null;
            return new DirectionalSpriteSheet(tex, s.frames, s.sides, 1f, s.pixelsPerUnit).GetSprite(side, frame);
        }

        /// <summary>Which side of the sheet a sprite (made by <see cref="DirectionalSpriteSheet"/>) came from, by its rect; -1 when unknown.</summary>
        public static int SideOf(SpriteSkeleton s, Sprite sprite)
        {
            if (s == null || sprite == null || s.sides <= 1) return 0;
            // sprites cut from a DirectionalSpriteSheet are named "<texture>[side,frame]"
            string n = sprite.name;
            int lb = n != null ? n.LastIndexOf('[') : -1, comma = lb >= 0 ? n.IndexOf(',', lb) : -1;
            if (lb >= 0 && comma > lb && int.TryParse(n.Substring(lb + 1, comma - lb - 1), out int parsed)) return Mathf.Clamp(parsed, 0, s.sides - 1);
            float ch = (float)sprite.texture.height / s.sides;
            int rowFromBottom = Mathf.RoundToInt(sprite.rect.y / ch);
            return Mathf.Clamp(s.sides - 1 - rowFromBottom, 0, s.sides - 1);
        }
    }
}
