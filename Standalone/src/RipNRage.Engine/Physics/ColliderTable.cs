using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs;

namespace RipNRage.Engine
{
    /// <summary>
    /// A collider's slot in the <see cref="ColliderTable"/>: index plus generation, so a stale proxy (its slot freed
    /// and reused) is detectable. Generation 0 is "none". Plain data: ECS components and solver caches can hold it.
    /// </summary>
    public readonly struct PhysicsProxy : IEquatable<PhysicsProxy>
    {
        public readonly int Index;
        public readonly int Generation;
        public PhysicsProxy(int index, int generation) { Index = index; Generation = generation; }
        public bool IsNone => Generation == 0;
        public bool Equals(PhysicsProxy o) => Index == o.Index && Generation == o.Generation;
        public override bool Equals(object o) => o is PhysicsProxy p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Index, Generation);
        public override string ToString() => IsNone ? "Proxy(none)" : $"Proxy({Index}:{Generation})";
    }

    /// <summary>
    /// The one simulation's collider registry: every collider — a legacy <see cref="Collider"/> behaviour today, an
    /// ECS entity from layer 2.4 on — owns a slot here, and the broadphase grids store slot indices. Slots are
    /// recycled through a free list; the generation bumps on free.
    /// </summary>
    public sealed class ColliderTable
    {
        internal sealed class Slot
        {
            public int index, generation = 1;
            /// <summary>Never reused across allocations (contact-cache keys); the slot index is.</summary>
            public int uid;
            /// <summary>The rigid body moving this collider (null: static / kinematic mover / trigger).</summary>
            public SolverBody body;
            // ---- ECS collider state (used when collider == null; the sync system fills it) ----
            /// <summary>Local-space shape description of an ECS collider.</summary>
            public Ecs.Physics.ColliderShape ecsShape;
            public Ecs.Physics.SurfaceMaterial ecsMaterial; public bool hasEcsMaterial;
            public int layer; public bool isTrigger;
            /// <summary>ECS: current world-space shape (sphere / capsule / box; static convex uses <see cref="polytope"/>).</summary>
            public CollisionShape worldShape;
            /// <summary>ECS static box / convex: baked world polytope for the narrow phase.</summary>
            public ConvexShape polytope;
            /// <summary>ECS: current tight world bounds (legacy slots read the collider instead).</summary>
            public Bounds ecsBounds;

            /// <summary>Tight world bounds whoever the owner is.</summary>
            public Bounds CurrentBounds() => collider != null ? collider.bounds : ecsBounds;
            public bool alive;
            /// <summary>Legacy owner (adapter; null once ECS colliders register).</summary>
            public Collider collider;
            /// <summary>ECS owner (Entity.Null for legacy colliders).</summary>
            public Entity entity;
            // ---- broadphase state ----
            public Bounds bounds;       // statics: cached bounds; dynamics: the fat box they are bucketed by
            public Bounds tight;        // dynamics: bounds at the last refresh
            public bool isStatic;
            public bool large;
            public int stamp;           // last query that visited this slot (grid entries appear in several cells)
            public int x0, y0, z0, x1, y1, z1; // dynamics: cell range currently occupied
            public bool bucketed;

            public void ResetBroadphase()
            {
                bounds = tight = default; isStatic = large = bucketed = false; stamp = 0;
                x0 = y0 = z0 = x1 = y1 = z1 = 0;
                ecsShape = default; ecsMaterial = default; hasEcsMaterial = false;
                layer = 0; isTrigger = false; worldShape = default; polytope = null; ecsBounds = default;
            }
        }

        private Slot[] _slots = new Slot[64];
        private int _count;                       // slots ever created (high-water mark)
        private readonly Stack<int> _free = new Stack<int>();
        private static int _nextUid;

        public int Alive { get; private set; }
        public int Capacity => _count;

        /// <summary>Direct slot access by index (hot path: broadphase cell lists hold indices).</summary>
        internal Slot this[int index] => _slots[index];

        internal Slot Allocate(Collider collider, Entity entity)
        {
            int i;
            if (_free.Count > 0) i = _free.Pop();
            else
            {
                if (_count == _slots.Length) Array.Resize(ref _slots, _slots.Length * 2);
                i = _count++;
                _slots[i] = new Slot { index = i };
            }
            var s = _slots[i];
            s.alive = true; s.collider = collider; s.entity = entity; s.body = null;
            s.uid = System.Threading.Interlocked.Increment(ref _nextUid);
            s.ResetBroadphase();
            Alive++;
            return s;
        }

        internal void Free(Slot s)
        {
            if (s == null || !s.alive) return;
            s.alive = false; s.collider = null; s.entity = Entity.Null; s.body = null;
            s.generation++; if (s.generation == 0) s.generation = 1;
            _free.Push(s.index);
            Alive--;
        }

        internal static PhysicsProxy ProxyOf(Slot s) => s == null ? default : new PhysicsProxy(s.index, s.generation);

        /// <summary>The live slot behind a proxy, or null when it is stale / none.</summary>
        internal Slot Resolve(PhysicsProxy p)
        {
            if (p.IsNone || (uint)p.Index >= (uint)_count) return null;
            var s = _slots[p.Index];
            return s.alive && s.generation == p.Generation ? s : null;
        }

        public bool IsAlive(PhysicsProxy p) => Resolve(p) != null;
        /// <summary>The legacy collider behind a proxy (null for ECS colliders or stale proxies).</summary>
        public Collider ColliderOf(PhysicsProxy p) => Resolve(p)?.collider;
        /// <summary>The ECS entity behind a proxy (Entity.Null for legacy colliders or stale proxies).</summary>
        public Entity EntityOf(PhysicsProxy p) => Resolve(p)?.entity ?? Entity.Null;
        /// <summary>The rigid body moving the collider behind a proxy (null: static, no body, or stale).</summary>
        public SolverBody BodyOf(PhysicsProxy p) => Resolve(p)?.body;

    }
}
