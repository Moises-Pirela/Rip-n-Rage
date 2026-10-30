using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine
{
    /// <summary>
    /// Frame profiler: real wall-clock frame time (independent of <c>--fixed-dt</c>, which only fakes the
    /// simulation delta), a 240-frame history with average / worst / 1 % low, per-stage CPU time and — when the
    /// driver has GL timer queries — per-stage GPU time, the time spent blocked in the buffer swap (vsync / GPU
    /// back-pressure), GC collections per second, managed allocations per frame and the heap size.
    ///
    /// Stages are begun / ended by the engine around every step of the frame (<see cref="Begin"/> /
    /// <see cref="End"/>, nestable; GPU timing only for the outermost GPU stage because GL disallows nested
    /// TIME_ELAPSED queries). <see cref="SyncGpu"/> (RIPNRAGE_PROFILE=1 or --profile) adds a glFinish at the end of
    /// every GPU stage so the CPU numbers include the GPU work — the only reliable breakdown on drivers whose
    /// timer queries are meaningless (software renderers).
    /// </summary>
    public static class Profiler
    {
        public sealed class Stage
        {
            public string name;
            public int depth;
            public bool gpu;
            /// <summary>CPU milliseconds in the last frame / averaged over the last window / worst in the window.</summary>
            public double cpuMs, cpuAvg, cpuMax;
            /// <summary>GPU milliseconds (timer query) for the last frame with a result / window average.</summary>
            public double gpuMs, gpuAvg;
            public bool hasGpu;
            public int order;
            internal long cpuStart;
            internal double cpuSum, gpuSum; internal int cpuSamples, gpuSamples; internal double windowMax;
            internal uint[] queries; internal bool[] pending; internal bool queryOpen;
            internal int lastFrameSeen;
        }

        /// <summary>CPU stage timing (always cheap; two Stopwatch reads per stage).</summary>
        public static bool Enabled = true;
        /// <summary>Use GL timer queries for the GPU side when the driver has them.</summary>
        public static bool GpuTiming = true;
        /// <summary>glFinish after each GPU stage: CPU times then include the GPU work (accurate but serialises CPU and GPU).</summary>
        public static bool SyncGpu = Environment.GetEnvironmentVariable("RIPNRAGE_PROFILE") == "1";
        /// <summary>Frames kept for the graph / percentiles.</summary>
        public const int HistoryLength = 240;
        /// <summary>Seconds over which averages / maxima are accumulated before being published.</summary>
        public static double WindowSeconds = 0.5;

        // ---- published frame statistics
        /// <summary>Wall-clock duration of the last frame (ms), measured from loop start to loop start.</summary>
        public static double FrameMs { get; private set; }
        public static double FrameMsAvg { get; private set; }
        public static double FrameMsMax { get; private set; }
        /// <summary>99th percentile frame time over the history (the "1 % low" of fps counters).</summary>
        public static double FrameMsP99 { get; private set; }
        public static double Fps => FrameMsAvg > 0.0 ? 1000.0 / FrameMsAvg : 0.0;
        public static double FpsLow1 => FrameMsP99 > 0.0 ? 1000.0 / FrameMsP99 : 0.0;
        /// <summary>GPU time of the last frame from start-of-render to end-of-render timestamps (ms; 0 when unavailable).</summary>
        public static double GpuFrameMs { get; private set; }
        public static double GpuFrameMsAvg { get; private set; }
        /// <summary>Milliseconds the CPU spent blocked in SwapBuffers last window (vsync wait or the GPU running behind).</summary>
        public static double PresentMsAvg { get; private set; }
        /// <summary>The simulation delta the game is fed (seconds); differs from the real frame time under --fixed-dt or timeScale.</summary>
        public static float SimulationDelta { get; set; }
        public static bool SimulationDeltaFixed { get; set; }
        public static readonly float[] History = new float[HistoryLength];
        public static int HistoryIndex { get; private set; }
        public static long FrameCount { get; private set; }
        public static bool GpuTimersAvailable { get; private set; }

        // ---- memory
        public static long AllocBytesPerFrame { get; private set; }
        public static float Gen0PerSecond { get; private set; }
        public static float Gen1PerSecond { get; private set; }
        public static float Gen2PerSecond { get; private set; }
        public static long HeapBytes { get; private set; }

        private static readonly Stopwatch _sw = Stopwatch.StartNew();
        private static readonly Dictionary<string, Stage> _byName = new Dictionary<string, Stage>();
        private static readonly List<Stage> _stages = new List<Stage>();
        private static readonly Stack<Stage> _open = new Stack<Stage>();
        public static IReadOnlyList<Stage> Stages => _stages;

        private static long _frameStartTicks, _lastFrameStartTicks;
        private static double _windowStart;
        private static int _windowFrames;
        private static double _windowMax, _frameSum, _gpuSum;
        private static int _gpuSamples;
        private static bool _gpuQueryActive;
        private static int _gen0, _gen1, _gen2;
        private static long _allocAtFrameStart, _allocSum;
        private static bool _gpuChecked;
        private static readonly uint[] _tsStart = new uint[4], _tsEnd = new uint[4];
        private static readonly bool[] _tsPending = new bool[4];
        private static int _tsSlot;

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        // ------------------------------------------------------------------ frame

        /// <summary>Call at the very start of the main loop iteration.</summary>
        public static void BeginFrame()
        {
            long now = _sw.ElapsedTicks;
            if (_lastFrameStartTicks != 0)
            {
                FrameMs = Ms(now - _lastFrameStartTicks);
                History[HistoryIndex] = (float)FrameMs;
                HistoryIndex = (HistoryIndex + 1) % HistoryLength;
                _frameSum += FrameMs; _windowFrames++;
                if (FrameMs > _windowMax) _windowMax = FrameMs;
            }
            _lastFrameStartTicks = now;
            _frameStartTicks = now;
            FrameCount++;
            foreach (var s in _stages) { s.cpuMs = 0.0; s.lastFrameSeen = -1; }
            _open.Clear();
            _gpuQueryActive = false;
            _allocAtFrameStart = GC.GetAllocatedBytesForCurrentThread();
            if (!_gpuChecked) { _gpuChecked = true; GpuTimersAvailable = GpuTiming && GL.HasTimerQueries; }
        }

        /// <summary>Marks the start of GPU work for the frame (a timestamp query); pairs with <see cref="GpuFrameEnd"/>.</summary>
        public static void GpuFrameBegin()
        {
            if (!GpuTimersAvailable) return;
            if (_tsStart[_tsSlot] == 0) { _tsStart[_tsSlot] = GL.GenQuery(); _tsEnd[_tsSlot] = GL.GenQuery(); }
            GL.QueryCounter(_tsStart[_tsSlot], GL.TIMESTAMP);
        }

        public static void GpuFrameEnd()
        {
            if (!GpuTimersAvailable) return;
            GL.QueryCounter(_tsEnd[_tsSlot], GL.TIMESTAMP);
            _tsPending[_tsSlot] = true;
            _tsSlot = (_tsSlot + 1) % 4;
            // read the slot that is three frames old
            int old = _tsSlot;
            if (_tsPending[old] && GL.QueryResultAvailable(_tsEnd[old]))
            {
                ulong a = GL.QueryResult64(_tsStart[old]), b = GL.QueryResult64(_tsEnd[old]);
                GpuFrameMs = b > a ? (b - a) / 1e6 : 0.0;
                _gpuSum += GpuFrameMs; _gpuSamples++;
                _tsPending[old] = false;
            }
        }

        /// <summary>Call after SwapBuffers: publishes window statistics, reads back GPU queries, samples the GC.</summary>
        public static void EndFrame()
        {
            long allocNow = GC.GetAllocatedBytesForCurrentThread();
            _allocSum += Math.Max(0, allocNow - _allocAtFrameStart);
            if (GpuTimersAvailable) foreach (var s in _stages) ReadGpu(s);

            double now = _sw.Elapsed.TotalSeconds;
            if (now - _windowStart < WindowSeconds || _windowFrames == 0) return;
            double span = now - _windowStart;
            _windowStart = now;

            FrameMsAvg = _frameSum / _windowFrames;
            FrameMsMax = _windowMax;
            FrameMsP99 = Percentile(0.99f);
            GpuFrameMsAvg = _gpuSamples > 0 ? _gpuSum / _gpuSamples : 0.0;
            AllocBytesPerFrame = _allocSum / _windowFrames;
            foreach (var s in _stages)
            {
                s.cpuAvg = s.cpuSamples > 0 ? s.cpuSum / s.cpuSamples : 0.0;
                s.cpuMax = s.windowMax;
                s.gpuAvg = s.gpuSamples > 0 ? s.gpuSum / s.gpuSamples : 0.0;
                s.cpuSum = 0.0; s.cpuSamples = 0; s.windowMax = 0.0; s.gpuSum = 0.0; s.gpuSamples = 0;
            }
            PresentMsAvg = _byName.TryGetValue("present", out var pr) ? pr.cpuAvg : 0.0;

            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            Gen0PerSecond = (float)((g0 - _gen0) / span); Gen1PerSecond = (float)((g1 - _gen1) / span); Gen2PerSecond = (float)((g2 - _gen2) / span);
            _gen0 = g0; _gen1 = g1; _gen2 = g2;
            HeapBytes = GC.GetTotalMemory(false);

            _frameSum = 0.0; _windowFrames = 0; _windowMax = 0.0; _gpuSum = 0.0; _gpuSamples = 0; _allocSum = 0;
        }

        private static readonly float[] _sorted = new float[HistoryLength];
        private static float Percentile(float p)
        {
            // the ring fills sequentially from index 0, so the first n entries are the valid ones until it wraps
            int n = (int)Math.Min(FrameCount - 1, HistoryLength);
            if (n <= 0) return 0f;
            Array.Copy(History, _sorted, n);
            Array.Sort(_sorted, 0, n);
            return _sorted[Math.Clamp((int)(p * (n - 1)), 0, n - 1)];
        }

        // ------------------------------------------------------------------ stages

        /// <summary>Starts a stage; <paramref name="gpu"/> stages also get a GL timer query (outermost only).</summary>
        public static void Begin(string name, bool gpu = false)
        {
            if (!Enabled) return;
            if (!_byName.TryGetValue(name, out var s))
            {
                s = new Stage { name = name, order = _stages.Count };
                _byName[name] = s; _stages.Add(s);
            }
            s.depth = _open.Count;
            s.gpu |= gpu;
            s.cpuStart = _sw.ElapsedTicks;
            s.lastFrameSeen = (int)(FrameCount & 0x7fffffff);
            _open.Push(s);
            if (gpu && GpuTimersAvailable && !_gpuQueryActive)
            {
                s.queries ??= new uint[4]; s.pending ??= new bool[4];
                int slot = (int)(FrameCount % 4);
                if (s.queries[slot] == 0) s.queries[slot] = GL.GenQuery();
                if (!s.pending[slot]) { GL.BeginQuery(GL.TIME_ELAPSED, s.queries[slot]); s.queryOpen = true; _gpuQueryActive = true; }
            }
        }

        /// <summary>Ends the innermost open stage.</summary>
        public static void End()
        {
            if (!Enabled || _open.Count == 0) return;
            var s = _open.Pop();
            if (s.queryOpen)
            {
                GL.EndQuery(GL.TIME_ELAPSED);
                s.pending[(int)(FrameCount % 4)] = true;
                s.queryOpen = false; _gpuQueryActive = false;
            }
            if (s.gpu && SyncGpu) GL.Finish();
            double ms = Ms(_sw.ElapsedTicks - s.cpuStart);
            s.cpuMs += ms;
            s.cpuSum += ms; s.cpuSamples++;
            if (s.cpuMs > s.windowMax) s.windowMax = s.cpuMs;
        }

        private static void ReadGpu(Stage s)
        {
            if (s.queries == null) return;
            int slot = (int)((FrameCount + 1) % 4); // the oldest slot (3 frames back)
            if (!s.pending[slot]) return;
            if (!GL.QueryResultAvailable(s.queries[slot])) return;
            ulong ns = GL.QueryResult64(s.queries[slot]);
            s.pending[slot] = false;
            s.gpuMs = ns / 1e6; s.hasGpu = true;
            s.gpuSum += s.gpuMs; s.gpuSamples++;
        }

        // ------------------------------------------------------------------ reporting

        /// <summary>One-line summary for logs.</summary>
        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.Append($"frame {FrameMsAvg:0.0} ms avg / {FrameMsP99:0.0} p99 / {FrameMsMax:0.0} max ({Fps:0.0} fps, 1% low {FpsLow1:0.0})");
            if (GpuFrameMsAvg > 0.0) sb.Append($"  gpu {GpuFrameMsAvg:0.0} ms");
            sb.Append($"  present {PresentMsAvg:0.0} ms");
            if (SimulationDeltaFixed) sb.Append($"  [sim dt fixed {SimulationDelta * 1000f:0.0} ms]");
            sb.Append($"  gc {Gen0PerSecond:0.0}/{Gen1PerSecond:0.0}/{Gen2PerSecond:0.0} /s  alloc {AllocBytesPerFrame / 1024.0:0.0} KB/frame  heap {HeapBytes / (1024.0 * 1024.0):0.0} MB");
            sb.Append(" |");
            foreach (var s in _stages)
            {
                if (s.cpuAvg < 0.05 && s.gpuAvg < 0.05) continue;
                sb.Append(' ').Append(s.name).Append('=').Append(s.cpuAvg.ToString("0.0"));
                if (s.hasGpu) sb.Append('/').Append(s.gpuAvg.ToString("0.0")).Append("g");
            }
            return sb.ToString();
        }

        /// <summary>CSV header / row for per-frame logging (--perf-csv).</summary>
        public static string CsvHeader()
        {
            var sb = new StringBuilder("frame,frame_ms,gpu_ms,alloc_bytes");
            foreach (var s in _stages) { sb.Append(',').Append(s.name.Replace(' ', '_')).Append("_cpu_ms"); if (s.gpu) sb.Append(',').Append(s.name.Replace(' ', '_')).Append("_gpu_ms"); }
            return sb.ToString();
        }

        public static string CsvRow()
        {
            var sb = new StringBuilder();
            sb.Append(FrameCount).Append(',').Append(FrameMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(GpuFrameMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocAtFrameStart));
            foreach (var s in _stages)
            {
                sb.Append(',').Append(s.cpuMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                if (s.gpu) sb.Append(',').Append(s.gpuMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
