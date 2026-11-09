using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;

namespace RipNRage.Benchmark
{
    /// <summary>A benchmark scenario: builds its world under a root object, gets ticked every frame, reports a line of live stats.</summary>
    public abstract class Scenario
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        /// <summary>The count the scenario is about (for the report).</summary>
        public abstract int Target(BenchmarkConfig cfg);
        public abstract void Build(GameObject root, BenchmarkConfig cfg);
        public virtual void Tick(float dt, float elapsed) { }
        /// <summary>Live numbers appended to the per-frame sample (entities alive etc.).</summary>
        public virtual int Alive => 0;
        public virtual string Extra => "";
        public virtual void Teardown() { }
    }

    /// <summary>One frame's measurements.</summary>
    public struct Sample
    {
        public float time;
        /// <summary>physicsMs = every physics stage of the frame (fixed rigid steps + the per-frame mover / trigger pass); stepMs = the cost of one fixed step plus the per-frame pass.</summary>
        public double frameMs, physicsMs, stepMs, sceneMs, renderMs;
        public int fixedSteps, rigidBodies, awake, manifolds, contacts, joints, kinematic, movers, queries, sweeps, raycasts, ccd, alive, islands, largestIsland, threads, drawCalls, dispatches, stages;
        /// <summary>Rigid step breakdown (ms for the whole frame, all fixed steps).</summary>
        public double beginMs, narrowMs, prepareMs, islandsMs, solveMs, ccdMs, finishMs;
        public long allocBytes;
    }

    /// <summary>Aggregated result of one scenario.</summary>
    public sealed class ScenarioResult
    {
        public string name, description;
        public int target;
        public int frames;
        public double frameAvg, frameP99, frameMax, physicsAvg, stepAvg, stepP99, stepMax, sceneAvg, renderAvg, stepsAvg;
        public double fps => frameAvg > 0 ? 1000.0 / frameAvg : 0;
        public int bodiesAvg, awakeAvg, manifoldsAvg, contactsAvg, jointsAvg, kinematicAvg, aliveAvg, aliveMin, queriesAvg, sweepsAvg, raycastsAvg, islandsAvg, largestIslandAvg, threads, drawCallsAvg, dispatchesAvg, stagesAvg;
        /// <summary>Per fixed step.</summary>
        public double beginAvg, narrowAvg, prepareAvg, islandsTimeAvg, solveAvg, ccdAvg, finishAvg;
        public double allocKbAvg;
        public bool framePass, physicsPass;
        public bool Pass => framePass && physicsPass;
        public readonly List<Sample> samples = new List<Sample>();
    }

    /// <summary>
    /// Runs the scenarios back to back: build → warm-up → measure → tear down, sampling every frame at the
    /// profiler's end-of-frame hook (so the stage times of that very frame are read), then prints / writes the report.
    /// </summary>
    public sealed class BenchmarkRunner : Behaviour
    {
        public BenchmarkConfig config;
        public List<Scenario> scenarios = new List<Scenario>();
        public bool Failed { get; private set; }

        private int _index = -1;
        private Scenario _current;
        private GameObject _root;
        private ScenarioResult _result;
        private float _elapsed;
        private bool _measuring;
        private readonly List<ScenarioResult> _results = new List<ScenarioResult>();
        private StreamWriter _csv;
        private Camera _camera;
        private float _hudTimer;
        private bool _done;

        protected override void Awake()
        {
            Profiler.FrameEnded += OnFrameEnded;
            if (config.csvPath != null)
            {
                try
                {
                    _csv = new StreamWriter(config.csvPath, false);
                    _csv.WriteLine("scenario,time,frame_ms,physics_ms,physics_step_ms,fixed_steps,scene_ms,render_ms,rigid_bodies,awake,manifolds,contacts,joints,kinematic,movers,queries,sweeps,raycasts,ccd,alive,islands,largest_island,threads,alloc_kb");
                }
                catch (Exception e) { Debug.LogError("csv: " + e.Message); _csv = null; }
            }
            var camGo = new GameObject("BenchmarkCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.fieldOfView = 60f; _camera.nearClipPlane = 0.1f; _camera.farClipPlane = 600f;
            Camera.main = _camera;
            RenderSettings.drawSky = true;
            Next();
        }

        protected override void OnDestroy()
        {
            Profiler.FrameEnded -= OnFrameEnded;
            _csv?.Dispose();
        }

        private void Next()
        {
            if (_current != null) Teardown();
            _index++;
            if (_index >= scenarios.Count) { Finish(); return; }
            _current = scenarios[_index];
            _root = new GameObject("Scenario_" + _current.Name);
            _result = new ScenarioResult { name = _current.Name, description = _current.Description, target = _current.Target(config) };
            _elapsed = 0f; _measuring = false;
            BenchmarkScene.PlaceCamera(_camera, _current.Name);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _current.Build(_root, config);
            Debug.LogWarning($"[bench] {_current.Name}: built in {sw.Elapsed.TotalMilliseconds:0} ms — {_current.Description} (target {_result.target}); warm-up {config.warmup:0.#} s, measuring {config.duration:0.#} s");
        }

        private void Teardown()
        {
            _current.Teardown();
            BenchmarkScene.DestroyEntities();
            if (_root != null) GameObject.DestroyImmediate(_root);
            _root = null; _current = null;
            Physics.World.Rigid.Contacts.Clear();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }

        protected override void Update()
        {
            if (_done || _current == null) return;
            float dt = Time.deltaTime;
            _elapsed += dt;
            _current.Tick(dt, _elapsed);
            if (!_measuring && _elapsed >= config.warmup) { _measuring = true; Debug.LogWarning($"[bench] {_current.Name}: measuring"); }
            if (_elapsed >= config.warmup + config.duration) { Summarise(); Next(); return; }
            _hudTimer -= dt;
            if (_hudTimer <= 0f)
            {
                _hudTimer = 1f;
                var w = Physics.World;
                int steps = Math.Max(1, Scene.Current != null ? Scene.Current.FixedStepsLastFrame : 1);
                Debug.LogWarning($"[bench] {_current.Name} t={_elapsed:0.0}s {(_measuring ? "MEASURE" : "warmup")}  frame {Profiler.FrameMsAvg:0.0} ms  physics {Profiler.StageMs("fixed physics") / steps + Profiler.StageMs("physics"):0.0} ms/step ({steps} steps)  bodies {w.RigidBodyCount} ({w.Rigid.AwakeBodies} awake)  islands {w.Rigid.IslandCount} (max {w.Rigid.LargestIsland})  threads {w.Rigid.ThreadsUsed}  manifolds {w.Rigid.ManifoldCount}  contacts {w.Rigid.ContactCount}  joints {w.Rigid.JointCount}  movers {w.DynamicCount}  alive {_current.Alive} {_current.Extra}");
            }
        }

        private void OnFrameEnded()
        {
            if (!_measuring || _current == null || _result == null) return;
            var w = Physics.World;
            int steps = Scene.Current != null ? Scene.Current.FixedStepsLastFrame : 0;
            double fixedMs = Profiler.StageMs("fixed physics"), frameMs = Profiler.StageMs("physics");
            var s = new Sample
            {
                time = _elapsed,
                frameMs = Profiler.FrameElapsedMs,
                physicsMs = fixedMs + frameMs, stepMs = fixedMs / Math.Max(1, steps) + frameMs, fixedSteps = steps,
                sceneMs = Profiler.StageMs("scene"), renderMs = Profiler.StageMs("render"),
                rigidBodies = w.RigidBodyCount, awake = w.Rigid.AwakeBodies, manifolds = w.Rigid.ManifoldCount, contacts = w.Rigid.ContactCount, joints = w.Rigid.JointCount,
                kinematic = 0, movers = w.DynamicCount, queries = w.QueriesLastFrame, sweeps = w.SweepsLastFrame, raycasts = w.RaycastsLastFrame, ccd = w.Rigid.CcdSweepsThisStep,
                alive = _current.Alive, islands = w.Rigid.IslandCount, largestIsland = w.Rigid.LargestIsland, threads = w.Rigid.ThreadsUsed, allocBytes = Profiler.AllocBytesThisFrame,
                drawCalls = Application.Current != null ? Application.Current.Renderer.DrawCalls : 0,
                dispatches = w.Rigid.Dispatches, stages = w.Rigid.StageCount,
                beginMs = Profiler.StageMs("rigid begin"), narrowMs = Profiler.StageMs("rigid narrowphase"), prepareMs = Profiler.StageMs("rigid prepare"),
                islandsMs = Profiler.StageMs("rigid islands"), solveMs = Profiler.StageMs("rigid solve"), ccdMs = Profiler.StageMs("rigid ccd"), finishMs = Profiler.StageMs("rigid finish"),
            };
            _result.samples.Add(s);
            _csv?.WriteLine(string.Join(",", _current.Name, F(s.time), F(s.frameMs), F(s.physicsMs), F(s.stepMs), s.fixedSteps, F(s.sceneMs), F(s.renderMs), s.rigidBodies, s.awake, s.manifolds, s.contacts, s.joints, s.kinematic, s.movers, s.queries, s.sweeps, s.raycasts, s.ccd, s.alive, s.islands, s.largestIsland, s.threads, F(s.allocBytes / 1024.0)));
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void Summarise()
        {
            var r = _result;
            int n = r.samples.Count;
            r.frames = n;
            if (n == 0) { _results.Add(r); return; }
            var frame = new double[n]; var step = new double[n];
            double fSum = 0, pSum = 0, stSum = 0, sSum = 0, rSum = 0, aSum = 0, stepsSum = 0;
            long bodies = 0, awake = 0, manifolds = 0, contacts = 0, joints = 0, kin = 0, alive = 0, queries = 0, sweeps = 0, rays = 0, islands = 0, largest = 0, draws = 0, dispatches = 0, stages = 0;
            double bSum = 0, nSum = 0, prSum = 0, iSum = 0, soSum = 0, cSum = 0, fiSum = 0; int stepFrames = 0;
            int aliveMin = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var s = r.samples[i];
                frame[i] = s.frameMs; step[i] = s.stepMs;
                fSum += s.frameMs; pSum += s.physicsMs; stSum += s.stepMs; sSum += s.sceneMs; rSum += s.renderMs; aSum += s.allocBytes; stepsSum += s.fixedSteps;
                bodies += s.rigidBodies; awake += s.awake; manifolds += s.manifolds; contacts += s.contacts; joints += s.joints; kin += s.kinematic; alive += s.alive;
                queries += s.queries; sweeps += s.sweeps; rays += s.raycasts; islands += s.islands; largest += s.largestIsland; draws += s.drawCalls;
                dispatches += s.dispatches; stages += s.stages;
                if (s.fixedSteps > 0)
                {
                    stepFrames++;
                    bSum += s.beginMs / s.fixedSteps; nSum += s.narrowMs / s.fixedSteps; prSum += s.prepareMs / s.fixedSteps; iSum += s.islandsMs / s.fixedSteps;
                    soSum += s.solveMs / s.fixedSteps; cSum += s.ccdMs / s.fixedSteps; fiSum += s.finishMs / s.fixedSteps;
                }
                r.threads = Math.Max(r.threads, s.threads);
                if (s.alive < aliveMin) aliveMin = s.alive;
            }
            Array.Sort(frame); Array.Sort(step);
            r.frameAvg = fSum / n; r.frameP99 = frame[Math.Clamp((int)(0.99 * (n - 1)), 0, n - 1)]; r.frameMax = frame[n - 1];
            r.physicsAvg = pSum / n; r.stepsAvg = stepsSum / n;
            r.stepAvg = stSum / n; r.stepP99 = step[Math.Clamp((int)(0.99 * (n - 1)), 0, n - 1)]; r.stepMax = step[n - 1];
            r.sceneAvg = sSum / n; r.renderAvg = rSum / n;
            r.bodiesAvg = (int)(bodies / n); r.awakeAvg = (int)(awake / n); r.manifoldsAvg = (int)(manifolds / n); r.contactsAvg = (int)(contacts / n);
            r.jointsAvg = (int)(joints / n); r.kinematicAvg = (int)(kin / n); r.aliveAvg = (int)(alive / n); r.aliveMin = aliveMin;
            r.queriesAvg = (int)(queries / n); r.sweepsAvg = (int)(sweeps / n); r.raycastsAvg = (int)(rays / n);
            r.islandsAvg = (int)(islands / n); r.largestIslandAvg = (int)(largest / n); r.drawCallsAvg = (int)(draws / n);
            r.dispatchesAvg = (int)(dispatches / n); r.stagesAvg = (int)(stages / n);
            if (stepFrames > 0) { r.beginAvg = bSum / stepFrames; r.narrowAvg = nSum / stepFrames; r.prepareAvg = prSum / stepFrames; r.islandsTimeAvg = iSum / stepFrames; r.solveAvg = soSum / stepFrames; r.ccdAvg = cSum / stepFrames; r.finishAvg = fiSum / stepFrames; }
            r.allocKbAvg = aSum / n / 1024.0;
            r.framePass = r.frameAvg <= config.budgetFrameMs;
            r.physicsPass = r.stepAvg <= config.budgetPhysicsMs;
            if (!r.Pass) Failed = true;
            _results.Add(r);
            Debug.LogWarning("[bench] " + Line(r));
        }

        private string Line(ScenarioResult r) =>
            $"{r.name,-12} {(r.Pass ? "PASS" : "FAIL")}  frame {r.frameAvg:0.00} ms avg / {r.frameP99:0.0} p99 / {r.frameMax:0.0} max ({r.fps:0} fps)  physics/step {r.stepAvg:0.00} / {r.stepP99:0.0} / {r.stepMax:0.0} ms ({r.stepsAvg:0.0} steps/frame, {r.physicsAvg:0.0} ms/frame)  scene {r.sceneAvg:0.00}  render {r.renderAvg:0.00}  bodies {r.bodiesAvg} ({r.awakeAvg} awake)  islands {r.islandsAvg} (max {r.largestIslandAvg})  threads {r.threads}  contacts {r.contactsAvg}  joints {r.jointsAvg}  alive {r.aliveAvg} (min {r.aliveMin})  alloc {r.allocKbAvg:0.0} KB/frame";

        private void Finish()
        {
            _done = true;
            string report = Report();
            Console.WriteLine();
            Console.WriteLine(report);
            if (config.reportPath != null)
            {
                try { File.WriteAllText(config.reportPath, report); Console.WriteLine("Report written to " + config.reportPath); }
                catch (Exception e) { Debug.LogError("report: " + e.Message); }
            }
            Application.Quit();
        }

        /// <summary>Markdown report: a summary table, then the counts per scenario.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Rip-n-Rage benchmark");
            sb.AppendLine();
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  {Environment.OSVersion}  ·  {Environment.ProcessorCount} cores  ·  .NET {Environment.Version}  ·  GPU: {Engine.Platform.GpuInfo.Summary}");
            sb.AppendLine($"Budgets: frame ≤ {config.budgetFrameMs:0.#} ms avg, physics ≤ {config.budgetPhysicsMs:0.#} ms per fixed step (rigid step + the per-frame mover / trigger pass)  ·  {config.duration:0.#} s measured after {config.warmup:0.#} s warm-up  ·  scale ×{config.scale:0.##}{(Application.Current != null && Application.Current.Options.NoRender ? "  ·  rendering off" : "")}{(Application.Current != null && Application.Current.Options.FixedFrameDelta > 0f ? $"  ·  fixed dt {Application.Current.Options.FixedFrameDelta * 1000f:0.#} ms" : "")}");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Target | Result | Frame avg / p99 / max (ms) | FPS | Physics per step avg / p99 / max (ms) | Fixed steps / frame | Physics / frame (ms) | Scene (ms) | Render (ms) | Alloc (KB/frame) |");
            sb.AppendLine("|---|---:|:---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results)
            {
                if (r.frames == 0) { sb.AppendLine($"| {r.name} | {r.target} | no samples | | | | | | | | |"); continue; }
                sb.AppendLine($"| {r.name} | {r.target} | **{(r.Pass ? "PASS" : "FAIL")}**{(r.Pass ? "" : r.framePass ? " (physics)" : r.physicsPass ? " (frame)" : " (both)")} | {r.frameAvg:0.00} / {r.frameP99:0.0} / {r.frameMax:0.0} | {r.fps:0} | {r.stepAvg:0.00} / {r.stepP99:0.0} / {r.stepMax:0.0} | {r.stepsAvg:0.0} | {r.physicsAvg:0.00} | {r.sceneAvg:0.00} | {r.renderAvg:0.00} | {r.allocKbAvg:0.0} |");
            }
            sb.AppendLine();
            sb.AppendLine("| Scenario | Rigid bodies (awake) | Islands (largest) | Threads | Manifolds | Contacts | Joints | Kinematic bodies | Alive (min) | Queries / sweeps / rays per frame | Draw calls | Frames |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results)
                sb.AppendLine($"| {r.name} | {r.bodiesAvg} ({r.awakeAvg}) | {r.islandsAvg} ({r.largestIslandAvg}) | {r.threads} | {r.manifoldsAvg} | {r.contactsAvg} | {r.jointsAvg} | {r.kinematicAvg} | {r.aliveAvg} ({r.aliveMin}) | {r.queriesAvg} / {r.sweepsAvg} / {r.raycastsAvg} | {r.drawCallsAvg} | {r.frames} |");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Rigid step breakdown per step (ms): begin | narrow phase | prepare | islands | solve (of which CCD) | finish | Dispatches / stages per step |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results)
                sb.AppendLine($"| {r.name} | {r.beginAvg:0.00} | {r.narrowAvg:0.00} | {r.prepareAvg:0.00} | {r.islandsTimeAvg:0.00} | {r.solveAvg:0.00} ({r.ccdAvg:0.00}) | {r.finishAvg:0.00} | {r.dispatchesAvg} / {r.stagesAvg} |");
            sb.AppendLine();
            foreach (var r in _results) sb.AppendLine($"- **{r.name}** — {r.description}");
            sb.AppendLine();
            sb.AppendLine(Failed ? "Result: **FAIL** (a scenario exceeded its budget)" : "Result: **PASS**");
            return sb.ToString();
        }
    }
}
