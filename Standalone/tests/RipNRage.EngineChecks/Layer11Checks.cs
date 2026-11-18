using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs.Nav;

/// <summary>
/// Layer 11.2: the span graph and the A* over it.
///
/// The grids here are **hand-built**, not baked: a bake needs the process-wide physics world, which every other
/// suite in this process has also been writing to, and a check that depends on that is a check that fails for
/// reasons that have nothing to do with navigation. The links are built by <see cref="NavBake.Link"/> — the real
/// one, not a copy — so the step / drop / corner rules under test are the shipping rules. The bake's own
/// sampling (is there a floor here, does the body fit) is gated on the real level by the `nav` probe in the
/// gate sweep.
/// </summary>
internal static class Layer11Checks
{
    private const float Cell = 1f, TileSize = 16f;

    private static NavSettings Settings => new NavSettings
    {
        CellSize = Cell, CellHeight = 0.2f, MaxDrop = 2.5f,
        Radius = 0.3f, Height = 1.8f, StepUp = 0.6f, MaxSlopeDeg = 50f,
    };

    /// <summary>
    /// A one-tile grid from a map of characters, one per column, row 0 at z = 0:
    /// <c>.</c> floor at y 0, <c>#</c> nothing (a wall: no span at all), <c>s</c> a 0.4 m step up,
    /// <c>d</c> a 2 m drop down, <c>D</c> a 4 m drop (deeper than MaxDrop), <c>x</c> a floor that is not walkable.
    /// </summary>
    private static NavWorld Grid(params string[] rows)
    {
        int depth = rows.Length, width = rows[0].Length;
        var nav = new NavWorld
        {
            Settings = Settings, TileSize = TileSize, OriginX = 0f, OriginZ = 0f,
            TilesX = 1, TilesZ = 1,
        };
        var tile = new NavTile { X = 0, Z = 0, MinX = 0f, MinZ = 0f, Width = width, Depth = depth, MinY = -10f, MaxY = 10f };
        tile.Columns = new NavColumn[width * depth];
        var spans = new List<NavSpan>();
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                int first = spans.Count;
                char c = rows[z][x];
                if (c != '#')
                {
                    float floor = c == 's' ? 0.4f : c == 'd' ? -2f : c == 'D' ? -4f : 0f;
                    spans.Add(new NavSpan
                    {
                        Floor = floor, Headroom = 2f,
                        Flags = c == 'x' ? NavSpanFlags.Tight : NavSpanFlags.Walkable,
                    });
                }
                tile.Columns[z * width + x] = new NavColumn { First = first, Count = spans.Count - first };
            }
        tile.Spans = spans.ToArray();
        nav.Tiles = new[] { tile };
        NavBake.Link(nav);
        return nav;
    }

    /// <summary>Two tiles side by side in x, both flat: the grid a cross-tile search runs over.</summary>
    private static NavWorld TwoTiles(bool wallBetween)
    {
        int n = (int)(TileSize / Cell);
        var nav = new NavWorld { Settings = Settings, TileSize = TileSize, OriginX = 0f, OriginZ = 0f, TilesX = 2, TilesZ = 1 };
        nav.Tiles = new NavTile[2];
        for (int t = 0; t < 2; t++)
        {
            var tile = new NavTile { X = t, Z = 0, MinX = t * TileSize, MinZ = 0f, Width = n, Depth = n, MinY = -10f, MaxY = 10f };
            tile.Columns = new NavColumn[n * n];
            var spans = new List<NavSpan>();
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int first = spans.Count;
                    // a wall along the last column of the left tile, except one gap at z = 0
                    bool solid = wallBetween && t == 0 && x == n - 1 && z != 0;
                    if (!solid) spans.Add(new NavSpan { Floor = 0f, Headroom = 2f, Flags = NavSpanFlags.Walkable });
                    tile.Columns[z * n + x] = new NavColumn { First = first, Count = spans.Count - first };
                }
            tile.Spans = spans.ToArray();
            nav.Tiles[t] = tile;
        }
        NavBake.Link(nav);
        return nav;
    }

    private static int At(NavWorld nav, float x, float z)
    {
        nav.TryFindSpan(new Vector3(x, 0.1f, z), out int g, 6f);
        return g;
    }

    /// <summary>Breadth-first over the same links: the reference the A* is measured against.</summary>
    private static int Hops(NavWorld nav, int from, int to)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((from, 0));
        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node == to) return depth;
            if (!nav.Locate(node, out var t, out int s)) continue;
            var slice = t.LinkSlices[s];
            for (int k = slice.First; k < slice.First + slice.Count; k++)
            {
                int next = t.Links[k].To;
                if (nav.Locate(next, out var nt, out int ns) && nt.Spans[ns].Open && seen.Add(next)) queue.Enqueue((next, depth + 1));
            }
        }
        return -1;
    }

    private static float Length(NavPath p)
    {
        float d = 0f;
        for (int i = 1; i < p.Count; i++) d += (p.Corners[i] - p.Corners[i - 1]).magnitude;
        return d;
    }

    private static int LinkCount(NavWorld nav, int global)
    {
        return nav.Locate(global, out var t, out int s) ? t.LinkSlices[s].Count : -1;
    }

    public static void Run(Action<bool, string> True)
    {
        // ---- the worker-safe queries give the main-thread answers (the bake and the AI jobs run on them)
        {
            using var w = new RipNRage.Engine.Ecs.World(0);
            RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(w, new Vector3(0, 1.5f, 0), Quaternion.identity, new Vector3(2, 3, 2), Layers.Walkable);
            for (int i = 0; i < 4; i++) w.Update(1f / 60f);
            int mask = 1 << Layers.Walkable;
            var rays = new[]
            {
                (new Vector3(0.25f, 6f, 0.25f), Vector3.down, 10f),     // onto the top
                (new Vector3(-8f, 1.5f, 0.25f), Vector3.right, 20f),    // into the side
                (new Vector3(-8f, 1.5f, 5f), Vector3.right, 20f),       // a miss, beside it
                (new Vector3(0.25f, 6f, 0.25f), Vector3.down, 2f),      // too short to reach
            };
            bool agree = true;
            foreach (var (o, d, len) in rays)
            {
                bool a = RipNRage.Engine.Physics.Raycast(o, d, out var ha, len, mask);
                bool b = RipNRage.Engine.Physics.RaycastConcurrent(o, d, out var hb, len, mask);
                if (a != b || (a && Mathf.Abs(ha.distance - hb.distance) > 1e-4f)) agree = false;
            }
            True(agree, "nav: RaycastConcurrent gives Raycast's answer, hit for hit");
            // agreement is the claim. (A capsule wholly inside a box reads as *not* overlapping on both paths: the
            // GJK deep-overlap blind spot the suite's own box report notes. The bake does not rely on it — a span
            // enclosed in a solid is caught by the roof ray, not the capsule.)
            var lo = new Vector3(0, 0.4f, 0); var hi = new Vector3(0, 2.5f, 0);
            var far = new Vector3(5, 0.4f, 0); var farHi = new Vector3(5, 2.5f, 0);
            var edge = new Vector3(1.2f, 0.4f, 0); var edgeHi = new Vector3(1.2f, 2.5f, 0); // 0.2 m off the face, radius 0.3: touching
            bool a1 = RipNRage.Engine.Physics.CheckCapsule(lo, hi, 0.3f, mask), b1 = RipNRage.Engine.Physics.CheckCapsuleConcurrent(lo, hi, 0.3f, mask);
            bool a2 = RipNRage.Engine.Physics.CheckCapsule(far, farHi, 0.3f, mask), b2 = RipNRage.Engine.Physics.CheckCapsuleConcurrent(far, farHi, 0.3f, mask);
            bool a3 = RipNRage.Engine.Physics.CheckCapsule(edge, edgeHi, 0.3f, mask), b3 = RipNRage.Engine.Physics.CheckCapsuleConcurrent(edge, edgeHi, 0.3f, mask);
            // agreement only: in this process the shared broadphase does not give this one-box world a positive
            // capsule overlap at all (not even touching the face), while the game's bake marks thousands of spans
            // Tight with the same call. Which answer is right is the physics suite's question; that the two paths
            // give the *same* one is this suite's.
            True(a1 == b1 && a2 == b2 && a3 == b3, $"nav: CheckCapsuleConcurrent gives CheckCapsule's answer ({a1}/{b1} {a2}/{b2} {a3}/{b3})");
        }

        var search = new NavSearcher();
        var path = new NavPath();

        // ---- the grid itself
        {
            var nav = Grid("...",
                           "...",
                           "...");
            True(nav.TotalSpans == 9, $"nav: a 3x3 floor is nine spans (got {nav.TotalSpans})");
            True(At(nav, 0.5f, 0.5f) >= 0 && At(nav, 2.5f, 2.5f) >= 0, "nav: a point on the floor finds its span");
            True(At(nav, 50f, 50f) < 0, "nav: a point off the grid finds nothing");
            True(LinkCount(nav, At(nav, 1.5f, 1.5f)) == 8, "nav: the middle of a flat floor links to all eight neighbours");
            True(LinkCount(nav, At(nav, 0.5f, 0.5f)) == 3, "nav: a corner links to three");
        }

        // ---- verticality: a step is walked both ways, a drop is one-way, a deep drop is no link at all
        {
            var nav = Grid("..s");
            True(search.Search(nav, At(nav, 0.5f, 0.5f), At(nav, 2.5f, 0.5f), path) == NavStatus.Ok, "nav: a 0.4 m step is walked up");
            True(search.Search(nav, At(nav, 2.5f, 0.5f), At(nav, 0.5f, 0.5f), path) == NavStatus.Ok, "nav: and back down again");

            var drop = Grid("..d");
            True(search.Search(drop, At(drop, 0.5f, 0.5f), At(drop, 2.5f, 0.5f), path) == NavStatus.Ok, "nav: a 2 m drop is taken");
            // not Ok: the search may still hand back a partial way to the foot of the drop, which is progress
            True(search.Search(drop, At(drop, 2.5f, 0.5f), At(drop, 0.5f, 0.5f), path) != NavStatus.Ok,
                 "nav: a 2 m drop is one-way - the AI falls down it and cannot climb back");

            var cliff = Grid("..D");
            True(search.Search(cliff, At(cliff, 0.5f, 0.5f), At(cliff, 2.5f, 0.5f), path) != NavStatus.Ok,
                 "nav: a drop deeper than MaxDrop is not a way at all");
        }

        // ---- no corner cutting: a diagonal needs both of its orthogonals
        {
            //  . #
            //  # .      the two floors touch only at the corner
            var nav = Grid(".#",
                           "#.");
            int a = At(nav, 0.5f, 0.5f), b = At(nav, 1.5f, 1.5f);
            True(a >= 0 && b >= 0, "nav: both corner floors exist");
            True(LinkCount(nav, a) == 0, "nav: a diagonal through a gap is not a link");
            True(search.Search(nav, a, b, path) == NavStatus.Unreachable, "nav: the corner cannot be cut");

            var open = Grid("..",
                            "..");
            True(LinkCount(nav, a) < LinkCount(open, At(open, 0.5f, 0.5f)), "nav: with the orthogonals open the diagonal is a link");
        }

        // ---- a span the body does not fit in is not walked on
        {
            var nav = Grid(".x.");
            True(search.Search(nav, At(nav, 0.5f, 0.5f), At(nav, 2.5f, 0.5f), path) == NavStatus.Unreachable,
                 "nav: a span too tight for the body is not a way through");
        }

        // ---- A* agrees with breadth-first, and goes round a wall rather than through it
        {
            //  the wall runs across, open at the right
            var nav = Grid(".....",
                           "####.",
                           ".....");
            int from = At(nav, 0.5f, 0.5f), to = At(nav, 0.5f, 2.5f);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav: A* finds the way round the wall");
            True(Hops(nav, from, to) > 0, "nav: breadth-first agrees there is a way");
            True(Length(path) > 4f, "nav: the way round is longer than the blocked straight line");
            True(path.Count >= 2 && path.Count < 12, $"nav: the way is string-pulled to a few corners (got {path.Count})");

            // every corner sits on a walkable span
            bool onGrid = true;
            for (int i = 0; i < path.Count; i++) if (At(nav, path.Corners[i].x, path.Corners[i].z) < 0) onGrid = false;
            True(onGrid, "nav: every corner stands on a span");
        }

        // ---- walled off is Unreachable, both for A* and for the reference
        {
            var nav = Grid(".#.",
                           ".#.",
                           ".#.");
            int l = At(nav, 0.5f, 1.5f), r = At(nav, 2.5f, 1.5f);
            True(search.Search(nav, l, r, path) == NavStatus.Unreachable, "nav: a walled-off goal is unreachable");
            True(Hops(nav, l, r) == -1, "nav: breadth-first agrees it is walled off");
        }

        // ---- across tiles: a link may cross a tile edge, and the way through one gap is found
        {
            var open = TwoTiles(wallBetween: false);
            var walled = TwoTiles(wallBetween: true);
            int a = At(open, 1.5f, 8.5f), b = At(open, TileSize + 1.5f, 8.5f);
            True(a >= 0 && b >= 0 && open.TilesX == 2, "nav: two tiles, a span in each");
            True(search.Search(open, a, b, path) == NavStatus.Ok, "nav: a search crosses a tile edge");
            float straight = Length(path);

            int c = At(walled, 1.5f, 8.5f), d = At(walled, TileSize + 1.5f, 8.5f);
            True(search.Search(walled, c, d, path) == NavStatus.Ok, "nav: with a wall between the tiles the one gap is found");
            True(Length(path) > straight, "nav: and the way through the gap is longer than the open one");
        }

        // ---- a carve shuts a way, lifting it opens it again (the counter 11.5 will drive)
        {
            var nav = Grid(".....",
                           ".....",
                           ".....");
            int from = At(nav, 0.5f, 1.5f), to = At(nav, 4.5f, 1.5f);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav: the open floor is crossed");

            var tile = nav.Tiles[0];
            int shut = 0;
            for (int i = 0; i < tile.Spans.Length; i++)
            {
                var p = nav.PositionOf(tile, i);
                if (p.x > 2f && p.x < 3f) { tile.Spans[i].Dynamic++; shut++; }
            }
            True(shut == 3, $"nav: the carve shut the middle column (got {shut})");
            // not Ok: the way to the carve is still handed back, so the body walks up to the shut door and stops
            True(search.Search(nav, from, to, path) != NavStatus.Ok, "nav: a carved line is not walked through");

            for (int i = 0; i < tile.Spans.Length; i++) if (tile.Spans[i].Dynamic > 0) tile.Spans[i].Dynamic--;
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav: lifting the carve opens the way again");

            // two things on one span: taking one away leaves it shut
            tile.Spans[0].Dynamic += 2;
            tile.Spans[0].Dynamic--;
            True(!tile.Spans[0].Open, "nav: a carve is a counter, not a flag - two on a span, one gone, still shut");
        }

        // ---- 11.5 the carve: a footprint stamped on the spans it covers, lifted off the ones it leaves
        {
            var nav = Grid(".....",
                           ".....",
                           ".....");
            int from = At(nav, 0.5f, 1.5f), to = At(nav, 4.5f, 1.5f);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav carve: the open floor is crossed");
            int v0 = nav.Version;

            // a 1 m box over the middle column, standing on the floor
            int n = NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), +1);
            True(n == 3, $"nav carve: the stamp covered the middle column's three spans (got {n})");
            True(nav.Version > v0, "nav carve: a stamp moves the grid version on");
            True(search.Search(nav, from, to, path) != NavStatus.Ok, "nav carve: and the way through it is shut");

            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), -1);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav carve: lifting it opens the way again");

            // two things on one span, one taken away: still shut. A flag would get this wrong; a counter does not.
            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), +1);
            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), +1);
            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), -1);
            True(search.Search(nav, from, to, path) != NavStatus.Ok, "nav carve: two on a span, one gone, still shut");
            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), -1);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav carve: and the second lift opens it");

            // the counter never goes below zero, however many lifts arrive
            NavCarve.Stamp(nav, new Vector3(2.5f, 0.5f, 1.5f), new Vector3(0.4f, 1f, 1.4f), -1);
            True(search.Search(nav, from, to, path) == NavStatus.Ok, "nav carve: an extra lift does not wrap the counter");

            // a stamp that reaches nothing touches nothing
            True(NavCarve.Stamp(nav, new Vector3(50f, 0.5f, 50f), new Vector3(1f, 1f, 1f), +1) == 0, "nav carve: a stamp off the grid touches no span");
            // a stamp well above the floor does not reach it
            True(NavCarve.Stamp(nav, new Vector3(2.5f, 9f, 1.5f), new Vector3(0.5f, 0.5f, 0.5f), +1) == 0, "nav carve: a stamp in the air does not block the floor under it");
        }

        // ---- determinism and the allocation floor
        {
            var nav = Grid(".....",
                           "..#..",
                           ".....");
            int from = At(nav, 0.5f, 1.5f), to = At(nav, 4.5f, 1.5f);
            var p1 = new NavPath(); var p2 = new NavPath();
            new NavSearcher().Search(nav, from, to, p1);
            new NavSearcher().Search(nav, from, to, p2);
            bool same = p1.Count == p2.Count && p1.Status == p2.Status;
            for (int i = 0; same && i < p1.Count; i++)
                same = p1.Corners[i].x == p2.Corners[i].x && p1.Corners[i].y == p2.Corners[i].y && p1.Corners[i].z == p2.Corners[i].z;
            True(same, "nav: two searchers give byte-identical corners");

            for (int i = 0; i < 8; i++) search.Search(nav, from, to, path);   // warm: the pools reach their size
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) search.Search(nav, from, to, path);
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            True(spent == 0, $"nav: a warm search allocates nothing (spent {spent} bytes over 32 searches)");
        }

        // ---- the budget gives back a partial way rather than nothing
        {
            var nav = TwoTiles(wallBetween: true);
            var small = new NavSearcher { MaxNodes = 12 };
            var status = small.Search(nav, At(nav, 1.5f, 8.5f), At(nav, TileSize + 1.5f, 8.5f), path);
            True(status == NavStatus.Partial, $"nav: a search that runs out of budget returns a partial way (got {status})");
            True(path.Count > 0, "nav: and the partial way has corners to walk");
        }
    }
}
