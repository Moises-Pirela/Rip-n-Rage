using System;

namespace RipNRage.Engine.Ecs.Nav
{
    /// <summary>
    /// The level's navigation grid, where everyone finds it. Shared immutable-after-bake data, like
    /// <c>Enemies.All</c> or the surface table: one per level, rebuilt when the level is.
    /// </summary>
    public static class Navigation
    {
        public static NavWorld Current;
        public static bool Ready => Current != null && Current.TotalSpans > 0;

        /// <summary>Set when something added or removed level geometry after the bake; the bake system rebuilds next step.</summary>
        public static bool Stale;

        /// <summary>The level's shape changed — rebake. (Moving things do not need this: they carve, 11.5.)</summary>
        public static void Invalidate() => Stale = true;
    }

    /// <summary>What a span is good for. A span that is not <see cref="Walkable"/> is still *there* — it holds the
    /// floor up, and a neighbour's step rule measures against it — it just may not be stood on.</summary>
    [Flags]
    public enum NavSpanFlags : ushort
    {
        None = 0,
        /// <summary>The body fits here: slope within the limit, headroom and clearance for the capsule.</summary>
        Walkable = 1,
        /// <summary>The floor is steeper than <see cref="NavSettings.MaxSlopeDeg"/>.</summary>
        Steep = 2,
        /// <summary>Not enough room above the floor for the body.</summary>
        LowCeiling = 4,
        /// <summary>The body's capsule overlaps the world here (too close to a wall).</summary>
        Tight = 8,
    }

    /// <summary>
    /// One walkable surface in a column. A deck over a deck is two of these in the same column — this is what makes
    /// the grid vertical rather than a height per cell.
    /// </summary>
    public struct NavSpan
    {
        /// <summary>World y of the surface stood on.</summary>
        public float Floor;
        /// <summary>Clear height above <see cref="Floor"/> before the next solid.</summary>
        public float Headroom;
        public NavSpanFlags Flags;
        /// <summary>
        /// How many moving things cover this span right now (11.5). A **counter, not a flag**: two crates on one
        /// span, one taken away, the span stays blocked. Blocked for a search when &gt; 0.
        /// </summary>
        public ushort Dynamic;

        public bool Walkable => (Flags & NavSpanFlags.Walkable) != 0;
        /// <summary>Walkable and nothing moving is standing on it.</summary>
        public bool Open => Walkable && Dynamic == 0;
    }

    /// <summary>A column's slice of its tile's <see cref="NavTile.Spans"/>, ordered high floor first.</summary>
    public struct NavColumn { public int First, Count; }

    /// <summary>
    /// A move from one span to one of its 8 neighbours' spans. Links are built once, after every tile has its
    /// spans, so a link may cross a tile edge — that is what the spec calls a portal.
    /// </summary>
    public struct NavLink
    {
        /// <summary>Global span index of the far end (see <see cref="NavWorld.SpanIndex"/>).</summary>
        public int To;
        /// <summary>Cost of taking it (metres, the 3-D distance between the span centres).</summary>
        public float Cost;
        /// <summary>A drop too deep to climb back up: the search may go down it and not up.</summary>
        public bool OneWay;
    }

    /// <summary>A span's slice of its tile's <see cref="NavTile.Links"/>.</summary>
    public struct NavLinkSlice { public int First, Count; }

    /// <summary>
    /// One tile of the grid, on the level's render-chunk cut so a nav tile and a culling chunk are the same box.
    /// Tiles bake independently; a carve dirties one tile and bumps its <see cref="Version"/>.
    /// </summary>
    public sealed class NavTile
    {
        public int X, Z;
        /// <summary>World x/z of the tile's low corner (the column grid's origin).</summary>
        public float MinX, MinZ;
        public float MinY, MaxY;
        public int Width, Depth;
        /// <summary>Indexed <c>[z * Width + x]</c>.</summary>
        public NavColumn[] Columns = Array.Empty<NavColumn>();
        public NavSpan[] Spans = Array.Empty<NavSpan>();
        /// <summary>Parallel to <see cref="Spans"/>.</summary>
        public NavLinkSlice[] LinkSlices = Array.Empty<NavLinkSlice>();
        public NavLink[] Links = Array.Empty<NavLink>();
        /// <summary>This tile's first span in the global numbering.</summary>
        public int SpanBase;
        /// <summary>Bumped by a carve, so a follower can tell its path may be stale (11.6).</summary>
        public int Version;
    }

    /// <summary>The limits the bake measures against. Derived from the body that will walk the grid — never typed twice.</summary>
    public struct NavSettings
    {
        public float CellSize, CellHeight, MaxDrop;
        /// <summary>The body's capsule radius (clearance) and height (headroom).</summary>
        public float Radius, Height;
        /// <summary>How far up a neighbour may be and still be stepped onto.</summary>
        public float StepUp;
        public float MaxSlopeDeg;

