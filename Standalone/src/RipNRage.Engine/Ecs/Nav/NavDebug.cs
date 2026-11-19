using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Ecs.Nav
{
    /// <summary>
    /// Draws the grid, so what the AI can and cannot do is something you look at rather than infer from a probe
    /// line. Off unless asked for (<c>--nav-debug</c>): it draws a few thousand lines, which is not free.
    ///
    /// Spans are coloured by what they are: walkable green, blocked by the level grey, blocked by something that
    /// moved (a carve, 11.5) red. One-way drop links are drawn as a line down to where they land — those are the
    /// edges an enemy may fall off and not climb back.
    /// </summary>
    public static class NavDebug
    {
        public static bool Enabled;
        /// <summary>Metres around the viewer to draw: the whole dock is 130k spans and nobody can read that.</summary>
        public static float Radius = 20f;
        public static bool ShowLinks = true;
        /// <summary>Tile outlines.</summary>
        public static bool ShowTiles = true;
        /// <summary>Never draw more than this many spans, however wide the radius: a slider cannot be trusted with 130k.</summary>
        public static int MaxSpans = 20000;
        /// <summary>Spans the last <see cref="Draw"/> put on the screen (the panel reports it).</summary>
        public static int LastDrawn;

        private static readonly Color Walkable = new Color(0.25f, 0.9f, 0.35f, 0.5f);
        private static readonly Color Static = new Color(0.45f, 0.45f, 0.5f, 0.35f);
        /// <summary>The body does not fit: the usual reason a doorway a designer drew is not one the AI can use.</summary>
        private static readonly Color Tight = new Color(1f, 0.55f, 0.1f, 0.6f);
        private static readonly Color Reachable = new Color(0.3f, 0.65f, 1f, 0.75f);
        private static readonly Color Carved = new Color(1f, 0.25f, 0.2f, 0.85f);
        private static readonly Color Drop = new Color(1f, 0.75f, 0.1f, 0.7f);
        private static readonly Color TileEdge = new Color(0.2f, 0.5f, 1f, 0.35f);

        /// <summary>Draws the grid around <paramref name="around"/>. Call from a draw pass; does nothing when off.</summary>
        public static void Draw(Vector3 around)
        {
            var nav = Navigation.Current;
            if (!Enabled || nav == null || nav.TotalSpans == 0) return;
            float r = Radius, cell = nav.Settings.CellSize, half = cell * 0.4f;
            LastDrawn = 0;

            foreach (var tile in nav.Tiles)
            {
                if (tile == null) continue;
                float tMinX = tile.MinX, tMinZ = tile.MinZ, size = nav.TileSize;
                if (tMinX > around.x + r || tMinX + size < around.x - r || tMinZ > around.z + r || tMinZ + size < around.z - r) continue;

                if (ShowTiles)
                {
                    DebugDraw.Line(new Vector3(tMinX, around.y - 1f, tMinZ), new Vector3(tMinX + size, around.y - 1f, tMinZ), TileEdge);
                    DebugDraw.Line(new Vector3(tMinX, around.y - 1f, tMinZ), new Vector3(tMinX, around.y - 1f, tMinZ + size), TileEdge);
                }

                for (int s = 0; s < tile.Spans.Length; s++)
                {
                    var at = nav.PositionOf(tile, s);
                    if (Mathf.Abs(at.x - around.x) > r || Mathf.Abs(at.z - around.z) > r) continue;
                    if (LastDrawn >= MaxSpans) return;
                    ref var span = ref tile.Spans[s];
                    var c = span.Dynamic > 0 ? Carved
                          : Reach != null && span.Walkable && s + tile.SpanBase < Reach.Length && Reach[s + tile.SpanBase] ? Reachable
                          : span.Walkable ? Walkable
                          : (span.Flags & NavSpanFlags.Tight) != 0 ? Tight : Static;
                    LastDrawn++;
                    var y = at + Vector3.up * 0.02f;
                    DebugDraw.Line(y + new Vector3(-half, 0f, 0f), y + new Vector3(half, 0f, 0f), c);
                    DebugDraw.Line(y + new Vector3(0f, 0f, -half), y + new Vector3(0f, 0f, half), c);

                    if (!ShowLinks || !span.Walkable) continue;
                    var slice = tile.LinkSlices[s];
                    for (int k = slice.First; k < slice.First + slice.Count; k++)
                    {
                        if (!tile.Links[k].OneWay) continue; // only the interesting ones: where it may fall
                        DebugDraw.Line(y, nav.PositionOf(tile.Links[k].To) + Vector3.up * 0.02f, Drop);
                    }
                }
            }
        }

        /// <summary>
        /// Which spans a flood fill reached (11.17.4), indexed by global span, or null for "do not paint". Set by
        /// the editor; the draw tints what is reachable so an island shows up as the part that stays green.
        /// </summary>
        public static bool[] Reach;

        /// <summary>Draws one path: its corners, and a ring at the one being walked to.</summary>
        public static void DrawPath(NavPath path, int corner, int count, Color colour)
        {
            if (!Enabled || path == null || count <= 0) return;
            for (int i = 1; i < count && i < path.Count; i++)
                DebugDraw.Line(path.Corners[i - 1] + Vector3.up * 0.15f, path.Corners[i] + Vector3.up * 0.15f, colour, false);
            if (corner < count && corner < path.Count)
            {
                var c = path.Corners[corner] + Vector3.up * 0.15f;
                DebugDraw.Box(c - new Vector3(0.2f, 0.2f, 0.2f), c + new Vector3(0.2f, 0.2f, 0.2f), colour, false);
            }
        }
    }
}
