using System;
using System.Diagnostics;
using System.Threading;

namespace RipNRage.Engine
{
    /// <summary>Work the <see cref="JobSystem"/> hands out: items <c>[start, end)</c> of one stage, on worker thread <paramref name="worker"/> (0 = the caller).</summary>
    public interface IJob
    {
        void Execute(int stage, int start, int end, int worker);
    }

    /// <summary>
    /// A small job system for the physics step: persistent worker threads that spin while a step is running and
    /// block between steps, and <b>multi-stage dispatches</b> — one dispatch carries a sequence of stages (say:
    /// integrate the bodies, then warm-start colour 0, colour 1, …, then solve colour 0, …); every worker pulls
    /// chunks of the current stage from an atomic counter and, when the stage is exhausted, waits for the stragglers
    /// at an atomic barrier before moving to the next stage. Nothing returns to the calling thread between stages,
    /// no thread is woken per stage and nothing is allocated per stage, which is what makes the ~150 sync points
    /// of a multithreaded Gauss-Seidel step affordable (a <c>Parallel.For</c> per colour cost more in thread-pool
    /// latency than the colour's work). The calling thread is worker 0 and works too.
    /// </summary>
    public sealed class JobSystem : IDisposable
    {
        private sealed class Dispatch
        {
            public IJob job;
            public int stages;
            public int[] counts, batches, next, pending;
            public Exception error;
        }

        private readonly Thread[] _threads;
        private readonly ManualResetEventSlim _go = new ManualResetEventSlim(false);
        private volatile Dispatch _current;
        private volatile bool _shutdown;
        /// <summary>Microseconds a worker keeps spinning for the next dispatch before it blocks (a step issues dispatches back to back).</summary>
        public int SpinMicros = 400;

        public int ThreadCount { get; }
        public int Dispatches { get; private set; }
        public int Stages { get; private set; }

        public JobSystem(int threads)
        {
            ThreadCount = Math.Max(1, threads);
            _threads = new Thread[ThreadCount - 1];
            for (int i = 0; i < _threads.Length; i++)
            {
                int worker = i + 1;
                _threads[i] = new Thread(() => WorkerLoop(worker)) { IsBackground = true, Name = "Physics worker " + worker };
                _threads[i].Start();
            }
        }

        // ------------------------------------------------------------------ dispatching

        /// <summary>One stage of <paramref name="count"/> items in chunks of <paramref name="batch"/>; returns when every item ran.</summary>
        public void Run(IJob job, int count, int batch)
        {
            _stageCounts[0] = count; _stageBatches[0] = batch;
            Run(job, _stageCounts, _stageBatches, 1);
        }

        private readonly int[] _stageCounts = new int[1], _stageBatches = new int[1];

        /// <summary>
        /// Runs <paramref name="stages"/> stages in order: stage s has <c>counts[s]</c> items handed out in chunks of
        /// <c>batches[s]</c>; no item of stage s+1 starts before every item of stage s has finished. A stage with a
        /// single item of batch 1 is a sequential step inside the dispatch.
        /// </summary>
        public void Run(IJob job, int[] counts, int[] batches, int stages)
        {
            if (stages <= 0) return;
            if (ThreadCount == 1 || _shutdown)
            {
                for (int s = 0; s < stages; s++) if (counts[s] > 0) job.Execute(s, 0, counts[s], 0);
                Dispatches++; Stages += stages;
                return;
            }
            // a fresh record per dispatch (a few hundred bytes): a worker that lags behind can never see a reused one
            // next / pending are hammered by every worker with interlocked ops: one cache line per stage (Pad ints),
            // or the stages' counters share a line and every claim ping-pongs it between all cores
            var d = new Dispatch { job = job, stages = stages, counts = new int[stages], batches = new int[stages], next = new int[stages * Pad + Pad], pending = new int[stages * Pad + Pad] };
            for (int s = 0; s < stages; s++) { d.counts[s] = counts[s]; d.batches[s] = Math.Max(1, batches[s]); d.pending[(s + 1) * Pad] = counts[s]; }
            Dispatches++; Stages += stages;
            Volatile.Write(ref _current, d);
            if (!_go.IsSet) _go.Set();
            Work(d, 0);
            if (d.error != null) Debug.LogError("Physics job threw: " + d.error);
        }

        /// <summary>The step is over: workers may block until the next dispatch.</summary>
        public void Idle()
        {
            _go.Reset();
        }

        /// <summary>Runs every stage of a dispatch to completion (called by the dispatcher and by each worker).</summary>
        /// <summary>Ints per cache line (64 B): stage s's counters live at index (s + 1) * Pad.</summary>
        private const int Pad = 16;

        private static void Work(Dispatch d, int worker)
        {
            for (int s = 0; s < d.stages; s++)
            {
                int count = d.counts[s], batch = d.batches[s];
                while (true)
                {
                    int start = Interlocked.Add(ref d.next[(s + 1) * Pad], batch) - batch;
                    if (start >= count) break;
                    int end = Math.Min(start + batch, count);
                    try { d.job.Execute(s, start, end, worker); }
                    catch (Exception e) { Interlocked.CompareExchange(ref d.error, e, null); }
                    Interlocked.Add(ref d.pending[(s + 1) * Pad], -(end - start));
                }
                // barrier: the stage is complete when every handed-out chunk has run
                var sw = new SpinWait();
                while (Volatile.Read(ref d.pending[(s + 1) * Pad]) > 0) sw.SpinOnce(-1);
            }
        }

        private void WorkerLoop(int worker)
        {
            Dispatch last = null;
            long spinTicks = Stopwatch.Frequency * SpinMicros / 1_000_000L;
            while (!_shutdown)
            {
                var d = _current;
                if (d != null && d != last)
                {
                    last = d;
                    Work(d, worker);
                    continue;
                }
                // nothing new: stay hot for a moment (the step issues its dispatches back to back), then block
                long until = Stopwatch.GetTimestamp() + spinTicks;
                bool found = false;
                while (Stopwatch.GetTimestamp() < until)
                {
                    Thread.SpinWait(40);
                    if (_current != last) { found = true; break; }
                }
                if (found) continue;
                _go.Wait();
            }
        }

        public void Dispose()
        {
            _shutdown = true;
            _go.Set();
        }

        // ------------------------------------------------------------------ shared instance

        private static JobSystem _shared;

        /// <summary>
        /// Size of the process-wide pool (main thread included): one per core, at most 16. The ECS scheduler and the
        /// rigid solver share it: two pools (they used to be 23 + 16 spinning threads on 24 cores) starved each
        /// other's workers, and a worker descheduled while holding a chunk stalls every stage barrier.
        /// </summary>
        public static int DefaultThreads => Math.Clamp(Environment.ProcessorCount, 1, 16);

        /// <summary>The engine-wide job system with <paramref name="threads"/> threads (recreated when the count changes).</summary>
        public static JobSystem Shared(int threads)
        {
            threads = Math.Max(1, threads);
            if (_shared == null || _shared.ThreadCount != threads || _shared._shutdown)
            {
                _shared?.Dispose();
                _shared = new JobSystem(threads);
            }
            return _shared;
        }
    }
}
