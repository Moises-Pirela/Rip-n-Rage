using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs.Nav;
using RipNRage.ImGuiIntegration;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.Editor
{
    /// <summary>
    /// The navigation panel (F6): what the AI can and cannot do with the level as it stands. It bakes the grid the
    /// game would bake — the same `NavBake`, the same worldspawn settings — draws it in three colours, and answers
    /// the question a designer actually has, which is "why can't they get there".
    ///
    /// Baking is on request, never on edit: it is ~170 ms and the editor rebuilds its preview on every keystroke.
    /// The badge says when the grid is older than the level.
    /// </summary>
    public partial class EditorController
    {
        private bool _navOpen;
        private bool _navStale = true;
        private bool _navDraw = true;
        private float _navRadius = 40f;
        private int _navCap = 20000;
        private bool _navLinks = true, _navTileLines = true, _navPaint = true;
        private string _navStatus = "not baked";
        private int _navTiles, _navSpans, _navWalkable, _navTight, _navCarved, _navColliderCount, _navIslands, _navReached;
        private double _navMs;
        private bool[] _navReach;

        /// <summary>Open from a script or a key; the draw switch lives inside the panel (the editor has no nav hotkey).</summary>
        public bool NavOpen { get => _navOpen; set => _navOpen = value; }
        public bool NavDraw { get => _navDraw; set => _navDraw = value; }

        /// <summary>Builds the collision, bakes, measures, and floods from the player start. The panel's one button.</summary>
        public void NavRebake()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _navColliderCount = BuildNavCollision();
            var nav = BakeNav();
            watch.Stop();
            _navMs = watch.Elapsed.TotalMilliseconds;

            _navTiles = _navSpans = _navWalkable = _navTight = _navCarved = 0;
            foreach (var t in nav.Tiles)
            {
                if (t == null) continue;
                _navTiles++;
                foreach (var sp in t.Spans)
                {
                    _navSpans++;
                    if (sp.Walkable) _navWalkable++;
                    if ((sp.Flags & NavSpanFlags.Tight) != 0) _navTight++;
                    if (sp.Dynamic > 0) _navCarved++;
                }
            }
            NavFlood(nav);
            _navPlacements = null; // resolved again with the grid
            _navStale = false;
            _navStatus = $"baked in {_navMs:0} ms";
        }

        /// <summary>
        /// 11.17.4: everything the AI can reach from the player start, by flooding the link graph it would walk.
        /// What stays unpainted is an island — a piece of level the AI cannot get to, which is the bug a designer
        /// is usually looking for. One-way drops are followed the way the AI would follow them: down only.
        /// </summary>
        private void NavFlood(NavWorld nav)
        {
            _navReach = new bool[nav.TotalSpans];
            _navReached = 0; _navIslands = 0;
            var start = _preview?.playerStart ?? Vector3.zero;
            if (nav.TryFindSpan(start, out int from, 6f))
            {
                var queue = new Queue<int>();
                queue.Enqueue(from); _navReach[from] = true; _navReached = 1;
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    if (!nav.Locate(node, out var tile, out int local)) continue;
                    var slice = tile.LinkSlices[local];
                    for (int k = slice.First; k < slice.First + slice.Count; k++)
                    {
                        int next = tile.Links[k].To;
                        if (next < 0 || next >= _navReach.Length || _navReach[next]) continue;
                        if (!nav.Locate(next, out var nt, out int ns) || !nt.Spans[ns].Open) continue;
                        _navReach[next] = true; _navReached++;
                        queue.Enqueue(next);
                    }
                }
            }
            // what the body could stand on but cannot be walked to from the start
            foreach (var t in nav.Tiles)
            {
                if (t == null) continue;
                for (int i = 0; i < t.Spans.Length; i++)
                    if (t.Spans[i].Open && !_navReach[t.SpanBase + i]) _navIslands++;
            }
            NavDebug.Reach = _navPaint ? _navReach : null;


        }

        /// <summary>The grid is older than the level: every edit goes through Rebuild, which says so.</summary>
        public void NavMarkStale() { _navStale = true; _navPlacements = null; _navStatus = "the level changed since this grid was baked"; }

        /// <summary>Called from DrawGizmos: the grid around the camera, with the panel's switches.</summary>
        private void DrawNavGizmos()
        {
            NavDebug.Enabled = _navDraw && _navOpen && Navigation.Current != null;
            if (!NavDebug.Enabled) return;
            NavDebug.Radius = _navRadius;
            NavDebug.MaxSpans = _navCap;
            NavDebug.ShowLinks = _navLinks;
            NavDebug.ShowTiles = _navTileLines;
            NavDebug.Reach = _navPaint ? _navReach : null;
            NavDebug.Draw(cam?.position ?? Vector3.zero);
            DrawNavPlacements();
        }

        /// <summary>
        /// 11.17.5: the things the grid does not show. A class that carries a <c>NavObstacle</c> is drawn as the
        /// footprint it will stamp when it moves (the grid never shows it, because the editor does not carve), and
        /// a monster is drawn with the circle it will wander inside — both read from the prefab and the enemy
        /// definition, so they are what the game will do.
        /// </summary>
        private void DrawNavPlacements()
        {
            if (!_navOpen || !_navDraw) return;
            if (_navPlacements == null) BuildNavPlacements();
            var carve = new Color(1f, 0.35f, 0.3f, 0.8f);
            var roam = new Color(0.6f, 0.85f, 1f, 0.5f);
            foreach (var (centre, half, radius) in _navPlacements)
            {
                if (half != Vector3.zero) RipNRage.Engine.Graphics.DebugDraw.Box(centre - half, centre + half, carve, false);
                if (radius > 0f) NavCircle(centre, radius, roam);
            }
        }

        /// <summary>
        /// The placements, resolved once: a prefab lookup per entity per frame is what the first version did, and
        /// the editor draws every frame. Rebuilt when the grid is (Rebake) or when the document changes.
        /// </summary>
        private List<(Vector3 centre, Vector3 half, float radius)> _navPlacements;

        private void BuildNavPlacements()
        {
            _navPlacements = new List<(Vector3, Vector3, float)>();
            _navEnemyDefs ??= RipNRage.Game.Content.EnemyDefinitions.Load(_ => null);
            foreach (var e in doc.entities)
            {
                var def = RipNRage.Game.Content.Level.EntityRegistry.Get(e.classname);
                if (def?.prefab == null) continue;
                RipNRage.Engine.Ecs.Prefab prefab;
                try { prefab = RipNRage.Game.Content.Level.GamePrefabs.Library.Get(def.prefab); } catch { continue; }

                var half = prefab.Get<NavObstacleConfig>() != null ? NavFootprint(prefab) : Vector3.zero;
                float radius = 0f;
                foreach (var enemy in _navEnemyDefs)
                    if (string.Equals(enemy.classname, e.classname, System.StringComparison.OrdinalIgnoreCase)) { radius = enemy.wanderRadius; break; }
                if (half != Vector3.zero || radius > 0f) _navPlacements.Add((e.origin, half, radius));
            }
        }

        private RipNRage.Game.Ecs.AI.EnemyData[] _navEnemyDefs;

        /// <summary>The half extents a NavObstacle will stamp: its own, or the collider's.</summary>
        private static Vector3 NavFootprint(RipNRage.Engine.Ecs.Prefab prefab)
        {
            var ob = prefab.Get<NavObstacleConfig>();
            if (ob?.halfExtents != null && ob.halfExtents.Length == 3)
                return new Vector3(ob.halfExtents[0], ob.halfExtents[1], ob.halfExtents[2]);
            var shape = prefab.Get<RipNRage.Engine.Ecs.Physics.ColliderShapeConfig>();
            if (shape == null) return Vector3.zero;
            var baked = shape.Bake();
            var sized = prefab.Get<RipNRage.Engine.Ecs.Physics.ShapeSizeConfig>();
            if (sized?.size != null && sized.size.Length == 3) baked.Size = new Vector3(sized.size[0], sized.size[1], sized.size[2]);
            return baked.Kind == RipNRage.Engine.Ecs.Physics.ShapeKind.Box ? baked.Size * 0.5f
                 : new Vector3(baked.Radius, baked.Radius, baked.Radius);
        }

        private static void NavCircle(Vector3 centre, float radius, Color c)
        {
            if (radius <= 0f) return;
            const int steps = 32;
            var prev = centre + new Vector3(radius, 0.05f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * 2f * Mathf.PI / steps;
                var at = centre + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                RipNRage.Engine.Graphics.DebugDraw.Line(prev, at, c, false);
                prev = at;
            }
        }

        /// <summary>What the gate reads instead of the panel's pixels.</summary>
        public string NavSummary() =>
            $"tiles {_navTiles} spans {_navSpans} walkable {_navWalkable} tight {_navTight} carved {_navCarved} "
            + $"colliders {_navColliderCount} reached {_navReached} unreachable {_navIslands} stale {_navStale} drawn {NavDebug.LastDrawn}";

        /// <summary>The panel itself.</summary>
        private void DrawNavWindow()
        {
            if (!_navOpen) return;
            ImGui.SetNextWindowSize(new Vec2(360f, 420f), ImGuiCond.Appearing);
            bool open = _navOpen;
            if (!ImGui.Begin("Navigation (F6)", ref open)) { _navOpen = open; ImGui.End(); return; }
            _navOpen = open;

            if (ImGui.Button("Rebake")) NavRebake();
            ImGui.SameLine();
            if (_navStale) ImGui.TextColored(new Vec4(1f, 0.78f, 0.25f, 1f), "STALE");
            else ImGui.TextColored(new Vec4(0.55f, 0.58f, 0.63f, 1f), _navStatus);

            ImGui.SeparatorText("What was baked");
            ImGui.Text($"tiles {_navTiles}   spans {_navSpans}");
            ImGui.Text($"walkable {_navWalkable}");
            ImGui.TextColored(new Vec4(1f, 0.55f, 0.1f, 1f), $"too tight for the body {_navTight}");
            ImGui.Text($"carved {_navCarved}   colliders {_navColliderCount}");
            ImGui.SeparatorText("Reachable from the player start");
            ImGui.TextColored(new Vec4(0.3f, 0.65f, 1f, 1f), $"reached {_navReached}");
            ImGui.TextColored(_navIslands > 0 ? new Vec4(1f, 0.4f, 0.35f, 1f) : new Vec4(0.55f, 0.58f, 0.63f, 1f),
                              $"unreachable {_navIslands}");
            if (_navIslands > 0) ImGui.TextWrapped("Spans the body fits on that no path from the start reaches: an island.");

            ImGui.SeparatorText("Draw");
            ImGui.Checkbox("Show the grid", ref _navDraw);
            ImGui.Checkbox("Paint what is reachable", ref _navPaint);
            ImGui.Checkbox("One-way drops", ref _navLinks);
            ImGui.Checkbox("Tile outlines", ref _navTileLines);
            ImGui.SliderFloat("Radius (m)", ref _navRadius, 10f, 160f, "%.0f");
            ImGui.SliderInt("Span cap", ref _navCap, 2000, 120000);
            ImGui.TextColored(new Vec4(0.55f, 0.58f, 0.63f, 1f), $"drawn {NavDebug.LastDrawn}");
            ImGui.TextWrapped("Green walkable, blue reachable from the start, orange too tight for the body, grey the level, red carved by something that moves.");
            ImGui.End();
        }
    }
}
