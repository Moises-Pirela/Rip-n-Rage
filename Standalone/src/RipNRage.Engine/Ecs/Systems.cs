using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace RipNRage.Engine.Ecs
{
    // ---------------------------------------------------------------- events a system can subscribe to

    /// <summary>Marker for all systems. Implement any of the event interfaces below to subscribe.</summary>
    public interface ISystem { }
    public interface IOnSimStart : ISystem { void OnSimStart(SystemContext ctx); }
    public interface IOnSimEnd : ISystem { void OnSimEnd(SystemContext ctx); }
    public interface ITick : ISystem { void Tick(SystemContext ctx); }
    public interface IFixedTick : ISystem { void FixedTick(SystemContext ctx); }
    /// <summary>End of frame, after every Tick and FixedTick.</summary>
    public interface ILateTick : ISystem { void LateTick(SystemContext ctx); }
    /// <summary>Fired at the sync point after the entity was created, with its initial components in place.</summary>
    public interface IOnEntityCreated : ISystem { void OnEntityCreated(SystemContext ctx, Entity entity); }
    /// <summary>Fired at the sync point before the entity is removed: its components are still readable.</summary>
    public interface IOnEntityDestroyed : ISystem { void OnEntityDestroyed(SystemContext ctx, Entity entity); }

    /// <summary>A gameplay event (damage, death, pickup...) posted during the frame and consumed at its end.</summary>
    public interface IPostProcessEvent { }
    /// <summary>Receives every <typeparamref name="T"/> posted this frame, at end of frame, before the list is cleared.</summary>
    public interface IConsumes<T> : ISystem where T : struct, IPostProcessEvent { void Consume(SystemContext ctx, ReadOnlySpan<T> events); }

    /// <summary>Optional base class: keeps the world and runs <see cref="OnCreate"/> once when the system is added.</summary>
    public abstract class SystemBase : ISystem
    {
        public World World { get; internal set; }
        /// <summary>Build queries here.</summary>
        public virtual void OnCreate() { }
    }

    // ---------------------------------------------------------------- attributes

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class UpdateAfterAttribute : Attribute { public readonly Type System; public UpdateAfterAttribute(Type t) => System = t; }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class UpdateBeforeAttribute : Attribute { public readonly Type System; public UpdateBeforeAttribute(Type t) => System = t; }
    /// <summary>Components this system reads. Systems whose reads/writes do not conflict share a parallel batch.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReadsAttribute : Attribute { public readonly Type[] Types; public ReadsAttribute(params Type[] t) => Types = t; }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WritesAttribute : Attribute { public readonly Type[] Types; public WritesAttribute(params Type[] t) => Types = t; }
    /// <summary>Lifecycle events are delivered only for entities that have all of these components.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EntityFilterAttribute : Attribute { public readonly Type[] Types; public EntityFilterAttribute(params Type[] t) => Types = t; }
    /// <summary>Not picked up by <see cref="World.AddSystemsFromAssemblies"/>.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisableAutoCreationAttribute : Attribute { }

    // ---------------------------------------------------------------- per-call context

    /// <summary>What a system gets each call: the world, timing, and the command buffer it must use for structural changes.</summary>
    public sealed class SystemContext
    {
        public World World { get; internal set; }
        public float DeltaTime { get; internal set; }
        public double Time { get; internal set; }
        public long Frame { get; internal set; }
        /// <summary>This system's command buffer (played back at the sync point after its batch, in system order).</summary>
        public CommandBuffer Commands { get; internal set; }
        internal CommandBuffer[] Workers;
        /// <summary>Per-worker buffer for parallel chunk jobs (<c>worker</c> as passed to <see cref="IChunkJob.Execute"/>).</summary>
        public CommandBuffer CommandsFor(int worker) => worker == 0 ? Commands : Workers[worker];
        public void Post<T>(in T e) where T : struct, IPostProcessEvent => World.Post(e);
    }

    // ---------------------------------------------------------------- scheduling

    internal sealed class SystemEntry
    {
        public ISystem System;
        public Type Type;
        public Signature Reads, Writes;
        public bool Exclusive;            // declared nothing: conflicts with everything
        public Signature Filter;          // lifecycle-event filter
        public SystemContext Context;
        public string ProfileName;
    }

    /// <summary>One phase (Tick, FixedTick...): systems in dependency order, packed into conflict-free batches.</summary>
    internal sealed class Phase
    {
        public readonly string Name;
        public readonly List<SystemEntry> Ordered = new List<SystemEntry>();
        public readonly List<List<SystemEntry>> Batches = new List<List<SystemEntry>>();
        public Phase(string name) { Name = name; }
    }

    public sealed partial class World
    {
        private readonly List<SystemEntry> _systems = new List<SystemEntry>();
        private readonly Phase _simStart = new Phase("OnSimStart"), _simEnd = new Phase("OnSimEnd"),
            _tick = new Phase("Tick"), _fixed = new Phase("FixedTick"), _late = new Phase("LateTick");
        private readonly List<SystemEntry> _onCreated = new List<SystemEntry>(), _onDestroyed = new List<SystemEntry>();
        private bool _scheduleDirty;
        internal bool InParallelBatch;

        public float FixedDeltaTime = 1f / 60f;
        public int MaxFixedSteps = 8;
        /// <summary>
        /// How far real time is past the last fixed step, as a fraction of a step (0..1): what interpolation blends by.
        /// From the internal accumulator, or set by the host that drives the fixed steps (the Application copies the
        /// scene's value in every frame).
        /// </summary>
        public float FixedAlpha
        {
            get => DriveFixedInternally ? (FixedDeltaTime > 0f ? Math.Clamp((float)(_fixedAccumulator / FixedDeltaTime), 0f, 1f) : 1f) : _externalAlpha;
            set => _externalAlpha = Math.Clamp(value, 0f, 1f);
        }
        private float _externalAlpha = 1f;

        /// <summary>False when the host drives fixed steps itself through <see cref="RunFixed"/> (the Application's scene loop): Update then skips its own accumulator.</summary>
        public bool DriveFixedInternally = true;
        private double _fixedAccumulator, _time;
        private long _frame;
        public bool SimRunning { get; private set; }
        public IEnumerable<ISystem> Systems => _systems.Select(s => s.System);

        /// <summary>Creates every non-abstract <see cref="ISystem"/> with a parameterless constructor (unless [DisableAutoCreation]).</summary>
        public void AddSystemsFromAssemblies(params Assembly[] assemblies)
        {
            foreach (var asm in assemblies)
                foreach (var t in asm.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    if (!t.IsClass || t.IsAbstract || !typeof(ISystem).IsAssignableFrom(t)) continue;
                    if (t.GetCustomAttribute<DisableAutoCreationAttribute>() != null) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    if (_systems.Any(s => s.Type == t)) continue;
                    AddSystem((ISystem)Activator.CreateInstance(t));
                }
        }

        public T AddSystem<T>(T system) where T : ISystem
        {
            var t = system.GetType();
            var e = new SystemEntry { System = system, Type = t, ProfileName = "ecs: " + t.Name };
            var r = t.GetCustomAttribute<ReadsAttribute>(); var w = t.GetCustomAttribute<WritesAttribute>();
            if (r == null && w == null) e.Exclusive = true;
            if (r != null) foreach (var c in r.Types) e.Reads.Set(ComponentRegistry.Get(c).Id);
            if (w != null) foreach (var c in w.Types) e.Writes.Set(ComponentRegistry.Get(c).Id);
            var f = t.GetCustomAttribute<EntityFilterAttribute>();
            if (f != null) foreach (var c in f.Types) e.Filter.Set(ComponentRegistry.Get(c).Id);
            e.Context = new SystemContext { World = this, Commands = new CommandBuffer(this), Workers = new CommandBuffer[Math.Max(1, Jobs.ThreadCount + 1)] };
            for (int i = 1; i < e.Context.Workers.Length; i++) e.Context.Workers[i] = new CommandBuffer(this);
            _systems.Add(e);
            if (system is SystemBase sb) { sb.World = this; sb.OnCreate(); }
            _scheduleDirty = true;
            return system;
        }

        /// <summary>Diagnostics: the batches of a phase ("Tick", "FixedTick"...); systems in one batch run in parallel.</summary>
        public List<List<Type>> DescribeBatches(string phase)
        {
            if (_scheduleDirty) RebuildSchedule();
            var p = new[] { _simStart, _simEnd, _tick, _fixed, _late }.First(x => x.Name == phase);
            return p.Batches.Select(b => b.Select(s => s.Type).ToList()).ToList();
        }

        public T GetSystem<T>() where T : class, ISystem => _systems.Select(s => s.System).OfType<T>().FirstOrDefault();

        /// <summary>The created instance of a system type, or null (the type-driven form, for tooling and tests).</summary>
        public ISystem GetSystem(Type type) => _systems.FirstOrDefault(s => s.Type == type)?.System;

        // ---------------------------------------------------------------- frame

        public void StartSim()
        {
            if (SimRunning) return;
            SimRunning = true;
            RunPhase(_simStart, 0f, (s, c) => ((IOnSimStart)s).OnSimStart(c));
        }

        public void EndSim()
        {
            if (!SimRunning) return;
            RunPhase(_simEnd, 0f, (s, c) => ((IOnSimEnd)s).OnSimEnd(c));
            SimRunning = false;
        }

        /// <summary>One frame: Tick, the fixed steps that fit (FixedTick), LateTick, then the post-process events.</summary>
        public void Update(float dt)
        {
            if (!SimRunning) StartSim();
            _frame++;
            Sync(); // structural changes made outside systems since last frame
            RunPhase(_tick, dt, (s, c) => ((ITick)s).Tick(c));
            if (DriveFixedInternally)
            {
                _fixedAccumulator += dt;
                int steps = 0;
                while (_fixedAccumulator >= FixedDeltaTime && steps < MaxFixedSteps)
                {
                    RunPhase(_fixed, FixedDeltaTime, (s, c) => ((IFixedTick)s).FixedTick(c));
                    _fixedAccumulator -= FixedDeltaTime; steps++;
                }
                if (steps == MaxFixedSteps) _fixedAccumulator = 0; // falling behind: drop the backlog instead of spiralling
            }
            RunPhase(_late, dt, (s, c) => ((ILateTick)s).LateTick(c));
            DispatchPostProcessEvents(dt);
            _time += dt;
        }

        /// <summary>One externally driven fixed step (the scene's fixed loop calls this so legacy FixedUpdate and FixedTick share one clock).</summary>
        public void RunFixed(float dt)
        {
            if (!SimRunning) StartSim();
            RunPhase(_fixed, dt, (s, c) => ((IFixedTick)s).FixedTick(c));
        }

        private void RebuildSchedule()
        {
            _scheduleDirty = false;
            Build(_simStart, typeof(IOnSimStart)); Build(_simEnd, typeof(IOnSimEnd));
            Build(_tick, typeof(ITick)); Build(_fixed, typeof(IFixedTick)); Build(_late, typeof(ILateTick));
            _onCreated.Clear(); _onCreated.AddRange(TopoSort(_systems.Where(s => s.System is IOnEntityCreated).ToList()));
            _onDestroyed.Clear(); _onDestroyed.AddRange(TopoSort(_systems.Where(s => s.System is IOnEntityDestroyed).ToList()));
            BuildConsumers();
        }

        private void Build(Phase p, Type iface)
        {
            p.Ordered.Clear(); p.Batches.Clear();
            p.Ordered.AddRange(TopoSort(_systems.Where(s => iface.IsInstanceOfType(s.System)).ToList()));
            // level scheduling: in dependency order, each system goes to the batch right after the latest earlier
            // system it conflicts with or must follow. Order between conflicting systems is preserved (the earlier
            // one in topological order always runs in an earlier batch); everything else packs as early as it can.
            var level = new Dictionary<SystemEntry, int>();
            for (int i = 0; i < p.Ordered.Count; i++)
            {
                var s = p.Ordered[i];
                int lv = 0;
                for (int j = 0; j < i; j++)
                {
                    var o = p.Ordered[j];
                    if (Conflicts(o, s) || MustFollow(s, o)) lv = Math.Max(lv, level[o] + 1);
                }
                level[s] = lv;
                while (p.Batches.Count <= lv) p.Batches.Add(new List<SystemEntry>());
                p.Batches[lv].Add(s);
            }
        }

        private static bool Conflicts(SystemEntry a, SystemEntry b)
        {
            if (a.Exclusive || b.Exclusive) return true;
            return a.Writes.Intersects(b.Writes | b.Reads) || b.Writes.Intersects(a.Reads);
        }

        private static bool MustFollow(SystemEntry s, SystemEntry o) =>
            s.Type.GetCustomAttributes<UpdateAfterAttribute>().Any(a => a.System == o.Type)
            || o.Type.GetCustomAttributes<UpdateBeforeAttribute>().Any(a => a.System == s.Type);

        /// <summary>Kahn's algorithm over [UpdateAfter]/[UpdateBefore]; ties broken by type name for a stable order.</summary>
        private static List<SystemEntry> TopoSort(List<SystemEntry> items)
        {
            var byType = items.ToDictionary(i => i.Type);
            var after = items.ToDictionary(i => i, _ => new HashSet<SystemEntry>()); // i runs after these
            foreach (var i in items)
            {
                foreach (var a in i.Type.GetCustomAttributes<UpdateAfterAttribute>()) if (byType.TryGetValue(a.System, out var o)) after[i].Add(o);
                foreach (var b in i.Type.GetCustomAttributes<UpdateBeforeAttribute>()) if (byType.TryGetValue(b.System, out var o)) after[o].Add(i);
            }
            var result = new List<SystemEntry>();
            var ready = new SortedSet<SystemEntry>(Comparer<SystemEntry>.Create((x, y) => string.CompareOrdinal(x.Type.FullName, y.Type.FullName)));
            foreach (var i in items) if (after[i].Count == 0) ready.Add(i);
            while (ready.Count > 0)
            {
                var n = ready.Min; ready.Remove(n); result.Add(n);
                foreach (var i in items)
                    if (after[i].Remove(n) && after[i].Count == 0) ready.Add(i);
            }
            if (result.Count != items.Count)
                throw new InvalidOperationException("system ordering cycle: " + string.Join(", ", items.Except(result).Select(i => i.Type.Name)));
            return result;
        }

        private sealed class BatchJob : IJob
        {
            public List<SystemEntry> Batch; public Action<ISystem, SystemContext> Call; public Exception Error;
            public void Execute(int stage, int start, int end, int worker)
            {
                for (int i = start; i < end; i++)
                {
                    try { Call(Batch[i].System, Batch[i].Context); }
                    catch (Exception e) { Interlocked.CompareExchange(ref Error, e, null); }
                }
            }
        }
        private readonly BatchJob _batchJob = new BatchJob();

        private void RunPhase(Phase p, float dt, Action<ISystem, SystemContext> call)
        {
            if (_scheduleDirty) RebuildSchedule();
            foreach (var batch in p.Batches)
            {
                foreach (var s in batch) { s.Context.DeltaTime = dt; s.Context.Time = _time; s.Context.Frame = _frame; }
                BeginIteration();
                try
                {
                    if (batch.Count == 1 || Jobs.ThreadCount <= 1)
                        foreach (var s in batch)
                        {
                            Profiler.Begin(s.ProfileName);
                            try { call(s.System, s.Context); } finally { Profiler.End(); }
                        }
                    else
                    {
                        // independent systems run concurrently; their command buffers keep the outcome deterministic
                        InParallelBatch = true;
                        _batchJob.Batch = batch; _batchJob.Call = call; _batchJob.Error = null;
                        Jobs.Run(_batchJob, batch.Count, 1);
                        InParallelBatch = false;
                        if (_batchJob.Error != null) throw new AggregateException($"system failed in {p.Name}", _batchJob.Error);
                    }
                }
                finally { EndIteration(); InParallelBatch = false; }
                foreach (var s in batch) PlaybackSystem(s);
                Sync();
            }
        }

        private void PlaybackSystem(SystemEntry s)
        {
            s.Context.Commands.Playback();
            for (int i = 1; i < s.Context.Workers.Length; i++) s.Context.Workers[i].Playback();
        }

        // ---------------------------------------------------------------- lifecycle events (sync points)

        private readonly List<Entity> _pendingCreated = new List<Entity>(), _pendingDestroyed = new List<Entity>();
        internal void QueueCreated(Entity e) => _pendingCreated.Add(e);
        internal void QueueDestroyed(Entity e) { if (IsAlive(e) && !_pendingDestroyed.Contains(e)) _pendingDestroyed.Add(e); }

        /// <summary>
        /// Applies the world's own command buffer and delivers the lifecycle events. Handlers may queue more
        /// changes; the loop runs until nothing is pending (bounded, to catch create/destroy feedback loops).
        /// </summary>
        public void Sync()
        {
            if (IsIterating) throw new InvalidOperationException("Sync while systems run");
            if (_scheduleDirty) RebuildSchedule();
            for (int round = 0; round < 64; round++)
            {
                Commands.Playback();
                if (_pendingCreated.Count == 0 && _pendingDestroyed.Count == 0) return;

                if (_pendingCreated.Count > 0)
                {
                    var created = _pendingCreated.ToArray(); _pendingCreated.Clear();
                    foreach (var s in _onCreated)
                        foreach (var e in created)
                            if (IsAlive(e) && SignatureOf(e).ContainsAll(s.Filter)) Deliver(s, c => ((IOnEntityCreated)s.System).OnEntityCreated(c, e));
                }
                if (_pendingDestroyed.Count > 0)
                {
                    var destroyed = _pendingDestroyed.ToArray(); _pendingDestroyed.Clear();
                    foreach (var s in _onDestroyed)
                        foreach (var e in destroyed)
                            if (IsAlive(e) && SignatureOf(e).ContainsAll(s.Filter)) Deliver(s, c => ((IOnEntityDestroyed)s.System).OnEntityDestroyed(c, e));
                    foreach (var e in destroyed) DestroyNow(e);
                }
            }
            throw new InvalidOperationException("lifecycle events did not settle after 64 rounds (a handler keeps creating / destroying)");
        }

        private void Deliver(SystemEntry s, Action<SystemContext> call)
        {
            BeginIteration();
            try { call(s.Context); } finally { EndIteration(); }
            PlaybackSystem(s);
        }
    }
}
