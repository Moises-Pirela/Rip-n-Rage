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
        /// <summary>Every collider's slot (one per ECS entity with a ColliderShape).</summary>
        public ColliderTable Table => _table;

        private float _cellSize = 8f;
        private float _dynCellSize = 2f;
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
        /// <summary>Every rigid body in the simulation.</summary>
        public IReadOnlyList<SolverBody> Bodies => _rigidBodies;
        public int RigidBodyCount => _rigidBodies.Count;
        private readonly Stack<List<ColliderTable.Slot>> _listPool = new Stack<List<ColliderTable.Slot>>();

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
        public int ColliderCount => _table.Alive;

        // ------------------------------------------------------------------ registration

        /// <summary>
        /// Registers an ECS collider: the sync system allocates the slot, fills the ECS shape fields, then inserts.
        /// </summary>
        internal ColliderTable.Slot AddEcs(Ecs.Entity entity, bool isStatic)
        {
            var e = _table.Allocate(entity);
            e.isStatic = isStatic;
            return e;
        }

        /// <summary>Puts an ECS slot (shape fields filled) into the broadphase.</summary>
        internal void InsertEcs(ColliderTable.Slot e) { if (e != null && e.alive && !e.inserted) Insert(e); }

        internal void RemoveEcs(ColliderTable.Slot e)
        {
            if (e == null || !e.alive) return;
            Extract(e);
            _table.Free(e);
        }

        /// <summary>Re-inserts an ECS slot after its shape or pose was re-baked (static ECS colliders that moved).</summary>
        internal void RefreshEcs(ColliderTable.Slot e) { if (e == null || !e.alive) return; Extract(e); Insert(e); }

        private void Insert(ColliderTable.Slot e)
        {
            e.inserted = true;
            if (!e.isStatic) { _dynamic.Add(e); BucketDynamic(e, e.worldBounds); return; }
            e.bounds = e.worldBounds;
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
            if (!e.inserted) return;
            e.inserted = false;
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
            for (int i = 0; i < _table.Capacity; i++) { var e = _table[i]; if (e != null && e.alive && e.isStatic) statics.Add(e); }
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

        /// <summary>Re-buckets a dynamic collider whose bounds left its fat box (bodies call this after they move; cheap when they did not).</summary>
        internal void Touch(ColliderTable.Slot e, Bounds tight)
        {
            if (!e.inserted || e.isStatic) return;
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
                var b = e.body;
                if (b == null || b.isKinematic || !e.bucketed) Touch(e, e.worldBounds);
                if (!e.bucketed) continue;
                if (any) union.Encapsulate(e.bounds); else { union = e.bounds; any = true; }
            }
            _dynBounds = union; _hasDynamics = any;
        }

        private void RebuildDynamics()
        {
            foreach (var e in _dynamic) UnbucketDynamic(e);
            _dynCells.Clear(); _largeDynamic.Clear(); _hasDynamics = false;
            foreach (var e in _dynamic) BucketDynamic(e, e.worldBounds);
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF)) | (((long)(y & 0x1FFFFF)) << 21) | (((long)(z & 0x1FFFFF)) << 42);

        // ------------------------------------------------------------------ candidate lists

        internal List<ColliderTable.Slot> RentList() => _listPool.Count > 0 ? _listPool.Pop() : new List<ColliderTable.Slot>(32);
        internal void ReturnList(List<ColliderTable.Slot> list) { list.Clear(); _listPool.Push(list); }

        /// <summary>Broadphase: every collider accepted by the filter whose bounds touch <paramref name="region"/>.</summary>
        internal void QueryBounds(Bounds region, QueryFilter filter, List<ColliderTable.Slot> results)
        {
            QueriesThisFrame++;
            int stamp = ++_stamp;
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
                                    if (e.stamp == stamp) continue;
                                    e.stamp = stamp;
                                    if (!e.bounds.Intersects(region)) continue;
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
                            if (filter.Accepts(e)) results.Add(e);
                        }
                    }
        }

        /// <summary>Broadphase by entity: every collider accepted by the filter whose bounds touch <paramref name="region"/>.</summary>
        public void QueryBounds(Bounds region, QueryFilter filter, List<Ecs.Entity> results)
        {
            var slots = RentList();
            QueryBounds(region, filter, slots);
            foreach (var e in slots) results.Add(e.entity);
            ReturnList(slots);
        }

        /// <summary>
        /// Broadphase for worker threads (the rigid solver's narrow phase), over collider slots so ECS colliders take
        /// part: no visit stamps (duplicates from shapes spanning several cells are removed by scanning the short
        /// result list) and only an interlocked counter. Safe to call concurrently as long as nothing inserts or
        /// moves colliders meanwhile.
        /// </summary>
        internal void QueryBoundsConcurrent(Bounds region, QueryFilter filter, List<ColliderTable.Slot> results)
        {
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
        /// <summary>Concurrent queries are counted per worker by the caller and added here once (no shared counter in the hot loop).</summary>
        internal void AddConcurrentQueries(int n) => _queriesConcurrent += n;

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
                if (!filter.Accepts(e)) continue;
                if (RaycastSlot(e, ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            foreach (var e in _largeStatic)
            {
                if (!filter.Accepts(e)) continue;
                if (RaycastSlot(e, ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
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
                        if (!filter.Accepts(e)) continue;
                        if (RaycastSlot(e, ray, best, out var h) && h.distance < best) { best = h.distance; hit = h; found = true; }
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

        /// <summary>Ray against one collider's world geometry .</summary>
        internal static bool RaycastSlot(ColliderTable.Slot e, Ray ray, float maxDistance, out RaycastHit hit)
        {
            hit = default;
            float d; Vector3 n;
            bool ok;
            if (e.mesh != null) ok = e.mesh.Raycast(ray, maxDistance, out d, out n);
            else switch (e.worldShape.type)
            {
                case ShapeType.Sphere: ok = Physics.RaySphere(ray, e.worldShape.center, e.worldShape.radius, maxDistance, out d, out n); break;
                case ShapeType.Capsule:
                {
                    Vector3 axis = e.worldShape.rotation * Vector3.up * e.worldShape.halfHeight;
                    ok = Physics.RayCapsule(ray, e.worldShape.center - axis, e.worldShape.center + axis, e.worldShape.radius, maxDistance, out d, out n);
                    break;
                }
                default:
                {
                    var h = HullOf(e);
                    ok = h.Raycast(ray, maxDistance, out d, out n);
                    break;
                }
            }
            if (!ok) return false;
            hit = new RaycastHit { entity = e.entity, proxy = ColliderTable.ProxyOf(e), point = ray.GetPoint(d), normal = n, distance = d };
            return true;
        }

        /// <summary>
        /// The plane hull of a box / convex collider. Statics bake theirs at registration; moving boxes build one from
        /// their oriented box and keep it until the box moves.
        /// </summary>
        internal static ConvexHull HullOf(ColliderTable.Slot e)
        {
            if (e.hull != null) return e.hull;
            var s = e.worldShape;
            if (s.type == ShapeType.Box)
            {
                // cached per pose: a kinematic move sweeps the same crate many times per step
                var cached = e.movingHull; var cs = e.movingHullShape;
                if (cached != null && cs.center == s.center && cs.rotation == s.rotation && cs.halfExtents == s.halfExtents) return cached;
                var h = ConvexHull.FromBox(Matrix4x4.TRS(s.center, s.rotation, Vector3.one), Vector3.zero, s.halfExtents * 2f);
                e.movingHullShape = s; e.movingHull = h; // publish after building (main-thread callers; a race only rebuilds)
                return h;
            }
            return ConvexHull.FromBounds(e.worldBounds);
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

        [ThreadStatic] private static ConvexShape _triScratchTs;
        private static ConvexShape TriScratch => _triScratchTs ??= ConvexShape.CreateTriangle();
        [ThreadStatic] private static List<int> _meshTrisTs;
        private static List<int> MeshTris { get { var l = _meshTrisTs ??= new List<int>(64); l.Clear(); return l; } }

        /// <summary>Axis-aligned box movers against polytopes use the exact plane trace; everything else goes through GJK / conservative advancement.</summary>
        private static bool UseBoxPath(ColliderTable.Slot c, in CollisionShape mover) => mover.isAxisAlignedBox && !c.IsRounded;

        /// <summary>Sweeps any convex shape against one collider (continuous), lowering <paramref name="hit"/>.fraction when this one is touched first.</summary>
        internal static bool SweepAgainst(ColliderTable.Slot c, in CollisionShape mover, Vector3 motion, float skin, ref ShapeHit hit)
        {
            if (c.mesh != null) return SweepMesh(c, in mover, motion, skin, ref hit);
            if (UseBoxPath(c, in mover))
            {
                ConvexHull h = HullOf(c);
                float fraction = hit.fraction;
                if (!h.TraceBox(mover.halfExtents, mover.center, mover.center + motion, skin, ref fraction, out Plane plane, out bool startSolid)) return false;
                hit.Set(c);
                hit.fraction = fraction;
                hit.startSolid = startSolid;
                hit.plane = plane;
                hit.normal = plane.normal;
                hit.point = ContactPoint(mover.center + motion * fraction, mover.halfExtents, plane);
                return true;
            }
            CollisionShape target = c.worldShape;
            return SweepShapes(c, in mover, motion, in target, skin, ref hit);
        }

        private static bool SweepShapes(ColliderTable.Slot c, in CollisionShape mover, Vector3 motion, in CollisionShape target, float skin, ref ShapeHit hit)
        {
            if (!ConvexDistance.Sweep(in mover, motion, in target, Vector3.zero, hit.fraction, skin, out SweepResult r)) return false;
            if (!r.startSolid && r.fraction >= hit.fraction) return false;
            hit.Set(c);
            hit.fraction = r.fraction;
            hit.startSolid = r.startSolid;
            hit.normal = r.normal;
            hit.point = r.point;
            hit.plane = new Plane(r.normal, Vector3.Dot(r.normal, r.point));
            return true;
        }

        /// <summary>
        /// Minimum translation that separates a shape from one collider (false when they do not overlap). For a mesh:
        /// the deepest of its overlapping triangles. Deep overlaps (the cores intersect) fall back to the plane-based
        /// push-out of the collider's hull against the mover's bounds, which is conservative but always gets out.
        /// </summary>
        internal static bool Penetration(ColliderTable.Slot c, in CollisionShape mover, out Vector3 normal, out float depth, float downBias = 1f)
        {
            normal = Vector3.up; depth = 0f;
            if (c.mesh != null)
            {
                bool any = false;
                var tris = MeshTris;
                c.mesh.QueryTriangles(mover.bounds, tris);
                for (int i = 0; i < tris.Count; i++)
                {
                    Vector3 n; float d;
                    bool overlap = mover.isAxisAlignedBox
                        ? c.mesh.TriangleHull(tris[i]).BoxPenetration(mover.center, mover.halfExtents, out n, out d, downBias)
                        : TrianglePenetration(c.mesh, tris[i], in mover, out n, out d, downBias);
                    if (!overlap) continue;
                    if (!any || d > depth) { normal = n; depth = d; any = true; }
                }
                return any;
            }
            if (UseBoxPath(c, in mover)) return HullOf(c).BoxPenetration(mover.center, mover.halfExtents, out normal, out depth, downBias);
            CollisionShape target = c.worldShape;
            if (ConvexDistance.Distance(in mover, in target, out DistanceResult r))
            {
                if (r.distance >= 0f) return false;
                normal = r.normal; depth = -r.distance;
                return true;
            }
            if (!c.IsRounded)
            {
                Bounds b = mover.bounds;
                return HullOf(c).BoxPenetration(b.center, b.extents, out normal, out depth, downBias);
            }
            Vector3 away = mover.center - target.center;
            float dist = away.magnitude;
            normal = dist > 1E-06f ? away / dist : Vector3.up;
            depth = mover.BoundingRadius + target.BoundingRadius - dist;
            return depth > 0f;
        }

        private static bool TrianglePenetration(MeshShape mesh, int tri, in CollisionShape mover, out Vector3 normal, out float depth, float downBias)
        {
            mesh.GetTriangle(tri, out Vector3 a, out Vector3 b, out Vector3 c);
            var ts = TriScratch;
            ts.SetTriangle(a, b, c);
            CollisionShape target = CollisionShape.Polytope(ts, true);
            if (ConvexDistance.Distance(in mover, in target, out DistanceResult r))
            {
                normal = r.normal; depth = -r.distance;
                return r.distance < 0f;
            }
            Bounds bb = mover.bounds;
            return mesh.TriangleHull(tri).BoxPenetration(bb.center, bb.extents, out normal, out depth, downBias);
        }

        /// <summary>Exact shape / collider overlap test.</summary>
        internal static bool Overlaps(ColliderTable.Slot c, in CollisionShape mover)
        {
            if (c.mesh != null)
            {
                var tris = MeshTris;
                c.mesh.QueryTriangles(mover.bounds, tris);
                for (int i = 0; i < tris.Count; i++)
                {
                    if (mover.isAxisAlignedBox) { if (c.mesh.TriangleHull(tris[i]).OverlapsBox(mover.center, mover.halfExtents)) return true; }
                    else
                    {
                        c.mesh.GetTriangle(tris[i], out Vector3 a, out Vector3 b, out Vector3 cc);
                        var ts = TriScratch;
                        ts.SetTriangle(a, b, cc);
                        if (ConvexDistance.Overlaps(in mover, CollisionShape.Polytope(ts, true))) return true;
                    }
                }
                return false;
            }
            if (UseBoxPath(c, in mover)) return HullOf(c).OverlapsBox(mover.center, mover.halfExtents);
            return ConvexDistance.Overlaps(in mover, c.worldShape);
        }

        /// <summary>Exact overlap of two colliders (mesh / mesh pairs use their bounds).</summary>
        internal static bool CollidersOverlap(ColliderTable.Slot a, ColliderTable.Slot b)
        {
            if (a.mesh != null && b.mesh != null) return a.worldBounds.Intersects(b.worldBounds);
            if (a.mesh != null) return Overlaps(a, b.worldShape);
            return Overlaps(b, a.worldShape);
        }

        private static bool SweepMesh(ColliderTable.Slot c, in CollisionShape mover, Vector3 motion, float skin, ref ShapeHit hit)
        {
            var mesh = c.mesh;
            var tris = MeshTris;
            mesh.QueryTriangles(mover.bounds.Swept(motion).Expanded(skin + 0.001f), tris);
            bool any = false;
            bool boxPath = mover.isAxisAlignedBox;
            for (int i = 0; i < tris.Count; i++)
            {
                if (boxPath)
                {
                    float fraction = hit.fraction;
                    if (!mesh.TriangleHull(tris[i]).TraceBox(mover.halfExtents, mover.center, mover.center + motion, skin, ref fraction, out Plane plane, out bool startSolid)) continue;
                    hit.Set(c);
                    hit.fraction = fraction;
                    hit.startSolid = startSolid;
                    hit.plane = plane;
                    hit.normal = plane.normal;
                    hit.point = ContactPoint(mover.center + motion * fraction, mover.halfExtents, plane);
                    any = true;
                }
                else
                {
                    mesh.GetTriangle(tris[i], out Vector3 a, out Vector3 b, out Vector3 cc);
                    var ts = TriScratch;
                    ts.SetTriangle(a, b, cc);
                    if (SweepShapes(c, in mover, motion, CollisionShape.Polytope(ts, true), skin, ref hit)) any = true;
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
            return hit.hit;
        }

        /// <summary>Sweep restricted to an already gathered candidate list (the kinematic solver reuses one list for a whole move).</summary>
        internal static bool Sweep(List<ColliderTable.Slot> candidates, in CollisionShape shape, Vector3 motion, float skin, out ShapeHit hit)
        {
            hit = default; hit.fraction = 1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                SweepAgainst(candidates[i], in shape, motion, skin, ref hit);
                if (hit.startSolid) break;
            }
            return hit.hit;
        }

        /// <summary>Entities whose colliders a shape overlaps (exact). <paramref name="results"/> may be null to only count.</summary>
        public int Overlap(in CollisionShape shape, QueryFilter filter, List<Ecs.Entity> results)
        {
            var candidates = RentList();
            QueryBounds(shape.bounds, filter, candidates);
            int n = 0;
            foreach (var c in candidates)
            {
                if (!Overlaps(c, in shape)) continue;
                n++;
                if (results != null) results.Add(c.entity); else break;
            }
            ReturnList(candidates);
            return n;
        }

        // ------------------------------------------------------------------ stepping

        internal void AddRigidBody(SolverBody b) { if (b != null && !_rigidBodies.Contains(b)) _rigidBodies.Add(b); }
        internal void RemoveRigidBody(SolverBody b)
        {
            _rigidBodies.Remove(b);
            _constraints.RemoveAll(c => c.bodyA == b || c.bodyB == b);
        }

        /// <summary>Once per rendered frame: the query counters roll over.</summary>
        public void StepFrame(float dt)
        {
            QueriesLastFrame = QueriesThisFrame + _queriesConcurrent; RaycastsLastFrame = RaycastsThisFrame; SweepsLastFrame = SweepsThisFrame;
            QueriesThisFrame = 0; RaycastsThisFrame = 0; SweepsThisFrame = 0; _queriesConcurrent = 0;
        }

        private readonly List<SolverBody> _rigidScratch = new List<SolverBody>();

        /// <summary>One fixed step: broadphase refresh, then the rigid bodies.</summary>
        public void StepFixed(float dt)
        {
            RefreshDynamics();
            if (dt <= 0f || _rigidBodies.Count == 0) return;
            _rigidScratch.Clear();
            foreach (var b in _rigidBodies) if (b.ecsAlive) _rigidScratch.Add(b);
            try { Rigid.Step(this, _rigidScratch, dt); }
            catch (Exception e) { Debug.LogError("Rigid-body step threw: " + e); }
        }

        /// <summary>Drops every collider and body (a level reset).</summary>
        public void Clear()
        {
            for (int i = 0; i < _table.Capacity; i++) { var e = _table[i]; if (e != null && e.alive) { Extract(e); _table.Free(e); } }
            _rigidBodies.Clear(); _constraints.Clear();
        }
    }
}
