using System;
using System.Collections.Generic;
using System.IO;

namespace RipNRage.Engine.Ecs.Nav
{
    /// <summary>
    /// A bake, written down once per map. The bake is 220 ms on the dock and is paid at every level start,
    /// respawn and save-load; a level that has not changed bakes to the same grid every time, so the second time
    /// it is read back instead.
    ///
    /// The key is what the bake actually consumed: every static collider's world box (a brush moved, added,
    /// removed or rotated changes it), the settings the body derived (a profile change misses), the three map
    /// keys, the tile size and a format version. Links are not written: <see cref="NavBake.Link"/> is
    /// deterministic and cheap, so a loaded grid links itself and is byte-identical to a baked one — the
    /// <c>nav</c> probe cold and warm is the gate for that.
    /// </summary>
    public static class NavCache
    {
        /// <summary>Bump when the file layout or the bake's meaning changes: every cached grid misses.</summary>
        public const int FormatVersion = 1;

        /// <summary>The key for this level and these settings (FNV-1a over the bit patterns; stable across runs).</summary>
        public static ulong Key(World world, in NavSettings s, float tileSize)
        {
            ulong h = 14695981039346656037ul;
            void Mix(uint v) { h = (h ^ v) * 1099511628211ul; }
            void MixF(float f) => Mix(BitConverter.SingleToUInt32Bits(f));
            Mix((uint)FormatVersion);
            // the bake's rules are code: a change to how BakeTile samples (the roof ray was one) with no FormatVersion
            // bump would read stale grids back from a developer's data folder for ever. The engine module's build id
            // makes any rebuild of the engine a miss; a bake costs 200 ms once per build, and the sweep's hermetic
            // folder never had the problem.
            foreach (byte b in typeof(NavBake).Assembly.ManifestModule.ModuleVersionId.ToByteArray()) Mix(b);
            MixF(s.CellSize); MixF(s.CellHeight); MixF(s.MaxDrop); MixF(s.Radius); MixF(s.Height); MixF(s.StepUp); MixF(s.MaxSlopeDeg);
            MixF(tileSize); MixF(NavBake.SceneryExtent);
            if (NavBake.Bounds(world, out _, out _, out _, out var all))
            {
                // the boxes in a stable order: entity iteration order can change with archetype layout
                all.Sort((a, b) => { int c = a.lo.x.CompareTo(b.lo.x); if (c != 0) return c; c = a.lo.z.CompareTo(b.lo.z); if (c != 0) return c; c = a.lo.y.CompareTo(b.lo.y); if (c != 0) return c; c = a.hi.x.CompareTo(b.hi.x); if (c != 0) return c; c = a.hi.z.CompareTo(b.hi.z); return c != 0 ? c : a.hi.y.CompareTo(b.hi.y); });
                Mix((uint)all.Count);
                foreach (var (lo, hi) in all) { MixF(lo.x); MixF(lo.y); MixF(lo.z); MixF(hi.x); MixF(hi.y); MixF(hi.z); }
            }
            return h;
        }

        public static void Save(string path, NavWorld nav)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var f = new BinaryWriter(File.Create(path));
            f.Write(FormatVersion);
            var s = nav.Settings;
            f.Write(s.CellSize); f.Write(s.CellHeight); f.Write(s.MaxDrop); f.Write(s.Radius); f.Write(s.Height); f.Write(s.StepUp); f.Write(s.MaxSlopeDeg);
            f.Write(nav.OriginX); f.Write(nav.OriginZ); f.Write(nav.TileSize); f.Write(nav.TilesX); f.Write(nav.TilesZ);
            foreach (var t in nav.Tiles)
            {
                f.Write(t != null);
                if (t == null) continue;
                f.Write(t.X); f.Write(t.Z); f.Write(t.MinX); f.Write(t.MinZ); f.Write(t.MinY); f.Write(t.MaxY); f.Write(t.Width); f.Write(t.Depth);
                f.Write(t.Columns.Length);
                foreach (var c in t.Columns) { f.Write(c.First); f.Write(c.Count); }
                f.Write(t.Spans.Length);
                foreach (var sp in t.Spans) { f.Write(sp.Floor); f.Write(sp.Headroom); f.Write((ushort)sp.Flags); } // Dynamic is runtime: never written
            }
        }

