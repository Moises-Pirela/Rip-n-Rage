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
    /// The simulation's collider registry: every ECS collider owns a slot here, and the broadphase grids store slot
    /// indices. Slots are recycled through a free list; the generation bumps on free.
    /// </summary>
    public sealed class ColliderTable
    {
        internal sealed class Slot
        {
            public int index, generation = 1;
            /// <summary>Never reused across allocations (contact-cache keys); the slot index is.</summary>
            public int uid;
            public bool alive;
            /// <summary>The ECS entity that owns this collider.</summary>
            public Entity entity;
            /// <summary>The rigid body moving this collider (null: static / trigger).</summary>
            public SolverBody body;
            /// <summary>Local-space shape description (the entity's ColliderShape).</summary>
            public Ecs.Physics.ColliderShape ecsShape;
            public Ecs.Physics.SurfaceMaterial ecsMaterial; public bool hasEcsMaterial;
            public int layer; public bool isTrigger;
            // ---- world geometry (baked by the sync system; dynamic slots follow their body every step) ----
            /// <summary>World-space shape: sphere / capsule / box, or a polytope for convex.</summary>
            public CollisionShape worldShape;
            /// <summary>Box / convex: world polytope for the narrow phase.</summary>
            public ConvexShape polytope;
            /// <summary>Box / convex statics: world hull (planes) for the exact axis-aligned box trace of the kinematic solver.</summary>
            public ConvexHull hull;
            /// <summary>Mesh colliders: world triangles + BVH.</summary>
            public MeshShape mesh;
            /// <summary>Tight world bounds.</summary>
            public Bounds worldBounds;
            // ---- broadphase state ----
            public Bounds bounds;       // statics: cached bounds; dynamics: the fat box they are bucketed by
            public Bounds tight;        // dynamics: bounds at the last refresh
            public bool isStatic;
            public bool large;
            public int stamp;           // last query that visited this slot (grid entries appear in several cells)
            public int x0, y0, z0, x1, y1, z1; // dynamics: cell range currently occupied
            public bool bucketed;
            /// <summary>In the broadphase (Insert ran, Extract did not): Touch on a slot that is not inserted yet must not bucket it.</summary>
            public bool inserted;

            public bool IsRounded => worldShape.type == ShapeType.Sphere || worldShape.type == ShapeType.Capsule;

            public void Reset()
            {
                bounds = tight = default; isStatic = large = bucketed = inserted = false; stamp = 0;
                x0 = y0 = z0 = x1 = y1 = z1 = 0;
                ecsShape = default; ecsMaterial = default; hasEcsMaterial = false;
                layer = 0; isTrigger = false; worldShape = default; polytope = null; hull = null; mesh = null; worldBounds = default;
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

        internal Slot Allocate(Entity entity)
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
            s.alive = true; s.entity = entity; s.body = null;
            s.uid = System.Threading.Interlocked.Increment(ref _nextUid);
            s.Reset();
            Alive++;
            return s;
        }

        internal void Free(Slot s)
        {
            if (s == null || !s.alive) return;
            s.alive = false; s.entity = Entity.Null; s.body = null; s.mesh = null; s.polytope = null; s.hull = null;
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
        /// <summary>The ECS entity behind a proxy (Entity.Null for stale proxies).</summary>
        public Entity EntityOf(PhysicsProxy p) => Resolve(p)?.entity ?? Entity.Null;
        /// <summary>The rigid body moving the collider behind a proxy (null: static, no body, or stale).</summary>
        public SolverBody BodyOf(PhysicsProxy p) => Resolve(p)?.body;

    }
}
