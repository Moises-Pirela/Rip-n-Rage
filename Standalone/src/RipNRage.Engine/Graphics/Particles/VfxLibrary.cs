using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RipNRage.Engine.Graphics.Particles
{
    /// <summary>
    /// Registry + file format for the data-driven effects: one <c>vfx/&lt;name&gt;.json</c> per <see cref="ParticleEffect"/>
    /// (serialised with <see cref="EngineJson"/>: fields, enums as strings, colours as [r,g,b,a]). Code-built defaults
    /// are registered first; files in the folder override them and are hot-reloaded while the game runs, so the
    /// VFX editor's saves show up in a live game immediately.
    /// </summary>
    public static class VfxLibrary
    {
        public static readonly Dictionary<string, ParticleEffect> Effects = new Dictionary<string, ParticleEffect>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Folder being watched / saved to (null = code-built effects only).</summary>
        public static string Directory { get; private set; }
        /// <summary>Raised after files were (re)loaded.</summary>
        public static event Action Reloaded;
        /// <summary>The editor sets this: it is the writer and must not reload its own saves.</summary>
        public static bool SuppressPolling;

        private static readonly Dictionary<string, DateTime> _stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static double _nextPoll;

        public static void Register(ParticleEffect effect)
        {
            if (effect == null || string.IsNullOrEmpty(effect.name)) return;
            Effects[effect.name] = effect;
        }

        public static ParticleEffect Get(string name) => !string.IsNullOrEmpty(name) && Effects.TryGetValue(name, out var e) ? e : null;

        public static void Remove(string name) { if (name != null) Effects.Remove(name); }

        /// <summary>Finds the vfx folder: explicit path, RIPNRAGE_VFX, or a "vfx" directory up the tree from cwd / exe.</summary>
        public static string Locate(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && System.IO.Directory.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_VFX");
            if (!string.IsNullOrEmpty(env) && System.IO.Directory.Exists(env)) return env;
            foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "vfx");
                    if (System.IO.Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ files

        public static string PathOf(string effectName) => Directory == null ? null : Path.Combine(Directory, Safe(effectName) + ".json");

        private static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public static string ToJson(ParticleEffect effect) => JsonSerializer.Serialize(effect, EngineJson.Options);

        public static ParticleEffect FromJson(string json)
        {
            var e = JsonSerializer.Deserialize<ParticleEffect>(json, EngineJson.Options);
            if (e != null)
            {
                e.emitters ??= new List<ParticleEmitter>();
                foreach (var em in e.emitters) em.frames ??= new List<string>();
            }
            return e;
        }

        public static void SetDirectory(string dir)
        {
            Directory = dir;
            if (dir != null) System.IO.Directory.CreateDirectory(dir);
            _stamps.Clear();
        }

        public static void Save(ParticleEffect effect)
        {
            if (Directory == null || effect == null) return;
            string path = PathOf(effect.name);
            File.WriteAllText(path, ToJson(effect));
            _stamps[path] = File.GetLastWriteTimeUtc(path);
            Register(effect);
        }

        public static void SaveAll()
        {
            foreach (var e in Effects.Values) Save(e);
        }

        public static void DeleteFile(string effectName)
        {
            string path = PathOf(effectName);
            if (path != null && File.Exists(path)) File.Delete(path);
            if (path != null) _stamps.Remove(path);
            Remove(effectName);
        }

        /// <summary>Loads every *.json in the folder (updating / adding effects by file name). Returns the number loaded.</summary>
        public static int LoadAll(bool quiet = false)
        {
            if (Directory == null || !System.IO.Directory.Exists(Directory)) return 0;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                if (Path.GetFileName(f).StartsWith("bindings", StringComparison.OrdinalIgnoreCase)) continue; // game-side mapping file
                if (LoadFile(f, quiet)) n++;
            }
            Reloaded?.Invoke();
            return n;
        }

        private static bool LoadFile(string path, bool quiet)
        {
            try
            {
                var e = FromJson(File.ReadAllText(path));
                if (e == null) return false;
                if (string.IsNullOrEmpty(e.name)) e.name = Path.GetFileNameWithoutExtension(path);
                _stamps[path] = File.GetLastWriteTimeUtc(path);
                Register(e);
                if (!quiet) Debug.Log($"vfx: loaded {e.name} ({e.emitters.Count} emitters)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"vfx: failed to load {Path.GetFileName(path)}: {ex.Message}");
                _stamps[path] = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                return false;
            }
        }

        /// <summary>Call once per frame: reloads changed / new files and drops effects whose file was deleted.</summary>
        public static void Poll(double now)
        {
            if (Directory == null || SuppressPolling || now < _nextPoll) return;
            _nextPoll = now + 0.5;
            if (!System.IO.Directory.Exists(Directory)) return;
            bool changed = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                if (Path.GetFileName(f).StartsWith("bindings", StringComparison.OrdinalIgnoreCase)) continue;
                seen.Add(f);
                DateTime stamp;
                try { stamp = File.GetLastWriteTimeUtc(f); } catch (IOException) { continue; }
                if (_stamps.TryGetValue(f, out var old) && old == stamp) continue;
                if (LoadFile(f, false)) { changed = true; Debug.LogWarning($"vfx: reloaded {Path.GetFileNameWithoutExtension(f)}"); }
            }
            foreach (var known in new List<string>(_stamps.Keys))
            {
                if (seen.Contains(known)) continue;
                _stamps.Remove(known);
                Remove(Path.GetFileNameWithoutExtension(known));
                changed = true;
            }
            if (changed) Reloaded?.Invoke();
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Plays an effect by name at a position, facing <paramref name="forward"/> (surface normal / fire direction). Null when unknown (warned once).</summary>
        public static ParticleEffectInstance Spawn(string name, Vector3 position, Vector3 forward, float scale = 1f)
        {
            var def = Get(name);
            if (def == null) { WarnMissing(name); return null; }
            return ParticleWorld.Spawn(def, position, forward, scale);
        }

        /// <summary>Plays an effect travelling from <paramref name="from"/> towards <paramref name="to"/>; emitters with <c>lifetimeFromDistance</c> die on arrival.</summary>
        public static ParticleEffectInstance SpawnTracer(string name, Vector3 from, Vector3 to, float scale = 1f)
        {
            var def = Get(name);
            if (def == null) { WarnMissing(name); return null; }
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.01f) return null;
            return ParticleWorld.Spawn(def, from, d / dist, scale, dist);
        }

        private static void WarnMissing(string name)
        {
            if (string.IsNullOrEmpty(name) || !_warned.Add(name)) return;
            Debug.LogWarning($"vfx: unknown effect '{name}'");
        }
    }
}
