using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// A cached component filter. The table part (All / Any / None over archetype components) is matched once per
    /// archetype — when the archetype is created — so iterating never visits a table that cannot match, and never
    /// tests individual entities for it. Sparse terms are tested per entity with one 256-bit AND.
    /// <code>
    /// var q = world.Query().With&lt;Position&gt;().With&lt;Velocity&gt;().Without&lt;Frozen&gt;().Build();
    /// foreach (var chunk in q)
    /// {
    ///     var pos = chunk.Get&lt;Position&gt;(); var vel = chunk.Get&lt;Velocity&gt;();
    ///     for (int i = 0; i &lt; chunk.Length; i++) if (chunk.Includes(i)) pos[i].Value += vel[i].Value * dt;
    /// }
    /// </code>
    /// </summary>
    public sealed class Query
    {
        private readonly World _world;
        internal Signature TableAll, TableNone, TableAny, SparseAll, SparseNone;
        private readonly List<Archetype> _matched = new List<Archetype>();
        /// <summary>Read and written components, for the scheduler's conflict detection.</summary>
        public Signature Reads, Writes;

        internal Query(World world) { _world = world; }

        public bool HasSparseTerms => !SparseAll.IsEmpty || !SparseNone.IsEmpty;
        public IReadOnlyList<Archetype> MatchedArchetypes => _matched;

        internal void Consider(Archetype a)
        {
            var s = a.Signature;
            if (!s.ContainsAll(TableAll)) return;
            if (s.Intersects(TableNone)) return;
            if (!TableAny.IsEmpty && !s.Intersects(TableAny)) return;
            _matched.Add(a);
        }

        /// <summary>True if this entity's full signature satisfies the sparse terms.</summary>
        internal bool SparseMatch(int entityIndex)
        {
            var sig = _world.RecordSignature(entityIndex);
            return sig.ContainsAll(SparseAll) && !sig.Intersects(SparseNone);
        }

        public bool Matches(Entity e)
        {
            if (!_world.IsAlive(e)) return false;
            var sig = _world.SignatureOf(e);
            return sig.ContainsAll(TableAll | SparseAll) && !sig.Intersects(TableNone | SparseNone)
                && (TableAny.IsEmpty || sig.Intersects(TableAny));
        }

        /// <summary>Entities matching the query (cheap: sums the matched tables, then filters sparse terms if any).</summary>
        public int Count()
        {
            int n = 0;
            foreach (var a in _matched)
            {
                if (!HasSparseTerms) { n += a.Count; continue; }
                for (int i = 0; i < a.Count; i++) if (SparseMatch(a.Entities[i].Index)) n++;
            }
            return n;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
        {
            private readonly Query _q; private int _i;
            internal Enumerator(Query q) { _q = q; _i = -1; }
            public bool MoveNext()
            {
                while (++_i < _q._matched.Count) if (_q._matched[_i].Count > 0) return true;
                return false;
            }
            public Chunk Current => new Chunk(_q, _q._matched[_i], 0, _q._matched[_i].Count);
        }

        /// <summary>
        /// Runs <paramref name="job"/> over every matching row, split into batches across the world's worker threads.
        /// The job may write the components the system declared as written; structural changes must go to
        /// <see cref="SystemContext.CommandsFor"/>(worker), a per-worker buffer played back after the system.
        /// </summary>
        public void ScheduleParallel<TJob>(ref TJob job, int batchSize = 256) where TJob : struct, IChunkJob
        {
            var ranges = _world.BuildRanges(this, batchSize);
            if (ranges.Count == 0) return;
            // inline when there is nothing to split, no workers, or we already run inside a parallel system batch
            // (the job system does not nest)
            if (ranges.Count == 1 || _world.Jobs.ThreadCount <= 1 || _world.InParallelBatch)
            {
                foreach (var r in ranges) job.Execute(new Chunk(this, r.Archetype, r.Start, r.End), 0);
                return;
            }
            var runner = new ChunkJobRunner<TJob> { Job = job, Query = this, Ranges = ranges };
            _world.Jobs.Run(runner, ranges.Count, 1);
            job = runner.Job;
        }
    }

    /// <summary>A contiguous run of rows of one archetype that match a query.</summary>
    public readonly struct Chunk
    {
        private readonly Query _query;
        public readonly Archetype Archetype;
        public readonly int Start, End;
        internal Chunk(Query q, Archetype a, int start, int end) { _query = q; Archetype = a; Start = start; End = end; }

        public int Length => End - Start;
        public ReadOnlySpan<Entity> Entities => new ReadOnlySpan<Entity>(Archetype.Entities, Start, End - Start);

        /// <summary>The column of a table component for these rows (index 0 = row <see cref="Start"/>).</summary>
        public Span<T> Get<T>() where T : struct, IComponent
        {
            if (ComponentType<T>.Sparse) throw new InvalidOperationException($"{typeof(T).Name} is sparse: use world.Get<T>(entity)");
            return new Span<T>(Archetype.Data<T>(ComponentType<T>.Id), Start, End - Start);
        }

        public bool Has<T>() where T : struct, IComponent => Archetype.Signature.Has(ComponentType<T>.Id);

        /// <summary>Row passes the query's sparse terms (always true when it has none).</summary>
        public bool Includes(int i) => !_query.HasSparseTerms || _query.SparseMatch(Archetype.Entities[Start + i].Index);
    }

    /// <summary>Work done per chunk by <see cref="Query.ScheduleParallel{TJob}"/>.</summary>
    public interface IChunkJob
    {
        void Execute(in Chunk chunk, int worker);
    }

    internal struct RowRange { public Archetype Archetype; public int Start, End; }

    internal sealed class ChunkJobRunner<TJob> : IJob where TJob : struct, IChunkJob
    {
        public TJob Job; public Query Query; public List<RowRange> Ranges;
        public void Execute(int stage, int start, int end, int worker)
        {
            for (int i = start; i < end; i++)
            {
                var r = Ranges[i];
                Job.Execute(new Chunk(Query, r.Archetype, r.Start, r.End), worker);
            }
        }
    }

    /// <summary>Fluent query construction; the result is cached on the world and kept current.</summary>
    public struct QueryBuilder
    {
        private readonly World _world;
        private Signature _all, _none, _any, _sAll, _sNone, _reads, _writes;
        internal QueryBuilder(World w) { _world = w; _all = _none = _any = _sAll = _sNone = _reads = _writes = default; }

        /// <summary>Required, read and written.</summary>
        public QueryBuilder With<T>() where T : struct, IComponent { Require<T>(); _writes.Set(ComponentType<T>.Id); return this; }
        /// <summary>Required, only read (lets the scheduler run this system alongside other readers).</summary>
        public QueryBuilder WithReadOnly<T>() where T : struct, IComponent { Require<T>(); _reads.Set(ComponentType<T>.Id); return this; }
        public QueryBuilder Without<T>() where T : struct, IComponent
        {
            if (ComponentType<T>.Sparse) _sNone.Set(ComponentType<T>.Id); else _none.Set(ComponentType<T>.Id);
            return this;
        }
        /// <summary>At least one of the Any components (table components only).</summary>
        public QueryBuilder Any<T>() where T : struct, IComponent
        {
            if (ComponentType<T>.Sparse) throw new InvalidOperationException("Any<> supports table components only");
            _any.Set(ComponentType<T>.Id); _reads.Set(ComponentType<T>.Id); return this;
        }

        private void Require<T>() where T : struct, IComponent
        {
            if (ComponentType<T>.Sparse) _sAll.Set(ComponentType<T>.Id); else _all.Set(ComponentType<T>.Id);
        }

        public Query Build()
        {
            var q = new Query(_world) { TableAll = _all, TableNone = _none, TableAny = _any, SparseAll = _sAll, SparseNone = _sNone, Reads = _reads, Writes = _writes };
            _world.RegisterQuery(q);
            return q;
        }
    }

    public sealed partial class World
    {
        public JobSystem Jobs { get; }
        public QueryBuilder Query() => new QueryBuilder(this);

        internal List<RowRange> BuildRanges(Query q, int batch)
        {
            var list = new List<RowRange>(); // per call: a parallel job may run while another query builds its ranges
            foreach (var a in q.MatchedArchetypes)
                for (int s = 0; s < a.Count; s += batch)
                    list.Add(new RowRange { Archetype = a, Start = s, End = Math.Min(a.Count, s + batch) });
            return list;
        }
    }
}
