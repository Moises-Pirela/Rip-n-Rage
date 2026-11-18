using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Nav
{
    /// <summary>
    /// Builds a <see cref="NavWorld"/> out of the level's static colliders, by asking the physics world the same
    /// questions the body would: is there a floor here, is it flat enough, is there room above it, does the capsule
    /// fit. Synchronous, at level start — a build, not a system (entities from a build are immediate, 9.x).
    /// </summary>
    public static class NavBake
    {
        /// <summary>
        /// A static collider wider than this across is scenery, not ground: the dock's water and sky planes are
        /// 1000 m across, and taking them into the bounds costs 167 MB of grid at 0.5 m against 8 MB without them
        /// (measured at 11.0). The level's extent is the extent of the things you can actually walk on.
        /// </summary>
        public const float SceneryExtent = 200f;

        /// <summary>What counts as ground: the level, not people, pickups or projectiles.</summary>
        public const int GroundMask = (1 << Layers.Default) | (1 << Layers.Wall) | (1 << Layers.Walkable) | (1 << Layers.OnlyGroundCollision);

        /// <summary>
        /// The level's extent and the solids in it. **Two lists, because they answer two different questions**:
        /// <paramref name="bounding"/> is what the grid is sized to (scenery excluded — see
        /// <see cref="SceneryExtent"/>), while <paramref name="all"/> is everything that can hold a body up,
        /// scenery included: the dock's floor plane is 1000 m across and must not stretch the grid, but it is
        /// still the floor, and a column over it has somewhere to stand.
        /// </summary>
        public static bool Bounds(World world, out Vector3 min, out Vector3 max,
                                  out List<(Vector3 lo, Vector3 hi)> bounding, out List<(Vector3 lo, Vector3 hi)> all)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bounding = new List<(Vector3, Vector3)>();
            all = new List<(Vector3, Vector3)>();
            // ColliderShape alone: a level brush built by PhysicsEntities has a LocalTransform and no
            // LocalToWorld until something parents it, so asking for both finds nothing at all.
            var q = world.Query().WithReadOnly<ColliderShape>().Build();
            foreach (var chunk in q)
            {
                var shapes = chunk.Get<ColliderShape>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || !shapes[i].IsStatic) continue;
                    var e = ents[i];
                    Vector3 at = world.Has<LocalToWorld>(e) ? world.Get<LocalToWorld>(e).Position
                               : world.Has<LocalTransform>(e) ? world.Get<LocalTransform>(e).Position : Vector3.zero;
                    if (!BoxOf(in shapes[i], at, out var lo, out var hi)) continue;
                    all.Add((lo, hi));
                    if (hi.x - lo.x > SceneryExtent || hi.z - lo.z > SceneryExtent) continue; // does not size the grid
                    bounding.Add((lo, hi));
                    min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
                }
            }
            return bounding.Count > 0;
        }

        private static bool BoxOf(in ColliderShape s, Vector3 position, out Vector3 lo, out Vector3 hi)
        {
            if (s.Kind == ShapeKind.Convex)
            {
                var asset = PhysicsAssets.Hulls.Resolve(s.Hull);
                if (asset == null || asset.Vertices.Length == 0) { lo = hi = Vector3.zero; return false; }
                lo = hi = asset.Vertices[0]; // StaticConvex interns world-space vertices
                foreach (var v in asset.Vertices) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                return true;
            }
            var c = position + s.Center;
            Vector3 e;
            if (s.Kind == ShapeKind.Box) e = s.Size * 0.5f;
            else if (s.Kind == ShapeKind.Sphere) e = new Vector3(s.Radius, s.Radius, s.Radius);
            else if (s.Kind == ShapeKind.Capsule) e = new Vector3(s.Radius, Mathf.Max(s.Radius, s.Height * 0.5f), s.Radius);
            else { lo = hi = Vector3.zero; return false; } // a mesh collider has no cheap world box here
            lo = c - e; hi = c + e;
            return true;
        }

        /// <summary>
        /// Bakes the level. <paramref name="tileSize"/> is the level's render-chunk size, so a nav tile and a
        /// culling chunk are the same box. Tiles with no static geometry over them stay null.
        /// </summary>
        public static NavWorld Build(World world, in NavSettings settings, float tileSize)
        {
            var nav = new NavWorld { Settings = settings, TileSize = tileSize, BakeId = NavWorld.NextBakeId() };
            if (!Bounds(world, out var min, out var max, out var bounding, out var all)) return nav;

            nav.OriginX = Mathf.Floor(min.x / tileSize) * tileSize;
            nav.OriginZ = Mathf.Floor(min.z / tileSize) * tileSize;
            nav.TilesX = Mathf.Max(1, Mathf.CeilToInt((max.x - nav.OriginX) / tileSize));
            nav.TilesZ = Mathf.Max(1, Mathf.CeilToInt((max.z - nav.OriginZ) / tileSize));
            nav.Tiles = new NavTile[nav.TilesX * nav.TilesZ];

            // which tiles any walkable solid reaches: the rest are never sampled (half the dock, measured at 11.0)
            var used = new bool[nav.Tiles.Length];
            foreach (var (lo, hi) in bounding)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - nav.OriginX) / tileSize), 0, nav.TilesX - 1);
                int x1 = Mathf.Clamp(Mathf.FloorToInt((hi.x - nav.OriginX) / tileSize), 0, nav.TilesX - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - nav.OriginZ) / tileSize), 0, nav.TilesZ - 1);
                int z1 = Mathf.Clamp(Mathf.FloorToInt((hi.z - nav.OriginZ) / tileSize), 0, nav.TilesZ - 1);
                for (int tz = z0; tz <= z1; tz++)
                    for (int tx = x0; tx <= x1; tx++) used[tz * nav.TilesX + tx] = true;
            }

            float top = max.y + 1f, bottom = min.y - 1f;

            // the tiles are the job's items: each one writes only its own arrays and reads a broadphase nothing is
            // moving (we are inside a fixed tick, after the sync), so the result is the same at any thread count —
            // the sweep runs the ECS pool at 8 threads and diffs the probe lines byte for byte. The queries are the
            // worker-safe ones (no visit stamps, a thread's own scratch). Linking stays a serial pass, as before.
            var work = new List<(int tx, int tz)>();
            for (int tz = 0; tz < nav.TilesZ; tz++)
                for (int tx = 0; tx < nav.TilesX; tx++)
                    if (used[tz * nav.TilesX + tx]) work.Add((tx, tz));
            var job = new TileJob { Nav = nav, Work = work, Top = top, Bottom = bottom, All = all, Out = new NavTile[work.Count] };
            world.Jobs.Run(job, work.Count, 1);
            for (int i = 0; i < work.Count; i++)
                if (job.Out[i] != null && job.Out[i].Spans.Length > 0) nav.Tiles[work[i].tz * nav.TilesX + work[i].tx] = job.Out[i];

            Link(nav);
            return nav;
        }

        /// <summary>
        /// Numbers every span across the tiles and builds the moves between them — one pass **after** every tile
        /// has its spans, so a link may cross a tile edge (the spec's portals). Public because a grid can be built
        /// without the physics world (the checks hand-build one and link it with exactly this code).
        /// </summary>
        public static void Link(NavWorld nav)
        {
            int next = 0;
            foreach (var t in nav.Tiles) { if (t == null) continue; t.SpanBase = next; next += t.Spans.Length; }
            nav.TotalSpans = next;
            foreach (var t in nav.Tiles) if (t != null) BuildLinks(nav, t);
        }

        /// <summary>One item per tile on the world's job system; item i fills <see cref="Out"/>[i] and nothing else.</summary>
        private sealed class TileJob : IJob
        {
            public NavWorld Nav; public List<(int tx, int tz)> Work; public float Top, Bottom;
            public List<(Vector3 lo, Vector3 hi)> All; public NavTile[] Out;
            public void Execute(int stage, int start, int end, int worker)
            {
                for (int i = start; i < end; i++) Out[i] = BakeTile(Nav, Work[i].tx, Work[i].tz, Top, Bottom, All);
            }
        }

        /// <summary>One tile's columns and spans: cast down through each column collecting **every** solid top. Runs on a worker.</summary>
        private static NavTile BakeTile(NavWorld nav, int tx, int tz, float top, float bottom, List<(Vector3 lo, Vector3 hi)> solids)
        {
            var s = nav.Settings;
            float size = nav.TileSize;
            int n = Mathf.Max(1, Mathf.RoundToInt(size / s.CellSize));
            var tile = new NavTile
            {
                X = tx, Z = tz, Width = n, Depth = n,
                MinX = nav.OriginX + tx * size, MinZ = nav.OriginZ + tz * size,
                MinY = bottom, MaxY = top,
                Columns = new NavColumn[n * n],
            };
            var spans = new List<NavSpan>(n * n);
            float cosLimit = Mathf.Cos(s.MaxSlopeDeg * Mathf.Deg2Rad);

            // only the solids that reach into this tile: a column none of them covers has no floor to find, and
            // half the dock's columns are of that kind (measured at 11.0). An AABB test costs a fraction of a ray.
            var near = new List<(Vector3 lo, Vector3 hi)>();
            foreach (var box in solids)
                if (box.hi.x >= tile.MinX && box.lo.x <= tile.MinX + size && box.hi.z >= tile.MinZ && box.lo.z <= tile.MinZ + size)
                    near.Add(box);
            if (near.Count == 0) { tile.Spans = Array.Empty<NavSpan>(); return tile; }

            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    float wx = tile.MinX + (cx + 0.5f) * s.CellSize, wz = tile.MinZ + (cz + 0.5f) * s.CellSize;
                    int first = spans.Count;
                    bool covered = false;
                    foreach (var box in near)
                        if (wx >= box.lo.x && wx <= box.hi.x && wz >= box.lo.z && wz <= box.hi.z) { covered = true; break; }
                    if (!covered) { tile.Columns[cz * n + cx] = new NavColumn { First = first, Count = 0 }; continue; }
                    float y = top;
                    for (int guard = 0; guard < 16 && y > bottom; guard++)
                    {
                        if (!global::RipNRage.Engine.Physics.RaycastConcurrent(new Vector3(wx, y, wz), Vector3.down, out var hit, y - bottom, GroundMask)) break;
                        var span = new NavSpan { Floor = hit.point.y, Headroom = 0f, Flags = NavSpanFlags.None };

                        // steep floors hold the level up but are not stood on
                        if (hit.normal.y < cosLimit) span.Flags |= NavSpanFlags.Steep;

                        // headroom: how far up before the next solid
                        float probe = 0.05f;
                        span.Headroom = global::RipNRage.Engine.Physics.RaycastConcurrent(new Vector3(wx, span.Floor + probe, wz), Vector3.up, out var up, s.Height, GroundMask)
                            ? up.distance + probe : s.Height;
                        if (span.Headroom < s.Height) span.Flags |= NavSpanFlags.LowCeiling;
                        // the roof ray: a floor *inside* a solid (the ground under a wall's footprint) has no ceiling the
                        // up-ray can see, because the ray starts inside that solid; and the capsule test cannot see a
                        // body wholly enclosed either (GJK's deep-overlap blind spot). Casting down from the top of the
                        // body's height to just above the floor finds the enclosing solid's top in between.
                        else if (global::RipNRage.Engine.Physics.RaycastConcurrent(new Vector3(wx, span.Floor + s.Height, wz), Vector3.down, out var roof, s.Height - probe * 2f, GroundMask)
                                 && roof.point.y > span.Floor + probe)
                        { span.Headroom = roof.point.y - span.Floor; span.Flags |= NavSpanFlags.LowCeiling; }

                        // clearance: does the body's capsule actually fit here, or is it jammed against a wall
                        if ((span.Flags & (NavSpanFlags.Steep | NavSpanFlags.LowCeiling)) == 0)
                        {
                            float r = s.Radius;
                            var p1 = new Vector3(wx, span.Floor + r + 0.02f, wz);
                            var p2 = new Vector3(wx, span.Floor + s.Height - r, wz);
                            if (global::RipNRage.Engine.Physics.CheckCapsuleConcurrent(p1, p2, r, GroundMask)) span.Flags |= NavSpanFlags.Tight;
                        }

                        if (span.Flags == NavSpanFlags.None) span.Flags = NavSpanFlags.Walkable;
                        // merge with the span just above when they are within a cell height of each other
                        if (spans.Count > first && spans[spans.Count - 1].Floor - span.Floor < s.CellHeight) { }
                        else spans.Add(span);

                        y = span.Floor - Mathf.Max(0.05f, s.CellHeight);
                    }
                    tile.Columns[cz * n + cx] = new NavColumn { First = first, Count = spans.Count - first };
                }

            tile.Spans = spans.ToArray();
            return tile;
        }

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// Every span's moves. A neighbour is reachable when it is at most a step up or at most a drop down; a drop
        /// deeper than the step is **one-way** (the search may fall down it and never climb it). A diagonal needs
        /// both of its orthogonals open at a compatible height — no cutting a corner through a gap.
        /// </summary>
        private static void BuildLinks(NavWorld nav, NavTile tile)
        {
            var s = nav.Settings;
            tile.LinkSlices = new NavLinkSlice[tile.Spans.Length];
            var links = new List<NavLink>(tile.Spans.Length * 4);

            for (int i = 0; i < tile.Spans.Length; i++)
            {
                int start = links.Count;
                if (tile.Spans[i].Walkable)
                {
                    int col = NavWorld.ColumnOfSpan(tile, i);
                    int cx = col % tile.Width, cz = col / tile.Width;
                    var from = nav.PositionOf(tile, i);
                    for (int d = 0; d < 8; d++)
                    {
                        bool diagonal = d >= 4;
                        if (diagonal && !(Reachable(nav, tile, cx + DX[d], cz, from, s) && Reachable(nav, tile, cx, cz + DZ[d], from, s)))
                            continue; // the corner is not open on both sides
                        AddLinks(nav, tile, cx + DX[d], cz + DZ[d], i, from, links);
                    }
                }
                tile.LinkSlices[i] = new NavLinkSlice { First = start, Count = links.Count - start };
            }
            tile.Links = links.ToArray();
        }

        /// <summary>Is there a walkable span in this neighbouring column within a step of <paramref name="from"/>?</summary>
        private static bool Reachable(NavWorld nav, NavTile tile, int cx, int cz, Vector3 from, in NavSettings s)
        {
            if (!Column(nav, tile, cx, cz, out var t, out var col)) return false;
            for (int j = col.First; j < col.First + col.Count; j++)
            {
                if (!t.Spans[j].Walkable) continue;
                float dy = t.Spans[j].Floor - from.y;
                if (dy <= s.StepUp && dy >= -s.MaxDrop) return true;
            }
            return false;
        }

        private static void AddLinks(NavWorld nav, NavTile tile, int cx, int cz, int fromSpan, Vector3 from, List<NavLink> into)
        {
            var s = nav.Settings;
            if (!Column(nav, tile, cx, cz, out var t, out var col)) return;
            for (int j = col.First; j < col.First + col.Count; j++)
            {
                if (!t.Spans[j].Walkable) continue;
                float dy = t.Spans[j].Floor - from.y;
                if (dy > s.StepUp || dy < -s.MaxDrop) continue;
                var to = nav.PositionOf(t, j);
                into.Add(new NavLink
                {
                    To = nav.SpanIndex(t, j),
                    Cost = (to - from).magnitude,
                    OneWay = dy < -s.StepUp,   // too deep to climb back
                });
            }
        }

        /// <summary>A column by tile-local coordinates, crossing into the neighbouring tile when they run off the edge.</summary>
        private static bool Column(NavWorld nav, NavTile tile, int cx, int cz, out NavTile t, out NavColumn col)
        {
            t = tile; col = default;
            int tx = tile.X, tz = tile.Z;
            while (cx < 0) { cx += tile.Width; tx--; }
            while (cx >= tile.Width) { cx -= tile.Width; tx++; }
            while (cz < 0) { cz += tile.Depth; tz--; }
            while (cz >= tile.Depth) { cz -= tile.Depth; tz++; }
            t = nav.TileAt(tx, tz);
            if (t == null) return false;
            col = t.Columns[cz * t.Width + cx];
            return col.Count > 0;
        }
    }
}
