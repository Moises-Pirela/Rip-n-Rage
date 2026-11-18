using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Ecs.Nav
{
    public enum NavStatus
    {
        /// <summary>A way to the goal was found.</summary>
        Ok,
        /// <summary>The budget ran out; the corners lead to the closest node reached.</summary>
        Partial,
        /// <summary>There is no way: the goal is walled off, or either end is off the grid.</summary>
        Unreachable,
    }

    /// <summary>A found way, as world positions to walk through. Reused — read <see cref="Count"/>, not the array's length.</summary>
    public sealed class NavPath
    {
        public Vector3[] Corners = new Vector3[64];
        public int Count;
        public NavStatus Status = NavStatus.Unreachable;

        public void Clear() { Count = 0; Status = NavStatus.Unreachable; }

        public void Add(Vector3 p)
        {
            if (Count == Corners.Length) Array.Resize(ref Corners, Corners.Length * 2);
            Corners[Count++] = p;
        }
    }

    /// <summary>
    /// A* over the span graph. One searcher per worker, **pooled and reused**: the heap and the per-node arrays are
    /// allocated once and cleared by a stamp, so a search in the steady state allocates nothing (9.8 got the game to
    /// 6.0 KB/frame and this must not spend it back).
    ///
    /// Determinism: the open set breaks ties on the node index, so the same grid and the same ends always give the
    /// same corners — the gate sweep diffs its probe lines byte for byte.
    /// </summary>
    public sealed class NavSearcher
    {
        private float[] _g = Array.Empty<float>(), _f = Array.Empty<float>();
        private int[] _came = Array.Empty<int>(), _stamp = Array.Empty<int>();
        private byte[] _closed = Array.Empty<byte>();
        private int[] _heap = Array.Empty<int>();
        private int _heapCount, _epoch;
        private readonly List<int> _nodes = new List<int>(64);

        /// <summary>How many nodes a single search may open before it gives up and returns its best partial.</summary>
        public int MaxNodes = 4000;
        /// <summary>Nodes opened by the last search — the probe the gates print.</summary>
        public int LastNodes { get; private set; }

        private void Ensure(int n)
        {
            if (_g.Length >= n) return;
            int size = Mathf.Max(n, 64);
            Array.Resize(ref _g, size); Array.Resize(ref _f, size); Array.Resize(ref _came, size);
            Array.Resize(ref _stamp, size); Array.Resize(ref _closed, size); Array.Resize(ref _heap, size + 1);
        }

        /// <summary>
        /// The way from one span to another, as corners in <paramref name="into"/>. Both ends are global span
        /// indices (<see cref="NavWorld.TryFindSpan"/> turns a position into one).
        /// </summary>
        public NavStatus Search(NavWorld nav, int from, int to, NavPath into)
        {
            into.Clear();
            LastNodes = 0;
            if (nav == null || nav.TotalSpans == 0 || from < 0 || to < 0) return Fail(into);

            Ensure(nav.TotalSpans);
            _epoch++;
            _heapCount = 0;

            if (from == to)
            {
                into.Add(nav.PositionOf(to));
                into.Status = NavStatus.Ok;
                return NavStatus.Ok;
            }

            var goal = nav.PositionOf(to);
            Open(from, 0f, Heuristic(nav.PositionOf(from), goal), -1);

            int best = from; float bestH = Heuristic(nav.PositionOf(from), goal);
            while (_heapCount > 0)
            {
                int current = Pop();
                if (_closed[current] == 1) continue;
                _closed[current] = 1;
                LastNodes++;

                if (current == to) return Build(nav, from, to, into, NavStatus.Ok);
                if (LastNodes >= MaxNodes) break;

                if (!nav.Locate(current, out var tile, out int span)) continue;
                var here = nav.PositionOf(tile, span);
                float h = Heuristic(here, goal);
                if (h < bestH) { bestH = h; best = current; }

                var slice = tile.LinkSlices[span];
                for (int k = slice.First; k < slice.First + slice.Count; k++)
                {
                    var link = tile.Links[k];
                    int next = link.To;
                    if (_stamp[next] == _epoch && _closed[next] == 1) continue;
                    if (!Walkable(nav, next)) continue;            // carved shut since the bake (11.5)
                    float g = _g[current] + link.Cost;
                    if (_stamp[next] == _epoch && g >= _g[next]) continue;
                    Open(next, g, g + Heuristic(nav.PositionOf(next), goal), current);
                }
            }

            // nothing reached the goal: give back the way to the closest node we did reach, which is still progress
            if (best != from && _stamp[best] == _epoch) return Build(nav, from, best, into, NavStatus.Partial);
            return Fail(into);
        }

        /// <summary>A node is only walkable if nothing moving is standing on it — the carve counter (11.5).</summary>
        private static bool Walkable(NavWorld nav, int global) =>
            nav.Locate(global, out var t, out int s) && t.Spans[s].Open;

        private static float Heuristic(Vector3 a, Vector3 b) => (a - b).magnitude;

        private static NavStatus Fail(NavPath into) { into.Status = NavStatus.Unreachable; return NavStatus.Unreachable; }

        private NavStatus Build(NavWorld nav, int from, int to, NavPath into, NavStatus status)
        {
            _nodes.Clear();
            for (int at = to; at != -1; at = _came[at])
            {
                _nodes.Add(at);
                if (at == from) break;
            }
            _nodes.Reverse();
            StringPull(nav, _nodes, into);
            into.Status = status;
            return status;
        }

        /// <summary>
        /// Drops the corners a straight walk does not need. A run of spans at the same height collapses to its two
        /// ends; a **step or a drop stays a corner**, because the body has to arrive at it before the height
        /// changes. Cheap and purely geometric — no physics query.
        /// </summary>
        private void StringPull(NavWorld nav, List<int> nodes, NavPath into)
        {
            if (nodes.Count == 0) return;
            float step = nav.Settings.CellHeight;
            var anchor = nav.PositionOf(nodes[0]);
            into.Add(anchor);
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var prev = nav.PositionOf(nodes[i - 1]);
                var here = nav.PositionOf(nodes[i]);
                var next = nav.PositionOf(nodes[i + 1]);
                if (Mathf.Abs(here.y - prev.y) > step || Mathf.Abs(next.y - here.y) > step) { into.Add(here); anchor = here; continue; }
                // still in line with the anchor? then this corner is not needed
                var a = new Vector3(here.x - anchor.x, 0f, here.z - anchor.z);
                var b = new Vector3(next.x - anchor.x, 0f, next.z - anchor.z);
                if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) continue;
                a.Normalize(); b.Normalize();
                if (Vector3.Dot(a, b) < 0.9999f) { into.Add(here); anchor = here; }
            }
            if (nodes.Count > 1) into.Add(nav.PositionOf(nodes[nodes.Count - 1]));
        }

        // ---- the heap. Ties break on the node index, never on insertion order: determinism is a requirement.
        private void Open(int node, float g, float f, int came)
        {
            if (_stamp[node] != _epoch) { _stamp[node] = _epoch; _closed[node] = 0; }
            _g[node] = g; _f[node] = f; _came[node] = came;
            Push(node);
        }

        private bool Less(int a, int b) => _f[a] != _f[b] ? _f[a] < _f[b] : a < b;

        private void Push(int node)
        {
            if (_heapCount + 1 >= _heap.Length) Array.Resize(ref _heap, _heap.Length * 2 + 1);
            int i = ++_heapCount;
            _heap[i] = node;
            while (i > 1 && Less(_heap[i], _heap[i >> 1])) { (_heap[i], _heap[i >> 1]) = (_heap[i >> 1], _heap[i]); i >>= 1; }
        }

        private int Pop()
        {
            int top = _heap[1];
            _heap[1] = _heap[_heapCount--];
            int i = 1;
            while (true)
            {
                int l = i << 1, r = l + 1, best = i;
                if (l <= _heapCount && Less(_heap[l], _heap[best])) best = l;
                if (r <= _heapCount && Less(_heap[r], _heap[best])) best = r;
                if (best == i) break;
                (_heap[i], _heap[best]) = (_heap[best], _heap[i]);
                i = best;
            }
            return top;
        }
    }
}
