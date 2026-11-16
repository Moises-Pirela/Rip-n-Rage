using System;
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

        private static PrefabLibrary Load()
        {
            var lib = new PrefabLibrary();
            string dir = Directory ?? Locate();
            if (dir == null) { Debug.LogError("prefabs/ not found: the level classes cannot be built"); return lib; }
            lib.LoadDirectory(dir);
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
