using System;
using System.Collections.Generic;
using System.IO;
using RipNRage.Level;

namespace RipNRage.Editor
{
    /// <summary>One stored copy of a level.</summary>
    public sealed class SnapshotInfo
    {
        public string path;
        public string name;
        public DateTime time;
        public long size;
        public int brushes, entities;
        public bool auto;
    }

    /// <summary>
    /// Timestamped copies of a level, stored next to it in <c>.snapshots/&lt;levelname&gt;/</c>:
    /// manual snapshots (F7), automatic ones on every save (pruned to the last N) and a browser
    /// (F8) to restore or delete them. Restoring goes through the editor's undo stack.
    /// </summary>
    public sealed class LevelSnapshots
    {
        public int maxAutomatic = 20;

        public string DirectoryFor(string levelPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(levelPath)) ?? ".";
            return Path.Combine(dir, ".snapshots", Path.GetFileNameWithoutExtension(levelPath));
        }

        public SnapshotInfo Take(MapDocument doc, string label, bool automatic)
        {
            if (doc.path == null) return null;
            string dir = DirectoryFor(doc.path);
            Directory.CreateDirectory(dir);
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + (automatic ? "-auto" : "-" + Sanitize(label ?? "snapshot")) + ".map";
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, MapFile.Write(doc));
            if (automatic) Prune(doc.path);
            return Describe(path);
        }

        private void Prune(string levelPath)
        {
            var autos = List(levelPath).FindAll(s => s.auto);
            for (int i = maxAutomatic; i < autos.Count; i++) { try { File.Delete(autos[i].path); } catch (IOException) { } }
        }

        public List<SnapshotInfo> List(string levelPath)
        {
            var result = new List<SnapshotInfo>();
            if (levelPath == null) return result;
            string dir = DirectoryFor(levelPath);
            if (!Directory.Exists(dir)) return result;
            foreach (var f in Directory.GetFiles(dir, "*.map")) result.Add(Describe(f));
            result.Sort((a, b) => b.time.CompareTo(a.time));
            return result;
        }

        public MapDocument Restore(SnapshotInfo s)
        {
            var doc = MapFile.Load(s.path);
            return doc;
        }

        public void Delete(SnapshotInfo s)
        {
            try { File.Delete(s.path); } catch (IOException) { }
        }

        private static SnapshotInfo Describe(string path)
        {
            var info = new SnapshotInfo { path = path, name = Path.GetFileNameWithoutExtension(path), time = File.GetLastWriteTime(path), size = new FileInfo(path).Length };
            info.auto = info.name.EndsWith("-auto");
            try
            {
                // cheap counts without a full parse: faces start with "(", entities with "classname"
                int faces = 0, ents = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("(")) faces++;
                    else if (line.StartsWith("\"classname\"")) ents++;
                }
                info.brushes = faces / 6; // approximate (most brushes are boxes)
                info.entities = Math.Max(0, ents - 1);
            }
            catch (IOException) { }
            return info;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
