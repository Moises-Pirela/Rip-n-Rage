using System;
using System.Collections.Generic;
using System.IO;
using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Content.Level
{
    /// <summary>
    /// The game's prefab files (10.3b): everything under <c>prefabs/</c>, loaded once. <c>prefabs/level/</c> holds the
    /// entity classes a <c>.map</c> places — a class is a prefab name, and the map's keys are field overrides on it.
    /// </summary>
    public static class GamePrefabs
    {
        private static PrefabLibrary _library;
        /// <summary>Override the folder (tools and checks); null = $RIPNRAGE_PREFABS, else found next to the assets.</summary>
        public static string Directory = Environment.GetEnvironmentVariable("RIPNRAGE_PREFABS");

        public static PrefabLibrary Library => _library ??= Load();

        /// <summary>Forgets the loaded prefabs (a reload, or a check that points <see cref="Directory"/> somewhere else).</summary>
        public static void Reset() => _library = null;

        /// <summary>The folder the library was loaded from (where a new prefab file goes).</summary>
        public static string Folder => Directory ?? Locate();

        /// <summary>The level classes' folder.</summary>
        public static string LevelFolder => System.IO.Path.Combine(Folder ?? "prefabs", "level");

        /// <summary>Called with a prefab's name when its file changed on disk and was re-read (the editor re-previews it).</summary>
        public static Action<string> OnReloaded;

        private static readonly Dictionary<string, DateTime> _stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static float _nextPoll;

        /// <summary>
        /// Re-reads the prefab files that changed since the last look, and notices new ones (10.7). Called every frame
        /// by the editor; it stats the folder at most every <paramref name="interval"/> seconds. Returns how many
        /// prefabs were re-read, so a caller can say so.
        /// </summary>
        public static int PollReload(float interval = 0.5f)
        {
            if (Time.unscaledTime < _nextPoll) return 0;
            _nextPoll = Time.unscaledTime + interval;
            string dir = Folder;
            if (dir == null || !System.IO.Directory.Exists(dir)) return 0;
            int reloaded = 0;
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                DateTime stamp;
                try { stamp = File.GetLastWriteTimeUtc(file); } catch { continue; }
                if (_stamps.TryGetValue(file, out var known) && known == stamp) continue;
                _stamps[file] = stamp;
                if (known == default && _library == null) continue; // the first look is the load itself
                try
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    Library.AddJson(File.ReadAllText(file), name, file);
                    reloaded++;
                    OnReloaded?.Invoke(name);
                }
                catch (Exception e) { Debug.LogWarning($"prefabs: {Path.GetFileName(file)} changed but does not load: {e.Message}"); }
            }
            return reloaded;
        }

        private static PrefabLibrary Load()
        {
            var lib = new PrefabLibrary();
            string dir = Directory ?? Locate();
            if (dir == null) { Debug.LogError("prefabs/ not found: the level classes cannot be built"); return lib; }
            lib.LoadDirectory(dir);
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                try { _stamps[file] = File.GetLastWriteTimeUtc(file); } catch { }
            return lib;
        }

        /// <summary>Finds <c>prefabs/</c> by walking up from the assets and the working directory (as levels are found).</summary>
        private static string Locate()
        {
            foreach (var start in new[] { Application.dataPath, System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    string c = Path.Combine(d.FullName, "prefabs");
                    if (System.IO.Directory.Exists(c)) return c;
                    d = d.Parent;
                }
            }
            return null;
        }
    }
}
