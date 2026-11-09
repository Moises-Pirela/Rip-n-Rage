using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// The rigid-body step: pairs from the broadphase, manifolds from <see cref="ContactGeneration"/>, contacts
    /// solved by the <see cref="ContactSolver"/> and joints by their <see cref="Constraint"/>s, all inside Erin
    /// Catto's <b>soft step</b> loop: the fixed step is cut into <see cref="substeps"/>; each one integrates the
    /// velocities, warm-starts every constraint with the impulses cached from the previous step (unless the solver
    /// runs cold), solves joints then contacts <see cref="velocityIterations"/> times with position bias, clamps
    /// continuous-collision motion, integrates the positions, relaxes (solves again without bias) and — in NGS
    /// mode — corrects the poses directly. Restitution is applied after the substeps, impulses are stored in the
    /// contact cache, constraints may break, resting islands fall asleep.
    ///
    /// <b>Islands.</b> Every step the awake dynamic bodies are grouped by union-find over the contacts and joints
    /// that connect two dynamic bodies (static, kinematic and sleeping bodies do not connect anything). An island
    /// is solved on its own — Gauss-Seidel inside, independent of every other island — so small islands run side by
    /// side on the <see cref="JobSystem"/>'s <see cref="threads"/> workers, and an island only falls asleep when every
    /// body in it has been at rest for the sleep delay (a body can no longer doze off under a moving stack). A
    /// <b>large island</b> (a heap of a thousand crates is one island) is <b>graph coloured</b> instead: its contacts
    /// and joints are greedily assigned colours so that no two constraints of one colour share a dynamic body; the
    /// colours are solved one after another (still Gauss-Seidel) but every colour's constraints run in parallel, the
    /// way Box2D v3 does it. A whole substep phase is one multi-stage job dispatch (small islands, integrate, every
    /// colour of the warm start, every colour of the velocity pass, the overflow list): the workers barrier between
    /// stages among themselves, so a step costs about eleven dispatches instead of hundreds of thread-pool round
    /// trips. The narrow phase (one body's pairs at a time) and the contact preparation are parallel too;
    /// everything that raises events, touches the broadphase or the contact cache stays on the calling thread.
    ///
    /// Position correction comes in three flavours (<see cref="positionCorrection"/>): <b>Soft</b> (contacts and
    /// joints are stiff spring-dampers; stable, no energy gain — the default), <b>Baumgarte</b> (a velocity bias
    /// proportional to the error, the classic sequential-impulse look) and <b>NGS</b> (non-linear Gauss-Seidel
    /// position passes after the velocity solve). Friction is a static / dynamic Coulomb cone.
    /// </summary>
    public sealed class RigidSolver
    {
        // ---- stepping ----
        public int substeps = 4;
        /// <summary>Biased velocity passes per substep.</summary>
        public int velocityIterations = 1;
        /// <summary>Unbiased (relax) passes per substep.</summary>
        public int relaxIterations = 1;
        /// <summary>NGS position passes per substep.</summary>
        public int positionIterations = 2;
        public PositionCorrectionMode positionCorrection = PositionCorrectionMode.Soft;
        /// <summary>Carry impulses over from the previous step (warm start); off = cold start every step.</summary>
        public bool warmStarting = true;
        /// <summary>Worker threads for the narrow phase, contact preparation and the island solve: 0 = one per core (capped at 16), 1 = single-threaded.</summary>
        public int threads = 0;
        /// <summary>Islands / bodies below this amount of work are solved on the calling thread (a dispatch is not worth it).</summary>
        public int parallelThreshold = 64;
        /// <summary>Islands with at least this many bodies are graph coloured and solved colour by colour in parallel.</summary>
        public int colorIslandThreshold = 96;
        /// <summary>Constraints that found no free colour (a body touching more than <see cref="MaxColors"/> others) in the last step; solved sequentially.</summary>
        public int OverflowConstraints { get; private set; }
        /// <summary>Colours used by the largest coloured island in the last step.</summary>
        public int ColorsUsed { get; private set; }
        public const int MaxColors = 24;
        // ---- contacts ----
        /// <summary>Stiffness of the contact spring (cycles per second; capped at a quarter of the substep rate).</summary>
        public float contactHertz = 30f;
        public float dampingRatio = 10f;
        /// <summary>Fraction of the error corrected per substep (Baumgarte / NGS).</summary>
        public float baumgarte = 0.2f;
        /// <summary>Penetration tolerated without correction.</summary>
        public float linearSlop = 0.005f;
        /// <summary>Joint angle error tolerated without correction (radians).</summary>
        public float angularSlop = 0.035f;
        /// <summary>Largest NGS correction per pass (m).</summary>
        public float maxLinearCorrection = 0.2f;
        /// <summary>Largest speed the penetration recovery may push with.</summary>
        public float maxBiasVelocity = 4f;
        /// <summary>Approach speed above which restitution applies.</summary>
        public float restitutionThreshold = 1f;
        public FrictionModel frictionModel { get => Contacts.frictionModel; set => Contacts.frictionModel = value; }
        /// <summary>Tangential speed below which a contact sticks (static friction) rather than slides (dynamic friction).</summary>
        public float slipSpeed { get => Contacts.slipSpeed; set => Contacts.slipSpeed = MathF.Max(0f, value); }
        /// <summary>Steps the contact cache keeps a manifold after the shapes stop touching.</summary>
        public int contactCacheLifetime { get => Contacts.cacheLifetime; set => Contacts.cacheLifetime = Math.Max(0, value); }
        /// <summary>Friction and bounciness of colliders that have no rigid body and no material (level geometry, movers).</summary>
        public float staticFriction = 0.6f;
        public float dynamicFriction = 0.5f;
        public float staticRestitution = 0f;
        public CombineMode frictionCombine = CombineMode.Average;
        public CombineMode restitutionCombine = CombineMode.Maximum;

        public readonly ContactSolver Contacts = new ContactSolver();
        public int ManifoldCount => _manifolds.Count;
        public int ContactCount { get; private set; }
        public int JointCount => _activeConstraints.Count;
        public int CachedManifolds => Contacts.CachedManifolds;
        /// <summary>Bodies that were simulated (awake, non-kinematic) in the last step.</summary>
        public int AwakeBodies { get; private set; }
        /// <summary>Islands solved in the last step and the size of the largest one.</summary>
        public int IslandCount { get; private set; }
        public int LargestIsland { get; private set; }
        /// <summary>Worker threads the last step used (1 = everything ran on the calling thread).</summary>
        public int ThreadsUsed { get; private set; }
        /// <summary>Job dispatches and stages (sync points) of the last step.</summary>
        public int Dispatches { get; private set; }
        public int StageCount { get; private set; }
        public int MatchedPoints => Contacts.MatchedPoints;

        private readonly List<Manifold> _manifolds = new List<Manifold>(64);
        private readonly List<Constraint> _activeConstraints = new List<Constraint>(16);
        private readonly HashSet<long> _noCollide = new HashSet<long>();
        private readonly List<SolverBody> _awake = new List<SolverBody>(64);
        private readonly List<SolverBody> _kinematic = new List<SolverBody>(8);
        private readonly List<SolverBody> _ccd = new List<SolverBody>(16);
        private readonly List<SolverBody> _pendingWake = new List<SolverBody>(16);

        // ------------------------------------------------------------------ worker scratch

        /// <summary>Scratch owned by one worker thread during the narrow phase (no shared mutable state).</summary>
        private sealed class Worker
        {
            public int index;
            public readonly List<Collider> candidates = new List<Collider>(32);
            public readonly List<int> tris = new List<int>(32);
            public readonly ConvexShape triangle = ConvexShape.CreateTriangle();
            public readonly ConvexShape aabb = ConvexShape.CreateBox();
            public readonly Manifold scratch = new Manifold();
            public readonly Stack<Manifold> pool = new Stack<Manifold>();
            public readonly List<Manifold> manifolds = new List<Manifold>(64);
            public readonly List<SolverBody> wake = new List<SolverBody>(8);
        }

        private Worker[] _workers = Array.Empty<Worker>();

        private int EffectiveThreads()
        {
            int t = threads <= 0 ? Math.Min(Environment.ProcessorCount, 16) : threads;
            return Math.Max(1, t);
        }

        private void EnsureWorkers(int n)
        {
            if (_workers.Length >= n) return;
            var arr = new Worker[n];
            for (int i = 0; i < n; i++) arr[i] = i < _workers.Length ? _workers[i] : new Worker { index = i };
            _workers = arr;
        }

        private static long PairKey(SolverBody a, SolverBody b)
        {
            int x = a.id, y = b.id;
            return x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
        }

        // ------------------------------------------------------------------ islands

        private sealed class Island
        {
            public readonly List<SolverBody> bodies = new List<SolverBody>(16);
            public readonly List<Manifold> manifolds = new List<Manifold>(32);
            public readonly List<Constraint> constraints = new List<Constraint>(4);
            // graph colouring (large islands only)
            public bool colored;
            public int colors;
            public List<Manifold>[] colorManifolds;
            public List<Constraint>[] colorConstraints;
            public readonly List<Manifold> overflowManifolds = new List<Manifold>();
            public readonly List<Constraint> overflowConstraints = new List<Constraint>();
            public void Clear()
            {
                bodies.Clear(); manifolds.Clear(); constraints.Clear(); colored = false; colors = 0;
                overflowManifolds.Clear(); overflowConstraints.Clear();
                if (colorManifolds != null) for (int i = 0; i < colorManifolds.Length; i++) { colorManifolds[i].Clear(); colorConstraints[i].Clear(); }
            }
        }

        private readonly List<Island> _islands = new List<Island>(64);
        private readonly List<Island> _smallIslands = new List<Island>(64);
        private readonly List<Island> _largeIslands = new List<Island>(4);
        private readonly Stack<Island> _islandPool = new Stack<Island>();
        private int[] _parent = Array.Empty<int>();
        private int[] _islandOf = Array.Empty<int>();
        private ulong[] _usedColors = Array.Empty<ulong>();

        private int Find(int i)
        {
            while (_parent[i] != i) { _parent[i] = _parent[_parent[i]]; i = _parent[i]; }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        /// <summary>Groups the awake bodies by what connects them; every awake body lands in exactly one island.</summary>
        private void BuildIslands()
        {
            int n = _awake.Count;
            if (_parent.Length < n) { _parent = new int[Math.Max(n, 64)]; _islandOf = new int[_parent.Length]; }
            for (int i = 0; i < n; i++) { _parent[i] = i; _islandOf[i] = -1; }
            foreach (var m in _manifolds)
            {
                var a = m.bodyA; var b = m.bodyB;
                if (b != null && Dynamic(b) && Dynamic(a) && a.solverIndex >= 0 && b.solverIndex >= 0) Union(a.solverIndex, b.solverIndex);
            }
            foreach (var c in _activeConstraints)
            {
                var a = c.bodyA; var b = c.bodyB;
                if (a != null && b != null && Dynamic(a) && Dynamic(b) && a.solverIndex >= 0 && b.solverIndex >= 0) Union(a.solverIndex, b.solverIndex);
            }
            foreach (var isl in _islands) { isl.Clear(); _islandPool.Push(isl); }
            _islands.Clear();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (_islandOf[root] < 0) { _islandOf[root] = _islands.Count; _islands.Add(_islandPool.Count > 0 ? _islandPool.Pop() : new Island()); }
                _islands[_islandOf[root]].bodies.Add(_awake[i]);
            }
            foreach (var m in _manifolds)
            {
                // the manifold belongs to the island of its dynamic body (A is always awake dynamic here)
                int idx = m.bodyA.solverIndex >= 0 ? m.bodyA.solverIndex : (m.bodyB != null ? m.bodyB.solverIndex : -1);
                if (idx < 0) { m.island = -1; continue; }
                m.island = _islandOf[Find(idx)];
                _islands[m.island].manifolds.Add(m);
            }
            foreach (var c in _activeConstraints)
            {
                int idx = c.bodyA != null && c.bodyA.solverIndex >= 0 ? c.bodyA.solverIndex : (c.bodyB != null ? c.bodyB.solverIndex : -1);
                if (idx < 0) continue;
                _islands[_islandOf[Find(idx)]].constraints.Add(c);
            }
            IslandCount = _islands.Count;
            int largest = 0;
            foreach (var isl in _islands) if (isl.bodies.Count > largest) largest = isl.bodies.Count;
            LargestIsland = largest;
        }

        /// <summary>Greedy graph colouring: a constraint takes the lowest colour none of its dynamic bodies has yet.</summary>
        private void ColorIsland(Island isl)
        {
            if (isl.colorManifolds == null)
            {
                isl.colorManifolds = new List<Manifold>[MaxColors]; isl.colorConstraints = new List<Constraint>[MaxColors];
                for (int i = 0; i < MaxColors; i++) { isl.colorManifolds[i] = new List<Manifold>(); isl.colorConstraints[i] = new List<Constraint>(); }
            }
            if (_usedColors.Length < _awake.Count) _usedColors = new ulong[Math.Max(_awake.Count, 64)];
            foreach (var b in isl.bodies) _usedColors[b.solverIndex] = 0;
            int colors = 0;
            foreach (var c in isl.constraints)
            {
                int ia = Dynamic(c.bodyA) ? c.bodyA.solverIndex : -1, ib = Dynamic(c.bodyB) ? c.bodyB.solverIndex : -1;
                int color = PickColor(ia, ib);
                if (color < 0) { isl.overflowConstraints.Add(c); continue; }
                isl.colorConstraints[color].Add(c); if (color + 1 > colors) colors = color + 1;
            }
            foreach (var m in isl.manifolds)
            {
                int ia = m.bodyA.solverIndex, ib = Dynamic(m.bodyB) ? m.bodyB.solverIndex : -1;
                int color = PickColor(ia, ib);
                if (color < 0) { isl.overflowManifolds.Add(m); continue; }
                isl.colorManifolds[color].Add(m); if (color + 1 > colors) colors = color + 1;
            }
            isl.colors = colors; isl.colored = true;
        }

        private int PickColor(int ia, int ib)
        {
            ulong used = (ia >= 0 ? _usedColors[ia] : 0UL) | (ib >= 0 ? _usedColors[ib] : 0UL);
            for (int c = 0; c < MaxColors; c++)
            {
                ulong bit = 1UL << c;
                if ((used & bit) != 0) continue;
                if (ia >= 0) _usedColors[ia] |= bit;
                if (ib >= 0) _usedColors[ib] |= bit;
                return c;
            }
            return -1;
        }

        /// <summary>Sorts the islands into the ones solved whole (in parallel with each other) and the ones solved colour by colour.</summary>
        private void PartitionIslands(bool parallel)
        {
            _smallIslands.Clear(); _largeIslands.Clear();
            OverflowConstraints = 0; ColorsUsed = 0;
            foreach (var isl in _islands)
            {
                if (parallel && isl.bodies.Count >= colorIslandThreshold)
                {
                    ColorIsland(isl);
                    _largeIslands.Add(isl);
                    OverflowConstraints += isl.overflowManifolds.Count + isl.overflowConstraints.Count;
                    if (isl.colors > ColorsUsed) ColorsUsed = isl.colors;
                }
                else _smallIslands.Add(isl);
            }
        }

        // ------------------------------------------------------------------ jobs

        private enum Pass { WarmStart, Velocity, Relax, Position, Restitution }
        private enum StageKind { Narrowphase, Prepare, SmallIslandsA, SmallIslandsB, SmallRestitution, IntegrateVelocities, IntegratePositions, Color, Overflow }

        /// <summary>One stage of a dispatch: what to do with items [0, count).</summary>
        private struct Stage
        {
            public StageKind kind;
            public Island island;
            public int color;
            public Pass pass;
        }

        /// <summary>
        /// The solver's job: a table of stages filled per dispatch. The whole of a substep phase — integrate the
        /// large island's bodies, warm-start colour by colour, solve colour by colour, the overflow list on one
        /// thread, plus the small islands as independent items — is one dispatch; workers barrier between stages.
        /// </summary>
        private sealed class StepJob : IJob
        {
            public RigidSolver solver;
            public PhysicsWorld world;
            public SolverParams p;
            public float h;
            public readonly List<Stage> stages = new List<Stage>(64);
            public int[] counts = new int[64], batches = new int[64];

            public void Clear() { stages.Clear(); }

            public void Add(StageKind kind, int count, int batch, Island island = null, int color = 0, Pass pass = Pass.Velocity)
            {
                if (count <= 0) return;
                if (stages.Count >= counts.Length) { Array.Resize(ref counts, counts.Length * 2); Array.Resize(ref batches, batches.Length * 2); }
                counts[stages.Count] = count; batches[stages.Count] = Math.Max(1, batch);
                stages.Add(new Stage { kind = kind, island = island, color = color, pass = pass });
            }

            public void Execute(int stage, int start, int end, int worker)
            {
                var st = stages[stage];
                var s = solver;
                switch (st.kind)
                {
                    case StageKind.Narrowphase:
                        for (int i = start; i < end; i++) s.CollectContacts(world, s._awake[i], s._workers[worker]);
                        break;
                    case StageKind.Prepare:
                        for (int i = start; i < end; i++) s.Contacts.Prepare(s._manifolds[i], in p);
                        break;
                    case StageKind.SmallIslandsA:
                        for (int i = start; i < end; i++) s.PhaseA(s._smallIslands[i], h, in p);
                        break;
                    case StageKind.SmallIslandsB:
                        for (int i = start; i < end; i++) s.PhaseB(s._smallIslands[i], h, in p);
                        break;
                    case StageKind.SmallRestitution:
                        for (int i = start; i < end; i++) foreach (var m in s._smallIslands[i].manifolds) s.Contacts.ApplyRestitution(m, s.restitutionThreshold);
                        break;
                    case StageKind.IntegrateVelocities:
                        for (int i = start; i < end; i++) st.island.bodies[i].IntegrateVelocity(h);
                        break;
                    case StageKind.IntegratePositions:
                        for (int i = start; i < end; i++) st.island.bodies[i].IntegratePosition(h);
                        break;
                    case StageKind.Color:
                    {
                        var cs = st.island.colorConstraints[st.color]; var ms = st.island.colorManifolds[st.color];
                        int nc = cs.Count;
                        for (int i = start; i < end; i++) { if (i < nc) s.RunOne(cs[i], st.pass, in p); else s.RunOne(ms[i - nc], st.pass, in p); }
                        break;
                    }
                    case StageKind.Overflow:
                        foreach (var c in st.island.overflowConstraints) s.RunOne(c, st.pass, in p);
                        foreach (var m in st.island.overflowManifolds) s.RunOne(m, st.pass, in p);
                        break;
                }
            }
        }

        private readonly StepJob _job = new StepJob();
        private JobSystem _jobs;
        private bool _parallel;

        private void Dispatch()
        {
            int n = _job.stages.Count;
            if (n == 0) return;
            Dispatches++; StageCount += n;
            if (_parallel) _jobs.Run(_job, _job.counts, _job.batches, n);
            else for (int s = 0; s < n; s++) _job.Execute(s, 0, _job.counts[s], 0);
        }

        /// <summary>Chunk size that gives every thread a few chunks of a colour.</summary>
        private int ColorBatch(int items) => Math.Max(4, items / (ThreadsUsed * 4));

        /// <summary>Adds the colour stages (and the overflow stage) of a pass over one large island.</summary>
        private void AddColorPass(Island isl, Pass pass)
        {
            for (int c = 0; c < isl.colors; c++)
            {
                int items = isl.colorConstraints[c].Count + isl.colorManifolds[c].Count;
                _job.Add(StageKind.Color, items, ColorBatch(items), isl, c, pass);
            }
            if (isl.overflowConstraints.Count + isl.overflowManifolds.Count > 0) _job.Add(StageKind.Overflow, 1, 1, isl, 0, pass);
        }

        // ------------------------------------------------------------------ step

        public void Step(PhysicsWorld world, List<SolverBody> bodies, float dt)
        {
            Contacts.BeginStep();
            int sub = Math.Max(1, substeps);
            float h = dt / sub;
            var p = new SolverParams
            {
                h = h, invH = 1f / h, mode = positionCorrection, baumgarte = baumgarte, linearSlop = linearSlop, angularSlop = angularSlop,
                maxLinearCorrection = maxLinearCorrection, maxBiasVelocity = maxBiasVelocity, warmStart = warmStarting,
            };
            int maxThreads = EffectiveThreads();
            EnsureWorkers(maxThreads);
            _jobs = maxThreads > 1 ? JobSystem.Shared(maxThreads) : null;
            Dispatches = 0; StageCount = 0;
            _job.solver = this; _job.world = world; _job.p = p; _job.h = h;

            // 0. bodies: begin, caches current, lists
            Profiler.Begin("rigid begin");
            _awake.Clear(); _kinematic.Clear(); _ccd.Clear();
            foreach (var b in bodies)
            {
                b.solverIndex = -1;
                b.RefreshStepState();
                if (!b.live) continue;
                b.contacts.Clear(); b.BeginStep(); b.WarmCaches();
                if (b.isKinematic) _kinematic.Add(b);
                else if (!b.sleeping)
                {
                    b.solverIndex = _awake.Count; _awake.Add(b);
                    if (b.collisionDetection != CollisionDetectionMode.Discrete) _ccd.Add(b);
                }
            }

            // 1. constraints (and which pairs must not collide)
            var constraints = world.ConstraintList;
            _activeConstraints.Clear();
            _noCollide.Clear();
            foreach (var c in constraints)
            {
                if (c.bodyA != null && c.bodyB != null && !c.collideConnected && !c.broken && c.enabled) _noCollide.Add(PairKey(c.bodyA, c.bodyB));
                if (c.Prepare(in p)) _activeConstraints.Add(c);
            }
            foreach (var c in _activeConstraints) { Adopt(c.bodyA); Adopt(c.bodyB); } // Prepare may have woken bodies
            AwakeBodies = _awake.Count;
            _parallel = _jobs != null && _awake.Count >= parallelThreshold;
            ThreadsUsed = _parallel ? maxThreads : 1;
            Profiler.End();

            // 2. narrow phase: one body's pairs per item, per-worker scratch
            Profiler.Begin("rigid narrowphase");
            ReturnManifolds();
            _job.Clear(); _job.Add(StageKind.Narrowphase, _awake.Count, 4);
            Dispatch();
            _manifolds.Clear(); _pendingWake.Clear();
            for (int i = 0; i < _workers.Length; i++)
            {
                var w = _workers[i];
                if (w.manifolds.Count > 0) { _manifolds.AddRange(w.manifolds); w.manifolds.Clear(); }
                if (w.wake.Count > 0) { _pendingWake.AddRange(w.wake); w.wake.Clear(); }
            }
            Profiler.End();

            // 3. contact preparation (effective masses, warm-start matching): per manifold
            Profiler.Begin("rigid prepare");
            ContactCount = 0;
            foreach (var m in _manifolds) { ContactCount += m.count; m.hertz = contactHertz; m.dampingRatio = dampingRatio; }
            _job.Clear(); _job.Add(StageKind.Prepare, _manifolds.Count, 16);
            Dispatch();
            Profiler.End();

            // 4. islands
            Profiler.Begin("rigid islands");
            BuildIslands();
            PartitionIslands(_parallel);
            Profiler.End();

            // 5. substeps: [phase A dispatch] → CCD clamp + kinematic motion → [phase B dispatch]
            Profiler.Begin("rigid solve");
            CcdSweepsThisStep = 0;
            int vIters = Math.Max(1, velocityIterations), rIters = Math.Max(0, relaxIterations), pIters = positionCorrection == PositionCorrectionMode.NGS ? Math.Max(1, positionIterations) : 0;
            for (int s = 0; s < sub; s++)
            {
                // phase A: integrate velocities, warm start, biased velocity solve
                _job.Clear();
                _job.Add(StageKind.SmallIslandsA, _smallIslands.Count, 1);
                foreach (var isl in _largeIslands)
                {
                    _job.Add(StageKind.IntegrateVelocities, isl.bodies.Count, 64, isl);
                    if (warmStarting) AddColorPass(isl, Pass.WarmStart);
                    for (int it = 0; it < vIters; it++) AddColorPass(isl, Pass.Velocity);
                }
                Dispatch();

                // between: continuous collision (broadphase sweeps, calling thread) and kinematic motion
                if (_ccd.Count > 0)
                {
                    Profiler.Begin("rigid ccd");
                    foreach (var b in _ccd) if (!b.sleeping) ClampMotion(world, b, h);
                    Profiler.End();
                }
                foreach (var k in _kinematic) { k.IntegratePosition(h); k.WarmCaches(); }

                // phase B: integrate positions, relax, NGS
                _job.Clear();
                _job.Add(StageKind.SmallIslandsB, _smallIslands.Count, 1);
                foreach (var isl in _largeIslands)
                {
                    _job.Add(StageKind.IntegratePositions, isl.bodies.Count, 64, isl);
                    for (int it = 0; it < rIters; it++) AddColorPass(isl, Pass.Relax);
                    for (int it = 0; it < pIters; it++) AddColorPass(isl, Pass.Position);
                }
                Dispatch();
            }

            // restitution (after the substeps)
            _job.Clear();
            _job.Add(StageKind.SmallRestitution, _smallIslands.Count, 1);
            foreach (var isl in _largeIslands) AddColorPass(isl, Pass.Restitution);
            Dispatch();
            _jobs?.Idle();
            Profiler.End();

            // 6. breaking, cache, sleep (islands), reporting
            Profiler.Begin("rigid finish");
            foreach (var c in _activeConstraints) c.Finish(h);
            constraints.RemoveAll(c => c.broken);
            foreach (var m in _manifolds) Contacts.Store(m);
            Contacts.EndStep();

            foreach (var b in _kinematic) b.EndStep();
            foreach (var isl in _islands)
            {
                float minTimer = float.MaxValue;
                bool canSleep = true;
                foreach (var b in isl.bodies)
                {
                    b.EndStep();
                    float t = b.AdvanceSleepTimer(dt);
                    if (!b.canSleep) canSleep = false;
                    if (t < minTimer) minTimer = t;
                }
                // jointed to something kinematic (a motor, a moving platform)? stay awake
                foreach (var c in isl.constraints)
                    if ((c.bodyA != null && c.bodyA.isKinematic) || (c.bodyB != null && c.bodyB.isKinematic)) canSleep = false;
                if (canSleep && isl.bodies.Count > 0 && minTimer >= SleepDelayOf(isl)) foreach (var b in isl.bodies) b.Sleep();
            }
            // sleeping bodies that something moving touched join the simulation next step (waking them mid-step
            // would let two islands write to a body that belongs to neither)
            foreach (var b in _pendingWake) if (b.sleeping) b.WakeUp();
            _pendingWake.Clear();
            foreach (var m in _manifolds) Report(m);
            foreach (var b in _awake) b.RaiseContacts();
            Profiler.End();
        }

        /// <summary>A body referenced by a constraint that Prepare woke up joins the awake list for this step.</summary>
        private void Adopt(SolverBody b)
        {
            if (b == null || b.isKinematic || b.sleeping || !b.live || b.solverIndex >= 0) return;
            b.solverIndex = _awake.Count; _awake.Add(b);
            if (b.collisionDetection != CollisionDetectionMode.Discrete) _ccd.Add(b);
        }

        private static float SleepDelayOf(Island isl)
        {
            float d = 0f;
            foreach (var b in isl.bodies) if (b.sleepDelay > d) d = b.sleepDelay;
            return d;
        }

        /// <summary>Small island, phase A: integrate velocities, warm start, biased velocity passes — one work item.</summary>
        private void PhaseA(Island isl, float h, in SolverParams p)
        {
            int vIters = Math.Max(1, velocityIterations);
            foreach (var b in isl.bodies) b.IntegrateVelocity(h);
            if (p.warmStart)
            {
                foreach (var c in isl.constraints) c.WarmStart();
                foreach (var m in isl.manifolds) Contacts.WarmStart(m);
            }
            for (int it = 0; it < vIters; it++)
            {
                foreach (var c in isl.constraints) c.SolveVelocity(true, in p);
                foreach (var m in isl.manifolds) Contacts.SolveVelocity(m, true, in p);
            }
        }

        /// <summary>Small island, phase B: integrate positions, relax passes, NGS passes.</summary>
        private void PhaseB(Island isl, float h, in SolverParams p)
        {
            int rIters = Math.Max(0, relaxIterations), pIters = positionCorrection == PositionCorrectionMode.NGS ? Math.Max(1, positionIterations) : 0;
            foreach (var b in isl.bodies) b.IntegratePosition(h);
            for (int it = 0; it < rIters; it++)
            {
                foreach (var c in isl.constraints) c.SolveVelocity(false, in p);
                foreach (var m in isl.manifolds) Contacts.SolveVelocity(m, false, in p);
            }
            for (int it = 0; it < pIters; it++)
            {
                foreach (var c in isl.constraints) c.SolvePosition(in p);
                foreach (var m in isl.manifolds) Contacts.SolvePosition(m, in p);
            }
        }

        private void RunOne(Constraint c, Pass pass, in SolverParams p)
        {
            switch (pass)
            {
                case Pass.WarmStart: c.WarmStart(); break;
                case Pass.Velocity: c.SolveVelocity(true, in p); break;
                case Pass.Relax: c.SolveVelocity(false, in p); break;
                case Pass.Position: c.SolvePosition(in p); break;
                default: break; // joints have no restitution
            }
        }

        private void RunOne(Manifold m, Pass pass, in SolverParams p)
        {
            switch (pass)
            {
                case Pass.WarmStart: Contacts.WarmStart(m); break;
                case Pass.Velocity: Contacts.SolveVelocity(m, true, in p); break;
                case Pass.Relax: Contacts.SolveVelocity(m, false, in p); break;
                case Pass.Position: Contacts.SolvePosition(m, in p); break;
                default: Contacts.ApplyRestitution(m, restitutionThreshold); break;
            }
        }

        private static bool Dynamic(SolverBody b) => b != null && !b.isKinematic && !b.sleeping;

        // ------------------------------------------------------------------ pairs

        /// <summary>Gives last step's manifolds back to the pools they came from.</summary>
        private void ReturnManifolds()
        {
            foreach (var m in _manifolds)
            {
                int o = m.owner >= 0 && m.owner < _workers.Length ? m.owner : 0;
                _workers[o].pool.Push(m);
            }
            _manifolds.Clear();
        }

        private static Manifold NewManifold(Worker w)
        {
            var m = w.pool.Count > 0 ? w.pool.Pop() : new Manifold();
            m.Reset();
            m.owner = w.index >= 0 ? w.index : 0;
            return m;
        }

        private void CollectContacts(PhysicsWorld world, SolverBody a, Worker w)
        {
            var candidates = w.candidates;
            candidates.Clear();
            Bounds region = a.worldBounds.Expanded(ContactGeneration.ContactMargin + 0.01f);
            world.QueryBoundsConcurrent(region, a.Filter, candidates);
            float wake = a.sleepThreshold * a.sleepThreshold * 4f;
            bool aMoving = a.linearVelocity.sqrMagnitude > wake || a.angularVelocity.sqrMagnitude > wake;
            foreach (var c in candidates)
            {
                SolverBody b = c.broadphase?.body;
                if (b == a) continue;
                if (b != null)
                {
                    if (!b.live) continue;
                    if (a.collisionGroup != 0 && a.collisionGroup == b.collisionGroup && a.collisionGroup < 0) continue; // same negative group: never
                    if (_noCollide.Contains(PairKey(a, b))) continue; // joined without collideConnected
                    if (!b.isKinematic)
                    {
                        if (b.sleeping) { if (aMoving) w.wake.Add(b); }
                        else if (a.id > b.id) continue; // the other side generates this pair
                    }
                }
                long key = ((long)a.id << 32) ^ (long)c.broadphase.uid;
                if (c is MeshCollider mesh)
                {
                    var tris = w.tris;
                    tris.Clear();
                    mesh.QueryTriangles(region, tris);
                    foreach (int t in tris)
                    {
                        mesh.GetTriangle(t, out Vector3 p0, out Vector3 p1, out Vector3 p2);
                        w.triangle.SetTriangle(p0, p1, p2);
                        bool hit = a.isRounded
                            ? ContactGeneration.RoundedVsConvex(a.WorldShape, w.triangle, w.scratch)
                            : ContactGeneration.ConvexVsConvex(a.shape, w.triangle, w.scratch, w.triangle.triangleNormal);
                        if (hit) Commit(w, a, null, c, key ^ ((long)(t + 1) << 20));
                    }
                    continue;
                }
                bool contact = b != null ? CollideBodies(a, b, w.scratch) : CollideStatic(a, c, w);
                if (contact) Commit(w, a, b, c, key);
            }
        }

        /// <summary>Contacts between two rigid bodies (normal from A to B).</summary>
        private static bool CollideBodies(SolverBody a, SolverBody b, Manifold scratch)
        {
            if (a.isRounded && b.isRounded) return ContactGeneration.RoundedVsRounded(a.WorldShape, b.WorldShape, scratch);
            if (a.isRounded) return ContactGeneration.RoundedVsConvex(a.WorldShape, b.shape, scratch);
            if (b.isRounded)
            {
                bool hit = ContactGeneration.RoundedVsConvex(b.WorldShape, a.shape, scratch);
                if (hit) scratch.normal = -scratch.normal; // the routine's normal points from B towards A
                return hit;
            }
            return ContactGeneration.ConvexVsConvex(a.shape, b.shape, scratch);
        }

        private static bool IsRoundedCollider(Collider c) =>
            c is SphereCollider || c is CapsuleCollider || (c is CharacterController cc && cc.controllerShape == ControllerShape.Capsule);

        /// <summary>Contacts between a rigid body and a collider without one (level geometry, props, movers).</summary>
        private static bool CollideStatic(SolverBody a, Collider c, Worker w)
        {
            if (IsRoundedCollider(c))
            {
                CollisionShape sc = c.shape;
                if (a.isRounded) return ContactGeneration.RoundedVsRounded(a.WorldShape, in sc, w.scratch);
                bool hit = ContactGeneration.RoundedVsConvex(in sc, a.shape, w.scratch);
                if (hit) w.scratch.normal = -w.scratch.normal;
                return hit;
            }
            ConvexShape other = ShapeOf(c, w);
            if (a.isRounded) return ContactGeneration.RoundedVsConvex(a.WorldShape, other, w.scratch);
            return ContactGeneration.ConvexVsConvex(a.shape, other, w.scratch);
        }

        /// <summary>World polytope of a collider without a rigid body, built into the worker's scratch (no shared caches are touched from worker threads).</summary>
        private static ConvexShape ShapeOf(Collider c, Worker w)
        {
            switch (c)
            {
                case ConvexCollider hull: return hull.Shape; // built once per brush; a racing first build just wastes one allocation
                default:
                {
                    CollisionShape s = c.shape;
                    if (s.isPolytope && s.polytope != null) return s.polytope;
                    if (s.type == ShapeType.Box) { w.aabb.SetBox(s.center, s.halfExtents, s.rotation); return w.aabb; }
                    Bounds b = c.bounds;
                    w.aabb.SetBox(b.center, b.extents, Quaternion.identity);
                    return w.aabb;
                }
            }
        }

        private void Commit(Worker w, SolverBody a, SolverBody b, Collider c, long key)
        {
            var scratch = w.scratch;
            if (scratch.count == 0) return;
            var m = NewManifold(w);
            m.bodyA = a; m.bodyB = b; m.colliderA = a.stepCollider; m.colliderB = c;
            m.normal = scratch.normal; m.count = scratch.count;
            for (int i = 0; i < m.count; i++) m.points[i] = scratch.points[i];
            m.key = key;
            Surface(a.stepCollider, a, out float musA, out float mudA, out float restA, out CombineMode fcA, out CombineMode rcA);
            Surface(c, b, out float musB, out float mudB, out float restB, out CombineMode fcB, out CombineMode rcB);
            CombineMode fc = PhysicsMaterial.Pick(fcA, fcB), rc = PhysicsMaterial.Pick(rcA, rcB);
            m.staticFriction = MathF.Max(0f, PhysicsMaterial.Combine(musA, musB, fc));
            m.dynamicFriction = MathF.Max(0f, PhysicsMaterial.Combine(mudA, mudB, fc));
            if (m.staticFriction < m.dynamicFriction) m.staticFriction = m.dynamicFriction;
            m.restitution = Mathf.Clamp01(PhysicsMaterial.Combine(restA, restB, rc));
            w.manifolds.Add(m);
        }

        /// <summary>Surface values of a collider: its material, else its body's values, else the solver's defaults for the level.</summary>
        private void Surface(Collider c, SolverBody body, out float mus, out float mud, out float rest, out CombineMode fc, out CombineMode rc)
        {
            var mat = c != null ? c.material : null;
            if (mat != null) { mus = mat.staticFriction; mud = mat.dynamicFriction; rest = mat.restitution; fc = mat.frictionCombine; rc = mat.restitutionCombine; return; }
            if (body != null) { mus = body.staticFriction; mud = body.friction; rest = body.restitution; fc = frictionCombine; rc = restitutionCombine; return; }
            mus = staticFriction; mud = dynamicFriction; rest = staticRestitution; fc = frictionCombine; rc = restitutionCombine;
        }

        // ------------------------------------------------------------------ continuous collision

        /// <summary>Skin kept between a swept body and what it would hit (under the contact margin, so the next step sees a contact).</summary>
        public float ccdSkin = 0.003f;
        public int CcdSweepsThisStep { get; private set; }

        private static bool StaticOnly(Collider c) { var rb = c.broadphase?.body; return rb == null || rb.isKinematic || rb.isSleeping; }
        private static readonly Func<Collider, bool> StaticOnlyPredicate = StaticOnly;

        /// <summary>
        /// Continuous collision for one substep: a body that would move more than half its smallest extent is swept
        /// (conservative advancement, exact for its shape) against the static world — and, with ContinuousDynamic,
        /// against the other bodies with their own motion — and its displacement this substep is clamped at the first
        /// contact. It then sits within the contact margin, so the next step's manifold takes over (bounce, slide,
        /// rest); nothing can be skipped however fast it goes. Runs on the calling thread (broadphase sweeps).
        /// </summary>
        private void ClampMotion(PhysicsWorld world, SolverBody b, float h)
        {
            if (b.collisionDetection == CollisionDetectionMode.Discrete || b.sleeping || b.isKinematic) return;
            Vector3 motion = b.linearVelocity * h;
            CollisionShape shape = b.WorldShape;
            float threshold = 0.5f * shape.MinExtent;
            if (motion.sqrMagnitude < threshold * threshold) return;
            CcdSweepsThisStep++;
            float fraction = 1f;
            QueryFilter bodyFilter = b.Filter;
            QueryFilter filter = bodyFilter;
            filter.predicate = StaticOnlyPredicate;
            if (world.Sweep(in shape, motion, filter, ccdSkin, out ShapeHit hit)) fraction = MathF.Max(0f, hit.fraction);
            if (b.collisionDetection == CollisionDetectionMode.ContinuousDynamic)
            {
                var bodies = world.Bodies;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var o = bodies[i];
                    if (o == b || o.isKinematic || o.sleeping || !o.live) continue;
                    if (o.stepCollider != null && !bodyFilter.Accepts(o.stepCollider)) continue;
                    Vector3 otherMotion = o.linearVelocity * h;
                    if (!o.worldBounds.Swept(otherMotion).Intersects(shape.bounds.Swept(motion))) continue;
                    CollisionShape os = o.WorldShape;
                    if (ConvexDistance.Sweep(in shape, motion, in os, otherMotion, fraction, ccdSkin, out SweepResult r) && r.fraction < fraction)
                        fraction = MathF.Max(0f, r.fraction);
                }
            }
            if (fraction < 1f) b.motionScale = MathF.Min(b.motionScale, fraction);
        }

        // ------------------------------------------------------------------ reporting

        private static void Report(Manifold m)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint p = ref m.points[i];
                a.contacts.Add(new RigidContact { other = m.colliderB, otherBody = b?.legacy, point = p.point, normal = m.normal, impulse = p.normalImpulse, approachSpeed = p.relativeVelocity });
                if (b != null) b.contacts.Add(new RigidContact { other = m.colliderA, otherBody = a.legacy, point = p.point, normal = -m.normal, impulse = p.normalImpulse, approachSpeed = p.relativeVelocity });
            }
        }
    }
}
