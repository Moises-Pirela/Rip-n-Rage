using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs.Nav;

/// <summary>
/// Hand-built navigation grids for the checks, shared by both suites (11.10d: each had its own copy, and they were
/// already drifting). A grid is a map of characters, one per column, row 0 at z = 0:
/// <c>.</c> floor at y 0 · <c>#</c> nothing (a wall: no span at all) · <c>s</c> a 0.4 m step up ·
/// <c>d</c> a 2 m drop · <c>D</c> a 4 m drop (deeper than any MaxDrop here) · <c>x</c> a floor the body does not fit on.
/// The links come from <see cref="NavBake.Link"/> — the shipping builder, so the rules under test are the real ones.
/// </summary>
internal static class NavTestGrids
{
    public static NavSettings Settings(float cellSize = 1f) => new NavSettings
    {
        CellSize = cellSize, CellHeight = 0.2f, MaxDrop = 2.5f,
        Radius = 0.3f, Height = 1.8f, StepUp = 0.6f, MaxSlopeDeg = 50f,
    };

    public static NavWorld Grid(in NavSettings settings, float tileSize, params string[] rows)
    {
        int depth = rows.Length, width = rows[0].Length;
        var nav = new NavWorld
        {
            Settings = settings, TileSize = tileSize, OriginX = 0f, OriginZ = 0f,
            TilesX = 1, TilesZ = 1, BakeId = NavWorld.NextBakeId(),
        };
        var tile = new NavTile
        {
            X = 0, Z = 0, MinX = 0f, MinZ = 0f, Width = width, Depth = depth, MinY = -10f, MaxY = 10f,
            Columns = new NavColumn[width * depth],
        };
        var spans = new List<NavSpan>();
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                int first = spans.Count;
                char c = rows[z][x];
                if (c != '#')
                {
                    float floor = c == 's' ? 0.4f : c == 'd' ? -2f : c == 'D' ? -4f : 0f;
                    spans.Add(new NavSpan { Floor = floor, Headroom = 2f, Flags = c == 'x' ? NavSpanFlags.Tight : NavSpanFlags.Walkable });
                }
                tile.Columns[z * width + x] = new NavColumn { First = first, Count = spans.Count - first };
            }
        tile.Spans = spans.ToArray();
        nav.Tiles = new[] { tile };
        NavBake.Link(nav);
        return nav;
    }
}
