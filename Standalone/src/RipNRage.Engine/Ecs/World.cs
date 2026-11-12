using System;
using System.Collections.Generic;
using System.Threading;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// Owns the entities, their storage (archetype tables + sparse sets), the queries, the systems and the event
    /// queues.
    /// <para>
    /// Structural changes (create / destroy / add / remove) made while systems run go through a
    /// <see cref="CommandBuffer"/> and are applied at the next sync point, where the lifecycle events
    /// (OnEntityCreated / OnEntityDestroyed) fire. Calling the immediate structural API while a phase is running
    /// throws, so no query ever sees a table change under it.
    /// </para>
    /// </summary>
    public sealed partial class World : IDisposable
    {
        private struct Record
        {
            public Archetype Archetype;
            public int Row;
            public int Generation;
            public Signature Signature;   // archetype bits + sparse bits
            public bool Alive;
            public bool Reserved;         // handed out by a command buffer, not yet created
        }

        private Record[] _records = new Record[256];
        private int _recordCount;
        private readonly Stack<int> _free = new Stack<int>();
        private readonly object _reserveLock = new object();

        private readonly Dictionary<Signature, Archetype> _archetypes = new Dictionary<Signature, Archetype>();
        private readonly List<Archetype> _archetypeList = new List<Archetype>();
        private readonly SparseSetBase[] _sparse = new SparseSetBase[Signature.Capacity];
        private readonly List<Query> _queries = new List<Query>();
        public readonly Archetype Empty;

        /// <summary>The buffer for structural changes made outside system code, or by systems in serial batches.</summary>
        public CommandBuffer Commands { get; }
        public int EntityCount { get; private set; }
        public IReadOnlyList<Archetype> Archetypes => _archetypeList;

        /// <summary>Non-zero while a phase runs: the immediate structural API is disabled.</summary>
        private int _iterating;
        internal bool IsIterating => _iterating > 0;

        public World(int workerThreads = -1)
        {
            ComponentRegistry.Scan();
            Empty = GetArchetype(default);
            Commands = new CommandBuffer(this);
            // default: the process-wide pool the rigid solver uses too (never nested: physics steps inside an
            // exclusive system on the main thread). An explicit count gets a private pool (tests).
            if (workerThreads >= 0) { Jobs = new JobSystem(workerThreads); _ownsJobs = true; }
            else Jobs = JobSystem.Shared(JobSystem.DefaultThreads);
        }

        // ------------------------------------------------------------------ entities

        /// <summary>Reserves an entity handle (thread-safe). It becomes alive when its command buffer plays back.</summary>
        internal Entity Reserve()
        {
            lock (_reserveLock)
            {
                int index;
                if (_free.Count > 0) index = _free.Pop();
                else
                {
                    if (_recordCount == _records.Length) Array.Resize(ref _records, _records.Length * 2);
                    index = _recordCount++;
                }
                ref var r = ref _records[index];
                r.Generation = r.Generation == 0 ? 1 : r.Generation; // 0 is the null handle
                r.Reserved = true;
                return new Entity(index, r.Generation);
            }
        }

        /// <summary>Creates an entity immediately (not allowed while a phase runs: use <see cref="Commands"/>).</summary>
        public Entity CreateEntity()
        {
            AssertNotIterating();
            var e = Reserve();
            Materialize(e);
            _pendingCreated.Add(e);
            return e;
        }

        internal void Materialize(Entity e)
        {
            ref var r = ref _records[e.Index];
            r.Reserved = false; r.Alive = true;
            r.Archetype = Empty; r.Row = Empty.AddRow(e); r.Signature = default;
            EntityCount++;
        }

        public bool IsAlive(Entity e) => e.Index < _recordCount && _records[e.Index].Alive && _records[e.Index].Generation == e.Generation;
        public Signature SignatureOf(Entity e) { Check(e); return _records[e.Index].Signature; }

        /// <summary>Destroys immediately (OnEntityDestroyed fires first). Not while a phase runs: use <see cref="Commands"/>.</summary>
        public void DestroyEntity(Entity e)
        {
            AssertNotIterating();
            QueueDestroyed(e);
            Sync();
        }

        internal void DestroyNow(Entity e)
        {
            if (!IsAlive(e)) return;
            ref var r = ref _records[e.Index];
            var moved = r.Archetype.RemoveRow(r.Row);
            if (!moved.IsNull) _records[moved.Index].Row = r.Row;
            for (int id = 0; id < Signature.Capacity && !r.Signature.IsEmpty; id++)
                if (r.Signature.Has(id) && _sparse[id] != null) _sparse[id].Remove(e.Index);
            r.Alive = false; r.Archetype = null; r.Signature = default;
            r.Generation = r.Generation == int.MaxValue ? 1 : r.Generation + 1; // stale handles stop matching
            lock (_reserveLock) _free.Push(e.Index);
            EntityCount--;
        }

        /// <summary>Destroys every entity (level restart). OnEntityDestroyed fires for each.</summary>
        public void DestroyAllEntities()
        {
            AssertNotIterating();
            for (int i = 0; i < _recordCount; i++)
                if (_records[i].Alive) QueueDestroyed(new Entity(i, _records[i].Generation));
            Sync();

            // The carrier entities died with the rest. Claimed singletons are re-created here, at once and with their
            // values back at default, rather than left to the next read: a writer system claims its singleton in
            // OnCreate, which does not run again after a reset, so the next read would come from inside a running
            // phase (EcsInputSystem.Tick) and creating a carrier there is a structural change — it threw.
            var claimed = _singletons.Count == 0 ? null : new List<int>(_singletons.Keys);
            _singletons.Clear();
            if (claimed == null) return;
            foreach (int id in claimed)
            {
                var e = CreateEntity();
                _singletonClaims[id](this, e);
                _singletons[id] = e;
            }
        }

        // ------------------------------------------------------------------ components

        public bool Has<T>(Entity e) where T : struct, IComponent => IsAlive(e) && _records[e.Index].Signature.Has(ComponentType<T>.Id);

        /// <summary>Reference to a component (archetype column or sparse slot). Valid until the next structural change.</summary>
        public ref T Get<T>(Entity e) where T : struct, IComponent
        {
            Check(e);
            int id = ComponentType<T>.Id;
            if (ComponentType<T>.Sparse) return ref Sparse<T>().Get(e.Index);
            ref var r = ref _records[e.Index];
            return ref r.Archetype.Data<T>(id)[r.Row];
        }

        public bool TryGet<T>(Entity e, out T value) where T : struct, IComponent
        {
            if (Has<T>(e)) { value = Get<T>(e); return true; }
            value = default; return false;
        }

        // ------------------------------------------------------------------ singletons

        /// <summary>
        /// World-wide state held as a component on one hidden entity: the ECS equivalent of a static.
        /// <see cref="Singleton{T}"/> returns it by reference so a writer system can assign fields in place;
        /// readers that only want a snapshot use <see cref="GetSingleton{T}"/>, which never creates anything.
        ///
        /// <para>Creating the carrier is a structural change, so a system that writes a singleton must claim it in
        /// <see cref="SystemBase.OnCreate"/> (which runs outside a phase) — calling <see cref="Singleton{T}"/> for a
        /// type nobody has claimed yet from inside a running phase throws, like any other structural change.</para>
        /// </summary>
        public ref T Singleton<T>() where T : struct, IComponent
        {
            Entity e = SingletonEntity<T>();
            return ref Get<T>(e);
        }

        /// <summary>The singleton's current value, or <c>default</c> if nothing has created it. Never structural.</summary>
        public T GetSingleton<T>() where T : struct, IComponent =>
            TryFindSingleton<T>(out var e) ? Get<T>(e) : default;

        public bool HasSingleton<T>() where T : struct, IComponent => TryFindSingleton<T>(out _);

        private bool TryFindSingleton<T>(out Entity e) where T : struct, IComponent
        {
            if (_singletons.TryGetValue(ComponentType<T>.Id, out e) && IsAlive(e) && Has<T>(e)) return true;
            e = Entity.Null;
            return false;
        }

        /// <summary>The entity carrying <typeparamref name="T"/>; created (with a default value) if there is none.</summary>
        public Entity SingletonEntity<T>() where T : struct, IComponent
        {
            if (TryFindSingleton<T>(out var e)) return e;
            e = CreateEntity();
            AddNow(e, default(T));
            _singletons[ComponentType<T>.Id] = e;
            // remembered so DestroyAllEntities can put the carrier back without knowing the type
            _singletonClaims[ComponentType<T>.Id] = static (w, carrier) => w.AddNow(carrier, default(T));
            return e;
        }

        private readonly Dictionary<int, Entity> _singletons = new Dictionary<int, Entity>();
        private readonly Dictionary<int, Action<World, Entity>> _singletonClaims = new Dictionary<int, Action<World, Entity>>();

        /// <summary>Adds or overwrites a component immediately (not while a phase runs).</summary>
        public void Add<T>(Entity e, in T value) where T : struct, IComponent { AssertNotIterating(); AddNow(e, value); }
        public void Remove<T>(Entity e) where T : struct, IComponent { AssertNotIterating(); RemoveNow<T>(e); }

        internal void AddNow<T>(Entity e, in T value) where T : struct, IComponent
        {
            Check(e);
            int id = ComponentType<T>.Id;
            ref var r = ref _records[e.Index];
            if (ComponentType<T>.Sparse)
            {
                Sparse<T>().Set(e.Index, value);
                r.Signature.Set(id);
                return;
            }
            if (!r.Archetype.Signature.Has(id))
            {
                var dst = r.Archetype.AddEdge.TryGetValue(id, out var cached) ? cached : r.Archetype.AddEdge[id] = GetArchetype(r.Archetype.Signature | Signature.Of(id));
                MoveTo(e, ref r, dst);
                r.Signature.Set(id);
            }
            r.Archetype.Data<T>(id)[r.Row] = value;
        }

        internal void RemoveNow<T>(Entity e) where T : struct, IComponent
        {
            if (!IsAlive(e)) return;
            int id = ComponentType<T>.Id;
            ref var r = ref _records[e.Index];
            if (!r.Signature.Has(id)) return;
            if (ComponentType<T>.Sparse) { Sparse<T>().Remove(e.Index); r.Signature.Clear(id); return; }
            var sig = r.Archetype.Signature; sig.Clear(id);
            var dst = r.Archetype.RemoveEdge.TryGetValue(id, out var cached) ? cached : r.Archetype.RemoveEdge[id] = GetArchetype(sig);
            MoveTo(e, ref r, dst);
            r.Signature.Clear(id);
        }

        /// <summary>Copies the shared columns into the destination table and swap-removes the source row.</summary>
        private void MoveTo(Entity e, ref Record r, Archetype dst)
        {
            var src = r.Archetype;
            int newRow = dst.AddRow(e);
            for (int i = 0; i < src.ComponentIds.Length; i++)
            {
                int dc = dst.ColumnOf(src.ComponentIds[i]);
                if (dc >= 0) src.Columns[i].CopyRowTo(r.Row, dst.Columns[dc], newRow);
            }
            var moved = src.RemoveRow(r.Row);
            if (!moved.IsNull) _records[moved.Index].Row = r.Row;
            r.Archetype = dst; r.Row = newRow;
        }

        internal SparseSet<T> Sparse<T>() where T : struct, IComponent
        {
            int id = ComponentType<T>.Id;
            return (SparseSet<T>)(_sparse[id] ??= new SparseSet<T>());
        }

        internal bool SparseHas(int componentId, int entityIndex) => _records[entityIndex].Signature.Has(componentId);
        internal Signature RecordSignature(int entityIndex) => _records[entityIndex].Signature;

        private Archetype GetArchetype(Signature sig)
        {
            if (_archetypes.TryGetValue(sig, out var a)) return a;
            var ids = new List<int>();
            for (int i = 0; i < Signature.Capacity; i++) if (sig.Has(i)) ids.Add(i);
            a = new Archetype(sig, ids.ToArray());
            _archetypes[sig] = a; _archetypeList.Add(a);
            foreach (var q in _queries) q.Consider(a); // queries keep their matching-table list current
            return a;
        }

        internal void RegisterQuery(Query q)
        {
            _queries.Add(q);
            foreach (var a in _archetypeList) q.Consider(a);
        }

        private void Check(Entity e)
        {
            if (!IsAlive(e)) throw new InvalidOperationException($"{e} is not alive");
        }

        private void AssertNotIterating()
        {
            if (_iterating > 0) throw new InvalidOperationException("structural change while systems run: use a CommandBuffer (world.Commands / context.Commands)");
        }

        internal void BeginIteration() => Interlocked.Increment(ref _iterating);
        internal void EndIteration() => Interlocked.Decrement(ref _iterating);

        private readonly bool _ownsJobs;
        public void Dispose() { if (_ownsJobs) Jobs.Dispose(); }
    }
}