        /// <summary>Reads a grid back and links it. False if the file is missing, another version, or not for these settings.</summary>
        public static bool TryLoad(string path, in NavSettings want, float tileSize, out NavWorld nav, JobSystem jobs = null)
        {
            nav = null;
            if (!File.Exists(path)) return false;
            try
            {
                using var f = new BinaryReader(File.OpenRead(path));
                if (f.ReadInt32() != FormatVersion) return false;
                var s = new NavSettings
                {
                    CellSize = f.ReadSingle(), CellHeight = f.ReadSingle(), MaxDrop = f.ReadSingle(), Radius = f.ReadSingle(),
                    Height = f.ReadSingle(), StepUp = f.ReadSingle(), MaxSlopeDeg = f.ReadSingle(),
                };
                if (s.CellSize != want.CellSize || s.CellHeight != want.CellHeight || s.MaxDrop != want.MaxDrop || s.Radius != want.Radius
                    || s.Height != want.Height || s.StepUp != want.StepUp || s.MaxSlopeDeg != want.MaxSlopeDeg) return false;
                var n = new NavWorld { Settings = s, OriginX = f.ReadSingle(), OriginZ = f.ReadSingle(), TileSize = f.ReadSingle(), BakeId = NavWorld.NextBakeId() };
                if (n.TileSize != tileSize) return false;
                n.TilesX = f.ReadInt32(); n.TilesZ = f.ReadInt32();
                n.Tiles = new NavTile[n.TilesX * n.TilesZ];
                for (int i = 0; i < n.Tiles.Length; i++)
                {
                    if (!f.ReadBoolean()) continue;
                    var t = new NavTile
                    {
                        X = f.ReadInt32(), Z = f.ReadInt32(), MinX = f.ReadSingle(), MinZ = f.ReadSingle(), MinY = f.ReadSingle(), MaxY = f.ReadSingle(),
                        Width = f.ReadInt32(), Depth = f.ReadInt32(),
                    };
                    t.Columns = new NavColumn[f.ReadInt32()];
                    for (int c = 0; c < t.Columns.Length; c++) t.Columns[c] = new NavColumn { First = f.ReadInt32(), Count = f.ReadInt32() };
                    t.Spans = new NavSpan[f.ReadInt32()];
                    for (int sp = 0; sp < t.Spans.Length; sp++) t.Spans[sp] = new NavSpan { Floor = f.ReadSingle(), Headroom = f.ReadSingle(), Flags = (NavSpanFlags)f.ReadUInt16() };
                    n.Tiles[i] = t;
                }
                NavBake.Link(n, jobs);
                nav = n;
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"nav cache: {path} unreadable ({e.Message}); baking"); return false; }
        }

        /// <summary>Same tiles, columns, spans and links? (The check for the round trip, and for a loaded grid against a baked one.)</summary>
        public static bool SameGrid(NavWorld a, NavWorld b)
        {
            if (a.TotalSpans != b.TotalSpans || a.TilesX != b.TilesX || a.TilesZ != b.TilesZ || a.OriginX != b.OriginX || a.OriginZ != b.OriginZ) return false;
            for (int i = 0; i < a.Tiles.Length; i++)
            {
                var x = a.Tiles[i]; var y = b.Tiles[i];
                if ((x == null) != (y == null)) return false;
                if (x == null) continue;
                if (x.Spans.Length != y.Spans.Length || x.Columns.Length != y.Columns.Length || x.Links.Length != y.Links.Length || x.SpanBase != y.SpanBase) return false;
                for (int k = 0; k < x.Columns.Length; k++) if (x.Columns[k].First != y.Columns[k].First || x.Columns[k].Count != y.Columns[k].Count) return false;
                for (int k = 0; k < x.Spans.Length; k++) if (x.Spans[k].Floor != y.Spans[k].Floor || x.Spans[k].Headroom != y.Spans[k].Headroom || x.Spans[k].Flags != y.Spans[k].Flags) return false;
                for (int k = 0; k < x.Links.Length; k++) if (x.Links[k].To != y.Links[k].To || x.Links[k].Cost != y.Links[k].Cost || x.Links[k].OneWay != y.Links[k].OneWay) return false;
            }
            return true;
        }
    }
}