        public static NavSettings Default => new NavSettings
        {
            CellSize = 0.5f, CellHeight = 0.2f, MaxDrop = 2.5f,
            Radius = 0.35f, Height = 1.9f, StepUp = 0.6f, MaxSlopeDeg = 50f,
        };
    }

    /// <summary>
    /// The level's navigation grid: sparse tiles of columns of spans. Built once at level start
    /// (<see cref="NavBake"/>), read by the search (<see cref="NavSearch"/>) and carved by what moves (11.5).
    /// </summary>
    public sealed class NavWorld
    {
        public NavSettings Settings;
        /// <summary>Tile grid origin and size; <see cref="Tiles"/> is indexed <c>[tz * TilesX + tx]</c> and is **null where the level has no geometry**.</summary>
        public float OriginX, OriginZ, TileSize;
        public int TilesX, TilesZ;
        public NavTile[] Tiles = Array.Empty<NavTile>();
        /// <summary>Every span in the level, numbered across tiles — the search's node id.</summary>
        public int TotalSpans;
        /// <summary>Bumped by any carve: a cheap "has anything changed at all" for followers.</summary>
        public int Version;
        /// <summary>Which bake this is (a process-wide counter): a rebake is a new grid with no stamps on it, and
        /// everyone holding state about the old one — the carve system above all — must notice.</summary>
        public int BakeId;
        private static int _bakes;
        public static int NextBakeId() => ++_bakes;

        public NavTile TileAt(int tx, int tz) =>
            tx < 0 || tz < 0 || tx >= TilesX || tz >= TilesZ ? null : Tiles[tz * TilesX + tx];

        /// <summary>The tile holding a world x/z (null outside the level).</summary>
        public NavTile TileOf(float x, float z) =>
            TileAt(Mathf.FloorToInt((x - OriginX) / TileSize), Mathf.FloorToInt((z - OriginZ) / TileSize));

        /// <summary>A span's global index.</summary>
        public int SpanIndex(NavTile t, int span) => t.SpanBase + span;

        /// <summary>Finds the tile and local span a global index belongs to (linear over tiles; for debug and tests).</summary>
        public bool Locate(int global, out NavTile tile, out int span)
        {
            foreach (var t in Tiles)
            {
                if (t == null || global < t.SpanBase || global >= t.SpanBase + t.Spans.Length) continue;
                tile = t; span = global - t.SpanBase; return true;
            }
            tile = null; span = -1; return false;
        }

        /// <summary>The world position a span is stood on (the column's centre, at the span's floor).</summary>
        public Vector3 PositionOf(NavTile t, int span)
        {
            int col = ColumnOfSpan(t, span);
            int cx = col % t.Width, cz = col / t.Width;
            return new Vector3(t.MinX + (cx + 0.5f) * Settings.CellSize, t.Spans[span].Floor,
                               t.MinZ + (cz + 0.5f) * Settings.CellSize);
        }

        public Vector3 PositionOf(int global)
        {
            return Locate(global, out var t, out int s) ? PositionOf(t, s) : Vector3.zero;
        }

        /// <summary>Which column a tile-local span belongs to (binary search over the column slices).</summary>
        public static int ColumnOfSpan(NavTile t, int span)
        {
            int lo = 0, hi = t.Columns.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var c = t.Columns[mid];
                if (span < c.First) hi = mid - 1;
                else if (span >= c.First + c.Count) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        /// <summary>
        /// The span a point stands on: the walkable span in its column whose floor is nearest at or below
        /// <paramref name="p"/>, within <paramref name="reach"/>. The entry point for "where is this thing".
        /// </summary>
        public bool TryFindSpan(Vector3 p, out int global, float reach = 3f)
        {
            global = -1;
            var t = TileOf(p.x, p.z);
            if (t == null) return false;
            int cx = Mathf.FloorToInt((p.x - t.MinX) / Settings.CellSize);
            int cz = Mathf.FloorToInt((p.z - t.MinZ) / Settings.CellSize);
            if (cx < 0 || cz < 0 || cx >= t.Width || cz >= t.Depth) return false;
            var col = t.Columns[cz * t.Width + cx];
            float best = float.MaxValue;
            for (int i = col.First; i < col.First + col.Count; i++)
            {
                if (!t.Spans[i].Walkable) continue;
                float d = p.y - t.Spans[i].Floor;
                if (d < -Settings.StepUp || d > reach) continue; // a little above counts: the body rides its spring
                float score = Mathf.Abs(d);
                if (score < best) { best = score; global = SpanIndex(t, i); }
            }
            return global >= 0;
        }
    }
}
