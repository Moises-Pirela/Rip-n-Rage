using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// The collision world: every registered collider, the broadphase that finds candidates for a query, the
    /// narrow phase (exact box-vs-hull sweeps) and the per-step work (kinematic bodies, trigger enter/exit).
    ///
    /// Broadphase: static colliders live in a uniform grid (<see cref="CellSize"/> metres) with their bounds cached
    /// once; a static that spans too many cells goes on a short "large" list instead. Dynamic colliders live in a
    /// second, finer grid (<see cref="DynamicCellSize"/>) by a <b>fat</b> AABB (their bounds plus
    /// <see cref="FatMargin"/>): an entry is only re-bucketed when its real bounds leave the fat box, so resting
    /// and slow bodies cost nothing, and the fat boxes are refreshed once per step (<see cref="RefreshDynamics"/>)
    /// plus whenever a body writes its transform (<see cref="Touch"/>). Raycasts walk both grids cell by cell (3D
    /// DDA) and stop as soon as the best hit is nearer than the next cell. Queries never allocate: candidate lists
    /// are rented from a pool and reused.
    /// </summary>
    public sealed class PhysicsWorld
    {
        /// <summary>Grid cell size in metres. Changing it rebuilds the grid.</summary>
        public float CellSize
        {
            get => _cellSize;
            set { if (value <= 0.1f || value == _cellSize) return; _cellSize = value; Rebuild(); }
        }

        /// <summary>A static spanning more cells than this is kept on the "large" list (always tested) instead of in the grid.</summary>
        public int MaxCellsPerStatic = 64;
        /// <summary>Cell size of the dynamic grid (metres): about the size of a typical moving body.</summary>
        public float DynamicCellSize
        {
            get => _dynCellSize;
            set { if (value <= 0.1f || value == _dynCellSize) return; _dynCellSize = value; RebuildDynamics(); }
        }
        /// <summary>A dynamic spanning more cells than this goes on the "large dynamic" list.</summary>
        public int MaxCellsPerDynamic = 27;
        /// <summary>How much a dynamic collider's grid box is inflated beyond its bounds (re-bucketed only when it leaves that).</summary>
        public float FatMargin = 0.25f;


        private readonly ColliderTable _table = new ColliderTable();
        /// <summary>Every collider's slot (legacy behaviours today, ECS entities from layer 2.4).</summary>
        public ColliderTable Table => _table;

        private float _cellSize = 8f;
        private float _dynCellSize = 2f;
        private readonly List<Collider> _all = new List<Collider>();
        private readonly List<ColliderTable.Slot> _dynamic = new List<ColliderTable.Slot>();
        private readonly List<ColliderTable.Slot> _largeStatic = new List<ColliderTable.Slot>();
        private readonly List<ColliderTable.Slot> _largeDynamic = new List<ColliderTable.Slot>();
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, List<int>> _dynCells = new Dictionary<long, List<int>>();
        private readonly Stack<List<int>> _cellPool = new Stack<List<int>>();
        private Bounds _staticBounds, _dynBounds;
        private bool _hasStatics, _hasDynamics;
        private int _stamp;
        /// <summary>Dynamic entries re-bucketed during the last refresh (overlay / benchmark statistic).</summary>
        public int RebucketedLastStep { get; private set; }

        private readonly List<Collider> _movers = new List<Collider>();
        private readonly List<KinematicBody> _bodies = new List<KinematicBody>();
        private readonly List<SolverBody> _rigidBodies = new List<SolverBody>();
        private readonly List<Constraint> _constraints = new List<Constraint>();
        /// <summary>Every joint / constraint in the simulation (ball, distance, hinge, fixed...).</summary>
        public IReadOnlyList<Constraint> Constraints => _constraints;
        internal List<Constraint> ConstraintList => _constraints;
        /// <summary>Adds a constraint to the simulation (it is removed automatically when it breaks or one of its bodies is destroyed).</summary>
        public void AddConstraint(Constraint c) { if (c != null && !_constraints.Contains(c)) _constraints.Add(c); }
        public void RemoveConstraint(Constraint c) => _constraints.Remove(c);
        /// <summary>Alias of <see cref="Constraints"/> kept for the joint-only callers.</summary>
        public IReadOnlyList<Constraint> Joints => _constraints;
        public void AddJoint(Constraint j) => AddConstraint(j);
        public void RemoveJoint(Constraint j) => RemoveConstraint(j);
        public int ConstraintCount => _constraints.Count;
        /// <summary>The rigid-body contact solver and its tuning.</summary>
        public RigidSolver Rigid { get; } = new RigidSolver();
        /// <summary>Every rigid body in the one simulation (legacy adapters and, from 2.4, ECS entities).</summary>
        public IReadOnlyList<SolverBody> Bodies => _rigidBodies;
        public int RigidBodyCount => _rigidBodies.Count;
        private readonly Stack<List<Collider>> _listPool = new Stack<List<Collider>>();

        // statistics for the debug overlay
        public int StaticCount { get; private set; }
        public int DynamicCount => _dynamic.Count;
        public int CellCount => _cells.Count;
        public int QueriesThisFrame { get; private set; }
        public int RaycastsThisFrame { get; private set; }
        public int SweepsThisFrame { get; private set; }
        /// <summary>Counters of the previous frame (the overlay draws after the counters were reset).</summary>
        public int QueriesLastFrame { get; private set; }
        public int RaycastsLastFrame { get; private set; }
        public int SweepsLastFrame { get; private set; }
        public int BodyCount => _bodies.Count;
        public int MoverCount => _movers.Count;

        public IReadOnlyList<Collider> Colliders => _all;

        // ------------------------------------------------------------------ registration

        public void Add(Collider c)
        {
            if (c == null || c.broadphase != null) return;
            var e = _table.Allocate(c, Ecs.Entity.Null);
            e.isStatic = c.isStatic;
            e.body = c.attachedRigidbody?.body; // either Awake order: RigidBody.ComputeMass sets it too
            c.broadphase = e;
            _all.Add(c);
            Insert(e);
        }

        public void Remove(Collider c)
        {
            if (c == null) return;
            var e = c.broadphase;
            if (e != null)
            {
                Extract(e);
                c.broadphase = null;
                _table.Free(e);
                _all.Remove(c);
            }
            _movers.Remove(c);
        }

        /// <summary>
        /// Registers an ECS collider: the sync system allocates the slot, fills the ECS shape fields, then inserts.
        /// </summary>
        internal ColliderTable.Slot AddEcs(Ecs.Entity entity, bool isStatic)
        {
            var e = _table.Allocate(null, entity);
            e.isStatic = isStatic;
            return e;
        }

        /// <summary>Puts an ECS slot (shape fields filled) into the broadphase.</summary>
        internal void InsertEcs(ColliderTable.Slot e) { if (e != null && e.alive && !e.bucketed && e.collider == null) Insert(e); }

        internal void RemoveEcs(ColliderTable.Slot e)
        {
            if (e == null || !e.alive || e.collider != null) return;
            Extract(e);
            _table.Free(e);
        }

        /// <summary>Re-inserts an ECS slot after its shape or pose was re-baked (static ECS colliders that moved).</summary>
        internal void RefreshEcs(ColliderTable.Slot e) { if (e == null || !e.alive) return; Extract(e); Insert(e); }

        /// <summary>Re-inserts a collider after its static flag, transform or shape changed.</summary>
        public void Refresh(Collider c)
        {
            var e = c?.broadphase;
            if (e == null) return;
            Extract(e);
            e.isStatic = c.isStatic;
            Insert(e);
        }

        private void Insert(ColliderTable.Slot e)
        {
            if (!e.isStatic) { _dynamic.Add(e); BucketDynamic(e, e.CurrentBounds()); return; }
            e.bounds = e.CurrentBounds();
            StaticCount++;
            if (_hasStatics) _staticBounds.Encapsulate(e.bounds); else { _staticBounds = e.bounds; _hasStatics = true; }
            CellRange(e.bounds, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
            if (cells > MaxCellsPerStatic) { e.large = true; _largeStatic.Add(e); return; }
            e.large = false;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long key = Key(x, y, z);
                        if (!_cells.TryGetValue(key, out var list)) { list = new List<int>(4); _cells[key] = list; }
                        list.Add(e.index);
                    }
        }

        private void Extract(ColliderTable.Slot e)
        {
            if (!e.isStatic) { _dynamic.Remove(e); UnbucketDynamic(e); return; }
            StaticCount--;
            if (StaticCount <= 0) { StaticCount = 0; _hasStatics = false; } // a fresh level gets fresh world bounds
            if (e.large) { _largeStatic.Remove(e); return; }
            CellRange(e.bounds, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        if (_cells.TryGetValue(Key(x, y, z), out var list))
                        {
                            list.Remove(e.index);
                            if (list.Count == 0) _cells.Remove(Key(x, y, z));
                        }
        }

        private void Rebuild()
        {
            var statics = new List<ColliderTable.Slot>();
            foreach (var c in _all) if (c.broadphase != null && c.broadphase.isStatic) statics.Add(c.broadphase);
            _cells.Clear(); _largeStatic.Clear();
            StaticCount = 0; _hasStatics = false;
            foreach (var e in statics) Insert(e);
        }

        private int CellOf(float v) => (int)MathF.Floor(v / _cellSize);
        private int DynCellOf(float v) => (int)MathF.Floor(v / _dynCellSize);

        private void CellRange(Bounds b, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            x0 = CellOf(b.min.x); y0 = CellOf(b.min.y); z0 = CellOf(b.min.z);
            x1 = CellOf(b.max.x); y1 = CellOf(b.max.y); z1 = CellOf(b.max.z);
        }

        private void DynCellRange(Bounds b, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            x0 = DynCellOf(b.min.x); y0 = DynCellOf(b.min.y); z0 = DynCellOf(b.min.z);
            x1 = DynCellOf(b.max.x); y1 = DynCellOf(b.max.y); z1 = DynCellOf(b.max.z);
        }

        // ------------------------------------------------------------------ dynamic grid

        /// <summary>Puts a dynamic entry into the grid by a fat box around <paramref name="tight"/>.</summary>
        private void BucketDynamic(ColliderTable.Slot e, Bounds tight)
        {
            e.tight = tight;
            e.bounds = tight.Expanded(FatMargin);
            if (_hasDynamics) _dynBounds.Encapsulate(e.bounds); else { _dynBounds = e.bounds; _hasDynamics = true; }
            DynCellRange(e.bounds, out e.x0, out e.y0, out e.z0, out e.x1, out e.y1, out e.z1);
            long cells = (long)(e.x1 - e.x0 + 1) * (e.y1 - e.y0 + 1) * (e.z1 - e.z0 + 1);
            e.bucketed = true;
            if (cells > MaxCellsPerDynamic) { e.large = true; _largeDynamic.Add(e); return; }
            e.large = false;
            for (int x = e.x0; x <= e.x1; x++)
                for (int y = e.y0; y <= e.y1; y++)
                    for (int z = e.z0; z <= e.z1; z++)
                    {
                        long key = Key(x, y, z);
                        if (!_dynCells.TryGetValue(key, out var list)) { list = _cellPool.Count > 0 ? _cellPool.Pop() : new List<int>(8); _dynCells[key] = list; }
                        list.Add(e.index);
                    }
        }

        private void UnbucketDynamic(ColliderTable.Slot e)
        {
            if (!e.bucketed) return;
            e.bucketed = false;
            if (e.large) { _largeDynamic.Remove(e); return; }
            for (int x = e.x0; x <= e.x1; x++)
                for (int y = e.y0; y <= e.y1; y++)
                    for (int z = e.z0; z <= e.z1; z++)
                    {
                        long key = Key(x, y, z);
                        if (!_dynCells.TryGetValue(key, out var list)) continue;
                        int n = list.Count;
                        for (int i = 0; i < n; i++) if (list[i] == e.index) { list[i] = list[n - 1]; list.RemoveAt(n - 1); break; }
                        if (list.Count == 0) { _dynCells.Remove(key); _cellPool.Push(list); }
                    }
        }

        /// <summary>Re-buckets a dynamic collider whose bounds left its fat box (called by bodies after they move; cheap when they did not).</summary>
        public void Touch(Collider c)
        {
            var e = c?.broadphase;
            if (e == null || e.isStatic) return;
            Touch(e, c.bounds);
        }

        /// <summary>Same with bounds the caller already knows (rigid bodies know their world bounds).</summary>
        public void Touch(Collider c, Bounds tight)
        {
            var e = c?.broadphase;
            if (e == null || e.isStatic) return;
            Touch(e, tight);
        }

        internal void Touch(ColliderTable.Slot e, Bounds tight)
        {
            if (e.bucketed && e.bounds.Contains(tight.min) && e.bounds.Contains(tight.max)) { e.tight = tight; return; }
            UnbucketDynamic(e);
            BucketDynamic(e, tight);
            RebucketedLastStep++;
        }

        /// <summary>
        /// Once per fixed step: refreshes the grid box of every dynamic collider that is not a simulated rigid body
        /// (those touch themselves when they write their pose) — transforms moved by scripts, kinematic movers
        /// between steps — and recomputes the dynamic world bounds.
        /// </summary>
        public void RefreshDynamics()
        {
            RebucketedLastStep = 0;
            bool any = false; Bounds union = default;
            for (int i = 0; i < _dynamic.Count; i++)
            {
                var e = _dynamic[i];
                if (e.collider != null && e.collider.destroyed) continue;
                var b = e.body;
                if (b == null || b.isKinematic || !e.bucketed) Touch(e, e.CurrentBounds());
                if (!e.bucketed) continue;
                if (any) union.Encapsulate(e.bounds); else { union = e.bounds; any = true; }
            }
            _dynBounds = union; _hasDynamics = any;
        }

        private void RebuildDynamics()
        {
            foreach (var e in _dynamic) UnbucketDynamic(e);
            _dynCells.Clear(); _largeDynamic.Clear(); _hasDynamics = false;
            foreach (var e in _dynamic) BucketDynamic(e, e.CurrentBounds());
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF)) | (((long)(y & 0x1FFFFF)) << 21) | (((long)(z & 0x1FFFFF)) << 42);

        // ------------------------------------------------------------------ candidate lists

        internal List<Collider> RentList() => _listPool.Count > 0 ? _listPool.Pop() : new List<Collider>(32);
        internal void ReturnList(List<Collider> list) { list.Clear(); _listPool.Push(list); }

        /// <summary>Broadphase: every collider accepted by the filter whose bounds touch <paramref name="region"/>.</summary>
        public void QueryBounds(Bounds region, QueryFilter filter, List<Collider> results)
        {
            QueriesThisFrame++;
            int stamp = ++_stamp;
            if (_hasDynamics && _dynBounds.Intersects(region))
            {
                foreach (var e in _largeDynamic)
                {
                    if (!e.bounds.Intersects(region)) continue;
                    if (filter.Accepts(e.collider)) results.Add(e.collider);
                }
                if (_dynCells.Count > 0)
                {
                    DynCellRange(region, out int dx0, out int dy0, out int dz0, out int dx1, out int dy1, out int dz1);
                    for (int x = dx0; x <= dx1; x++)
                        for (int y = dy0; y <= dy1; y++)
                            for (int z = dz0; z <= dz1; z++)
                            {
                                if (!_dynCells.TryGetValue(Key(x, y, z), out var list)) continue;
                                for (int i = 0; i < list.Count; i++)
                                {
                                    var e = _table[list[i]];
                                    if (e.stamp == stamp) continue;
                                    e.stamp = stamp;
                                    if (!e.bounds.Intersects(region)) continue;
                                    if (filter.Accepts(e.collider)) results.Add(e.collider);
                                }
                            }
                }
            }
            foreach (var e in _largeStatic)
            {
                if (!e.bounds.Intersects(region)) continue;
                if (filter.Accepts(e.collider)) results.Add(e.collider);
            }
            if (_cells.Count == 0) return;
            if (_hasStatics && !_staticBounds.Intersects(region)) return;
            CellRange(region, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue(Key(x, y, z), out var list)) continue;
                        foreach (int id in list)
                        {
                            var e = _table[id];
                            if (e.stamp == stamp) continue;
                            e.stamp = stamp;
                            if (!e.bounds.Intersects(region)) continue;
                            if (filter.Accepts(e.collider)) results.Add(e.collider);
                        }
                    }
        }

        /// <summary>
        /// Broadphase for worker threads (the rigid solver's narrow phase), over collider slots so ECS colliders take
        /// part: no visit stamps (duplicates from shapes spanning several cells are removed by scanning the short
        /// result list) and only an interlocked counter. Safe to call concurrently as long as nothing inserts or
        /// moves colliders meanwhile.
        /// </summary>
        internal void QueryBoundsConcurrent(Bounds region, QueryFilter filter, List<ColliderTable.Slot> results)
        {
            System.Threading.Interlocked.Increment(ref _queriesConcurrent);
            int first = results.Count;
            if (_hasDynamics && _dynBounds.Intersects(region))
            {
                foreach (var e in _largeDynamic)
                {
                    if (!e.bounds.Intersects(region)) continue;
                    if (filter.Accepts(e)) results.Add(e);
                }
                if (_dynCells.Count > 0)
                {
                    DynCellRange(region, out int dx0, out int dy0, out int dz0, out int dx1, out int dy1, out int dz1);
                    for (int x = dx0; x <= dx1; x++)
                        for (int y = dy0; y <= dy1; y++)
                            for (int z = dz0; z <= dz1; z++)
                            {
                                if (!_dynCells.TryGetValue(Key(x, y, z), out var list)) continue;
                                for (int i = 0; i < list.Count; i++)
                                {
                                    var e = _table[list[i]];
                                    if (!e.bounds.Intersects(region)) continue;
                                    if ((e.x0 != e.x1 || e.y0 != e.y1 || e.z0 != e.z1) && Contains(results, first, e)) continue;
                                    if (filter.Accepts(e)) results.Add(e);
                                }
                            }
                }
            }
            foreach (var e in _largeStatic)
            {
                if (!e.bounds.Intersects(region)) continue;
                if (filter.Accepts(e)) results.Add(e);
            }
            if (_cells.Count == 0) return;
            if (_hasStatics && !_staticBounds.Intersects(region)) return;
            CellRange(region, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            bool several = x0 != x1 || y0 != y1 || z0 != z1;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue(Key(x, y, z), out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var e = _table[list[i]];
                            if (!e.bounds.Intersects(region)) continue;
                            if (several && Contains(results, first, e)) continue;
                            if (filter.Accepts(e)) results.Add(e);
                        }
                    }
        }

        private static bool Contains(List<ColliderTable.Slot> list, int from, ColliderTable.Slot e)
        {
            for (int i = from; i < list.Count; i++) if (list[i] == e) return true;
            return false;
        }

        private int _queriesConcurrent;

        // ------------------------------------------------------------------ raycast

        public bool Raycast(Ray ray, float maxDistance, QueryFilter filter, out RaycastHit hit)
        {
            RaycastsThisFrame++;
            hit = default;
            if (ray.direction.sqrMagnitude < 1E-12f) return false;
            float best = maxDistance;
            bool found = false;
            foreach (var e in _largeDynamic)
            {
                var c = e.collider;
                if (!filter.Accepts(c)) continue;
                if (c.Raycast(ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            foreach (var e in _largeStatic)
            {
                var c = e.collider;
                if (!filter.Accepts(c)) continue;
                if (c.Raycast(ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            if (_dynCells.Count > 0 && _hasDynamics && RaycastGrid(_dynCells, _dynCellSize, _dynBounds, ray, filter, ref best, ref hit)) found = true;
            if (_cells.Count > 0 && _hasStatics && RaycastGrid(_cells, _cellSize, _staticBounds, ray, filter, ref best, ref hit)) found = true;
            return found;
        }

        /// <summary>Walks a grid's cells along the ray (Amanatides &amp; Woo) until the best hit is nearer than the next cell.</summary>
        private bool RaycastGrid(Dictionary<long, List<int>> cells, float cellSize, Bounds worldBounds, Ray ray, QueryFilter filter, ref float best, ref RaycastHit hit)
        {
            if (!ClipRay(ray, worldBounds, best, out float t0, out float t1)) return false;
            bool found = false;
            int stamp = ++_stamp;
            Vector3 p = ray.GetPoint(t0);
            int cx = (int)MathF.Floor(p.x / cellSize), cy = (int)MathF.Floor(p.y / cellSize), cz = (int)MathF.Floor(p.z / cellSize);
            Vector3 d = ray.direction;
            int sx = d.x > 0f ? 1 : d.x < 0f ? -1 : 0, sy = d.y > 0f ? 1 : d.y < 0f ? -1 : 0, sz = d.z > 0f ? 1 : d.z < 0f ? -1 : 0;
            float tx = sx == 0 ? float.PositiveInfinity : t0 + ((cx + (sx > 0 ? 1 : 0)) * cellSize - p.x) / d.x;
            float ty = sy == 0 ? float.PositiveInfinity : t0 + ((cy + (sy > 0 ? 1 : 0)) * cellSize - p.y) / d.y;
            float tz = sz == 0 ? float.PositiveInfinity : t0 + ((cz + (sz > 0 ? 1 : 0)) * cellSize - p.z) / d.z;
            float dx = sx == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.x);
            float dy = sy == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.y);
            float dz = sz == 0 ? float.PositiveInfinity : cellSize / MathF.Abs(d.z);
            for (int guard = 0; guard < 100000; guard++)
            {
                if (cells.TryGetValue(Key(cx, cy, cz), out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = _table[list[i]];
                        if (e.stamp == stamp) continue;
                        e.stamp = stamp;
                        var c = e.collider;
                        if (!filter.Accepts(c)) continue;
                        if (c.Raycast(ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
                    }
                }
                float tNext = MathF.Min(tx, MathF.Min(ty, tz));
                if (best <= tNext || tNext > t1) break;
                if (tx <= ty && tx <= tz) { cx += sx; tx += dx; }
                else if (ty <= tz) { cy += sy; ty += dy; }
                else { cz += sz; tz += dz; }
            }
            return found;
        }

        /// <summary>Parameter range of the ray inside a box, clipped to [0, maxDistance].</summary>
        private static bool ClipRay(Ray ray, Bounds b, float maxDistance, out float t0, out float t1)
        {
            t0 = 0f; t1 = maxDistance;
            for (int i = 0; i < 3; i++)
            {
                float o = ray.origin[i], d = ray.direction[i];
                if (MathF.Abs(d) < 1E-08f)
                {
                    if (o < b.min[i] || o > b.max[i]) return false;
                    continue;
                }
                float inv = 1f / d;
                float a = (b.min[i] - o) * inv, c = (b.max[i] - o) * inv;
                if (a > c) { float t = a; a = c; c = t; }
                if (a > t0) t0 = a;
                if (c < t1) t1 = c;
                if (t0 > t1) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ narrow phase

        private static readonly ConvexShape _triScratch = ConvexShape.CreateTriangle();

        /// <summary>Sphere and capsule colliders (and capsule characters) are rounded: they never take the plane-based box path.</summary>
        private static bool IsRounded(Collider c) => c is SphereCollider || c is CapsuleCollider || (c is CharacterController cc && cc.controllerShape == ControllerShape.Capsule);

        /// <summary>Axis-aligned box movers against polytopes use the exact plane trace; everything else goes through GJK / conservative advancement.</summary>
        private static bool UseBoxPath(Collider c, in CollisionShape mover) => mover.isAxisAlignedBox && !IsRounded(c);

        /// <summary>Sweeps an axis-aligned box against one collider, lowering <paramref name="hit"/>.fraction when this one is touched first.</summary>
        public static bool SweepAgainst(Collider c, Vector3 center, Vector3 halfExtents, Vector3 motion, float skin, ref ShapeHit hit) =>
            SweepAgainst(c, CollisionShape.AxisAlignedBox(center, halfExtents), motion, skin, ref hit);

        /// <summary>Sweeps any convex shape against one collider (continuous: the exact time of impact, nothing is skipped), lowering <paramref name="hit"/>.fraction when this one is touched first.</summary>
        public static bool SweepAgainst(Collider c, in CollisionShape mover, Vector3 motion, float skin, ref ShapeHit hit)
        {
            if (c is MeshCollider mesh) return SweepMesh(mesh, in mover, motion, skin, ref hit);
            if (UseBoxPath(c, in mover))
            {
                ConvexHull h = c.hull ?? ScratchHull(c.bounds);
                float fraction = hit.fraction;
                if (!h.TraceBox(mover.halfExtents, mover.center, mover.center + motion, skin, ref fraction, out Plane plane, out bool startSolid)) return false;
                hit.collider = c;
                hit.fraction = fraction;
                hit.startSolid = startSolid;
                hit.plane = plane;
                hit.normal = plane.normal;
                hit.point = ContactPoint(mover.center + motion * fraction, mover.halfExtents, plane);
                return true;
            }
            CollisionShape target = c.shape;
            return SweepShapes(c, in mover, motion, in target, skin, ref hit);
        }

        /// <summary>Conservative-advancement sweep of <paramref name="mover"/> against a static shape, recorded into <paramref name="hit"/> when earlier.</summary>
        private static bool SweepShapes(Collider c, in CollisionShape mover, Vector3 motion, in CollisionShape target, float skin, ref ShapeHit hit)
        {
            if (!ConvexDistance.Sweep(in mover, motion, in target, Vector3.zero, hit.fraction, skin, out SweepResult r)) return false;
            if (!r.startSolid && r.fraction >= hit.fraction) return false;
            hit.collider = c;
            hit.fraction = r.fraction;
            hit.startSolid = r.startSolid;
            hit.normal = r.normal;
            hit.point = r.point;
            hit.plane = new Plane(r.normal, Vector3.Dot(r.normal, r.point));
            return true;
        }

        /// <summary>Minimum translation that separates an axis-aligned box from one collider.</summary>
        public static bool Penetration(Collider c, Vector3 center, Vector3 halfExtents, out Vector3 normal, out float depth, float downBias = 1f) =>
            Penetration(c, CollisionShape.AxisAlignedBox(center, halfExtents), out normal, out depth, downBias);

        /// <summary>
        /// Minimum translation that separates a shape from one collider (false when they do not overlap). For a mesh:
        /// the deepest of its overlapping triangles. Deep overlaps (the cores intersect) fall back to the plane-based
        /// push-out of the collider's hull against the mover's bounds, which is conservative but always gets out.
        /// </summary>
        public static bool Penetration(Collider c, in CollisionShape mover, out Vector3 normal, out float depth, float downBias = 1f)
        {
            normal = Vector3.up; depth = 0f;
            if (c is MeshCollider mesh)
            {
                bool any = false;
                _meshTris.Clear();
                mesh.QueryTriangles(mover.bounds, _meshTris);
                for (int i = 0; i < _meshTris.Count; i++)
                {
                    Vector3 n; float d;
                    bool overlap;
                    if (mover.isAxisAlignedBox) overlap = mesh.TriangleHull(_meshTris[i]).BoxPenetration(mover.center, mover.halfExtents, out n, out d, downBias);
                    else overlap = TrianglePenetration(mesh, _meshTris[i], in mover, out n, out d, downBias);
                    if (!overlap) continue;
                    if (!any || d > depth) { normal = n; depth = d; any = true; }
                }
                return any;
            }
            if (UseBoxPath(c, in mover))
            {
                ConvexHull h = c.hull ?? ScratchHull(c.bounds);
                return h.BoxPenetration(mover.center, mover.halfExtents, out normal, out depth, downBias);
            }
            CollisionShape target = c.shape;
            if (ConvexDistance.Distance(in mover, in target, out DistanceResult r))
            {
                if (r.distance >= 0f) return false;
                normal = r.normal; depth = -r.distance;
                return true;
            }
            // deep: the cores intersect
            if (c.hull != null)
            {
                Bounds b = mover.bounds;
                return c.hull.BoxPenetration(b.center, b.extents, out normal, out depth, downBias);
            }
            Vector3 away = mover.center - target.center;
            float dist = away.magnitude;
            normal = dist > 1E-06f ? away / dist : Vector3.up;
            depth = mover.BoundingRadius + target.BoundingRadius - dist;
            return depth > 0f;
        }

        private static bool TrianglePenetration(MeshCollider mesh, int tri, in CollisionShape mover, out Vector3 normal, out float depth, float downBias)
        {
            mesh.GetTriangle(tri, out Vector3 a, out Vector3 b, out Vector3 c);
            _triScratch.SetTriangle(a, b, c);
            CollisionShape target = CollisionShape.Polytope(_triScratch, true);
            if (ConvexDistance.Distance(in mover, in target, out DistanceResult r))
            {
                normal = r.normal; depth = -r.distance;
                return r.distance < 0f;
            }
            // the core crosses the triangle: push out along the triangle plane by the mover's bounds
            Bounds bb = mover.bounds;
            return mesh.TriangleHull(tri).BoxPenetration(bb.center, bb.extents, out normal, out depth, downBias);
        }

        /// <summary>Exact box/collider overlap test.</summary>
        public static bool Overlaps(Collider c, Vector3 center, Vector3 halfExtents) => Overlaps(c, CollisionShape.AxisAlignedBox(center, halfExtents));

        /// <summary>Exact shape/collider overlap test.</summary>
        public static bool Overlaps(Collider c, in CollisionShape mover)
        {
            if (c is MeshCollider mesh)
            {
                _meshTris.Clear();
                mesh.QueryTriangles(mover.bounds, _meshTris);
                for (int i = 0; i < _meshTris.Count; i++)
                {
                    if (mover.isAxisAlignedBox) { if (mesh.TriangleHull(_meshTris[i]).OverlapsBox(mover.center, mover.halfExtents)) return true; }
                    else
                    {
                        mesh.GetTriangle(_meshTris[i], out Vector3 a, out Vector3 b, out Vector3 cc);
                        _triScratch.SetTriangle(a, b, cc);
                        if (ConvexDistance.Overlaps(in mover, CollisionShape.Polytope(_triScratch, true))) return true;
                    }
                }
                return false;
            }
            if (UseBoxPath(c, in mover))
            {
                ConvexHull h = c.hull ?? ScratchHull(c.bounds);
                return h.OverlapsBox(mover.center, mover.halfExtents);
            }
            return ConvexDistance.Overlaps(in mover, c.shape);
        }

        /// <summary>Exact overlap of two colliders (mesh/mesh pairs use their bounds).</summary>
        public static bool CollidersOverlap(Collider a, Collider b)
        {
            if (a is MeshCollider && b is MeshCollider) return a.bounds.Intersects(b.bounds);
            if (a is MeshCollider) return Overlaps(a, b.shape);
            return Overlaps(b, a.shape);
        }

        private static readonly List<int> _meshTris = new List<int>(64);

        /// <summary>Box sweep against the triangles of a mesh near the swept volume (each one a small bevelled hull, or a triangle polytope for rounded / rotated movers).</summary>
        private static bool SweepMesh(MeshCollider mesh, in CollisionShape mover, Vector3 motion, float skin, ref ShapeHit hit)
        {
            _meshTris.Clear();
            mesh.QueryTriangles(mover.bounds.Swept(motion).Expanded(skin + 0.001f), _meshTris);
            bool any = false;
            bool boxPath = mover.isAxisAlignedBox;
            for (int i = 0; i < _meshTris.Count; i++)
            {
                if (boxPath)
                {
                    float fraction = hit.fraction;
                    if (!mesh.TriangleHull(_meshTris[i]).TraceBox(mover.halfExtents, mover.center, mover.center + motion, skin, ref fraction, out Plane plane, out bool startSolid)) continue;
                    hit.collider = mesh;
                    hit.fraction = fraction;
                    hit.startSolid = startSolid;
                    hit.plane = plane;
                    hit.normal = plane.normal;
                    hit.point = ContactPoint(mover.center + motion * fraction, mover.halfExtents, plane);
                    any = true;
                }
                else
                {
                    mesh.GetTriangle(_meshTris[i], out Vector3 a, out Vector3 b, out Vector3 c);
                    _triScratch.SetTriangle(a, b, c);
                    if (SweepShapes(mesh, in mover, motion, CollisionShape.Polytope(_triScratch, true), skin, ref hit)) any = true;
                }
                if (hit.startSolid) break;
            }
            return any;
        }

        /// <summary>Where the box touches the plane: its support point towards the surface, projected onto it.</summary>
        private static Vector3 ContactPoint(Vector3 center, Vector3 e, Plane plane)
        {
            Vector3 n = plane.normal;
            Vector3 p = center - new Vector3(MathF.Sign(n.x) * e.x, MathF.Sign(n.y) * e.y, MathF.Sign(n.z) * e.z);
            return p - n * plane.Distance(p);
        }

        // a reusable six-plane hull for colliders that collide as their bounding box
        private static readonly ConvexHull _scratch = ConvexHull.FromBounds(new Bounds(Vector3.zero, Vector3.zero));

        private static ConvexHull ScratchHull(Bounds b)
        {
            var pl = _scratch.planes;
            pl[0] = new Plane(Vector3.right, b.max.x); pl[1] = new Plane(Vector3.left, -b.min.x);
            pl[2] = new Plane(Vector3.up, b.max.y); pl[3] = new Plane(Vector3.down, -b.min.y);
            pl[4] = new Plane(Vector3.forward, b.max.z); pl[5] = new Plane(Vector3.back, -b.min.z);
            _scratch.bounds = b;
            return _scratch;
        }

        // ------------------------------------------------------------------ shape queries

        /// <summary>Sweeps an axis-aligned box through the world; true when something is touched before the end of the motion.</summary>
        public bool Sweep(Vector3 center, Vector3 halfExtents, Vector3 motion, QueryFilter filter, float skin, out ShapeHit hit) =>
            Sweep(CollisionShape.AxisAlignedBox(center, halfExtents), motion, filter, skin, out hit);

        /// <summary>Sweeps any convex shape through the world (continuous); true when something is touched before the end of the motion.</summary>
        public bool Sweep(in CollisionShape shape, Vector3 motion, QueryFilter filter, float skin, out ShapeHit hit)
        {
            SweepsThisFrame++;
            hit = default; hit.fraction = 1f;
            var candidates = RentList();
            QueryBounds(shape.bounds.Swept(motion).Expanded(skin + 0.001f), filter, candidates);
            foreach (var c in candidates)
            {
                SweepAgainst(c, in shape, motion, skin, ref hit);
                if (hit.startSolid) break;
            }
            ReturnList(candidates);
            return hit.collider != null;
        }

        /// <summary>Sweep restricted to an already gathered candidate list (the solver reuses one list for a whole move).</summary>
        public static bool Sweep(List<Collider> candidates, Vector3 center, Vector3 halfExtents, Vector3 motion, float skin, out ShapeHit hit) =>
            Sweep(candidates, CollisionShape.AxisAlignedBox(center, halfExtents), motion, skin, out hit);

        public static bool Sweep(List<Collider> candidates, in CollisionShape shape, Vector3 motion, float skin, out ShapeHit hit)
        {
            hit = default; hit.fraction = 1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                SweepAgainst(candidates[i], in shape, motion, skin, ref hit);
                if (hit.startSolid) break;
            }
            return hit.collider != null;
        }

        /// <summary>Colliders an axis-aligned box overlaps (exact). <paramref name="results"/> may be null to only count.</summary>
        public int Overlap(Vector3 center, Vector3 halfExtents, QueryFilter filter, List<Collider> results) =>
            Overlap(CollisionShape.AxisAlignedBox(center, halfExtents), filter, results);

        /// <summary>Colliders a shape overlaps (exact). <paramref name="results"/> may be null to only count.</summary>
        public int Overlap(in CollisionShape shape, QueryFilter filter, List<Collider> results)
        {
            var candidates = RentList();
            QueryBounds(shape.bounds, filter, candidates);
            int n = 0;
            foreach (var c in candidates)
            {
                if (!Overlaps(c, in shape)) continue;
                n++;
                if (results != null) results.Add(c); else break;
            }
            ReturnList(candidates);
            return n;
        }

        // ------------------------------------------------------------------ stepping

        public void AddMover(Collider c) { if (c != null && !_movers.Contains(c)) _movers.Add(c); }
        public void RemoveMover(Collider c) => _movers.Remove(c);
        internal void AddBody(KinematicBody b) { if (b != null && !_bodies.Contains(b)) _bodies.Add(b); }
        internal void RemoveBody(KinematicBody b) => _bodies.Remove(b);
        internal void AddRigidBody(SolverBody b) { if (b != null && !_rigidBodies.Contains(b)) _rigidBodies.Add(b); }
        internal void RemoveRigidBody(SolverBody b)
        {
            _rigidBodies.Remove(b);
            _constraints.RemoveAll(c => c.bodyA == b || c.bodyB == b);
        }

        /// <summary>Once per rendered frame: interpolated rigid bodies, per-frame bodies, then the trigger overlap pass for every mover.</summary>
        public void StepFrame(float dt)
        {
            float alpha = Scene.Current != null ? Scene.Current.FixedAlpha : 1f;
            foreach (var rb in _rigidBodies) if (rb.interpolate && rb.IsLive) rb.InterpolateTransform(alpha);
            StepBodies(SimulationMode.PerFrame, dt);
            StepTriggers();
            QueriesLastFrame = QueriesThisFrame + _queriesConcurrent; RaycastsLastFrame = RaycastsThisFrame; SweepsLastFrame = SweepsThisFrame;
            QueriesThisFrame = 0; RaycastsThisFrame = 0; SweepsThisFrame = 0; _queriesConcurrent = 0;
        }

        private readonly List<SolverBody> _rigidScratch = new List<SolverBody>();

        /// <summary>Once per fixed step (after the FixedUpdate callbacks): fixed-step kinematic bodies, then the rigid bodies.</summary>
        public void StepFixed(float dt)
        {
            RefreshDynamics();
            StepBodies(SimulationMode.FixedStep, dt);
            if (dt <= 0f || _rigidBodies.Count == 0) return;
            _rigidScratch.Clear();
            foreach (var b in _rigidBodies) if (b.legacy == null ? b.ecsAlive : !b.legacy.destroyed) _rigidScratch.Add(b);
            try { Rigid.Step(this, _rigidScratch, dt); }
            catch (Exception e) { Debug.LogError("Rigid-body step threw: " + e); }
        }

        private readonly List<KinematicBody> _bodyScratch = new List<KinematicBody>();

        private void StepBodies(SimulationMode mode, float dt)
        {
            if (dt <= 0f || _bodies.Count == 0) return;
            _bodyScratch.Clear();
            _bodyScratch.AddRange(_bodies); // bodies may be destroyed / added from collision callbacks
            foreach (var b in _bodyScratch)
            {
                if (b.destroyed || b.simulation != mode || !b.isActiveAndEnabled) continue;
                try { b.Integrate(dt); }
                catch (Exception e) { Debug.LogError($"KinematicBody on '{b.name}' threw: {e}"); }
            }
        }

        // ---- trigger tracking ----

        private HashSet<(Collider, Collider)> _overlaps = new HashSet<(Collider, Collider)>();
        private HashSet<(Collider, Collider)> _current = new HashSet<(Collider, Collider)>();
        private readonly List<(Collider, Collider)> _changes = new List<(Collider, Collider)>();

        /// <summary>Detects trigger overlaps between movers and trigger colliders and dispatches enter / exit callbacks.</summary>
        private void StepTriggers()
        {
            _current.Clear();
            var candidates = RentList();
            for (int i = 0; i < _movers.Count; i++)
            {
                var mover = _movers[i];
                if (mover.destroyed) { _movers.RemoveAt(i--); continue; }
                if (!mover.IsEnabledForPhysics) continue;
                var filter = new QueryFilter { layerMask = ~0, self = mover, useLayerMatrix = true, includeTriggers = true };
                candidates.Clear();
                QueryBounds(mover.bounds, filter, candidates);
                foreach (var other in candidates)
                {
                    if (!(other.isTrigger || mover.isTrigger)) continue;
                    if (mover.transform.IsChildOf(other.transform)) continue;
                    if (!CollidersOverlap(mover, other)) continue; // exact shapes, not just bounds
                    // one pair per couple, whichever side is the mover (both may be)
                    _current.Add(mover.instanceId < other.instanceId ? (mover, other) : (other, mover));
                }
            }
            ReturnList(candidates);

            _changes.Clear();
            foreach (var pair in _current) if (!_overlaps.Contains(pair)) _changes.Add(pair);
            foreach (var pair in _changes) { Dispatch(pair.Item1, pair.Item2, 0); Dispatch(pair.Item2, pair.Item1, 0); }
            _changes.Clear();
            foreach (var pair in _current) if (_overlaps.Contains(pair)) _changes.Add(pair);
            foreach (var pair in _changes) { Dispatch(pair.Item1, pair.Item2, 1); Dispatch(pair.Item2, pair.Item1, 1); }
            _changes.Clear();
            foreach (var pair in _overlaps) if (!_current.Contains(pair)) _changes.Add(pair);
            foreach (var pair in _changes) { Dispatch(pair.Item1, pair.Item2, 2); Dispatch(pair.Item2, pair.Item1, 2); }
            var t = _overlaps; _overlaps = _current; _current = t;
        }

        /// <summary>phase 0 = enter, 1 = stay, 2 = exit.</summary>
        private static void Dispatch(Collider self, Collider other, int phase)
        {
            if (self.destroyed || other.destroyed) return;
            self.RaiseTrigger(other, phase);
            foreach (var comp in self.gameObject.Components)
            {
                if (comp.destroyed) continue;
                try
                {
                    if (phase == 1) { if (comp is ITriggerStayListener s) s.OnTriggerStay(other); }
                    else if (comp is ITriggerListener l) { if (phase == 0) l.OnTriggerEnter(other); else l.OnTriggerExit(other); }
                }
                catch (Exception e) { Debug.LogError($"OnTrigger handler threw: {e}"); }
            }
        }
    }
}
