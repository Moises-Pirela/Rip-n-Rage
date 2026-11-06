using System;
using System.Collections;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// Owns all GameObjects and drives the Unity-style update order:
    /// FixedUpdate (fixed steps) → Update → coroutines → LateUpdate → deferred destruction.
    /// </summary>
    public sealed class Scene
    {
        public static Scene Current { get; private set; }

        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<Behaviour> _behaviours = new List<Behaviour>();
        // only the behaviours that override the callback are visited each pass (a RigidBody or a MeshRenderer has no Update)
        private readonly List<Behaviour> _fixedUpdaters = new List<Behaviour>(), _updaters = new List<Behaviour>(), _lateUpdaters = new List<Behaviour>();
        private bool _compact; // something was destroyed: drop it from the lists at the end of the frame (removal is O(1), the sweep O(n) once)
        private readonly List<Behaviour> _pendingStart = new List<Behaviour>();
        private readonly List<Coroutine> _coroutines = new List<Coroutine>();
        private readonly List<(EngineObject obj, float time)> _destroyQueue = new List<(EngineObject, float)>();
        private float _fixedAccumulator;
        /// <summary>Most fixed steps one frame may run before the simulation is allowed to fall behind real time.</summary>
        public static int MaxFixedSteps = 8;

        public IReadOnlyList<GameObject> GameObjects => _gameObjects;

        public Scene()
        {
            Current = this;
        }

        /// <summary>Called by the engine when a frame starts: drops stale coroutines of a previous scene.</summary>
        internal void ClearAll()
        {
            _coroutines.Clear(); _destroyQueue.Clear(); _pendingStart.Clear(); _behaviours.Clear(); _gameObjects.Clear();
            _fixedUpdaters.Clear(); _updaters.Clear(); _lateUpdaters.Clear(); _compact = false;
        }

        public static void MakeCurrent(Scene scene) => Current = scene;

        internal void Register(GameObject go)
        {
            go.scene = this;
            _gameObjects.Add(go);
        }

        internal void RegisterBehaviour(Behaviour b)
        {
            _behaviours.Add(b);
            _pendingStart.Add(b);
            byte f = Behaviour.UpdateFlagsOf(b.GetType());
            if ((f & Behaviour.HasFixedUpdate) != 0) _fixedUpdaters.Add(b);
            if ((f & Behaviour.HasUpdate) != 0) _updaters.Add(b);
            if ((f & Behaviour.HasLateUpdate) != 0) _lateUpdaters.Add(b);
        }

        /// <summary>Drops destroyed objects from every list (once per frame, only after a destruction).</summary>
        private void Compact()
        {
            if (!_compact) return;
            _compact = false;
            _behaviours.RemoveAll(b => b.destroyed);
            _fixedUpdaters.RemoveAll(b => b.destroyed);
            _updaters.RemoveAll(b => b.destroyed);
            _lateUpdaters.RemoveAll(b => b.destroyed);
            _pendingStart.RemoveAll(b => b.destroyed);
            _gameObjects.RemoveAll(g => g.destroyed);
        }

        /// <summary>Calls one callback on every live behaviour of a list, tolerating additions (seen next pass) and destructions (skipped) while it runs.</summary>
        private static void RunPass(List<Behaviour> list, int which, string what)
        {
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                var b = list[i];
                if (!CanRun(b)) continue;
                try
                {
                    switch (which) { case 0: b.CallFixedUpdate(); break; case 1: b.CallUpdate(); break; default: b.CallLateUpdate(); break; }
                }
                catch (Exception e) { Debug.LogError($"{what}() of {b.GetType().Name} on '{b.name}' threw: {e}"); }
            }
        }

        public GameObject Find(string name)
        {
            foreach (var go in _gameObjects)
                if (!go.destroyed && go.name == name) return go;
            return null;
        }

        public T FindObjectOfType<T>() where T : class
        {
            foreach (var b in _behaviours)
                if (b is T t && !b.destroyed) return t;
            return null;
        }

        public List<T> FindObjectsOfType<T>() where T : class
        {
            var list = new List<T>();
            foreach (var go in _gameObjects)
            {
                if (go.destroyed) continue;
                foreach (var c in go.Components)
                    if (c is T t && !c.destroyed) list.Add(t);
            }
            return list;
        }

        // ------------------------------------------------------------------ frame

        /// <summary>Runs one frame of simulation for the given real delta time.</summary>
        public void Tick(float realDeltaTime)
        {
            Time.Advance(realDeltaTime);

            // Fixed steps
            Profiler.Begin("fixed update");
            _fixedAccumulator += Time.deltaTime;
            int steps = 0;
            float savedDt = Time.deltaTime;
            int maxSteps = Math.Max(1, MaxFixedSteps);
            while (_fixedAccumulator >= Time.fixedDeltaTime && steps < maxSteps)
            {
                _fixedAccumulator -= Time.fixedDeltaTime;
                Time.fixedTime += Time.fixedDeltaTime;
                Time.inFixedTimeStep = true;
                Time.deltaTime = Time.fixedDeltaTime;
                RunStarts();
                RunPass(_fixedUpdaters, 0, "FixedUpdate");
                // fixed-step kinematic bodies move after the FixedUpdate callbacks that set their velocities (Unity order)
                Profiler.Begin("fixed physics");
                try { Physics.FixedStep(Time.fixedDeltaTime); }
                catch (Exception e) { Debug.LogError($"Physics.FixedStep threw: {e}"); }
                Profiler.End();
                steps++;
            }
            Time.inFixedTimeStep = false;
            Time.deltaTime = savedDt;
            if (steps >= maxSteps) _fixedAccumulator = 0f;
            FixedStepsLastFrame = steps;
            Profiler.End();

            Profiler.Begin("update");
            RunStarts();
            RunPass(_updaters, 1, "Update");
            Profiler.End();

            Profiler.Begin("coroutines");
            RunCoroutines();
            Profiler.End();

            Profiler.Begin("late update");
            RunPass(_lateUpdaters, 2, "LateUpdate");
            Profiler.End();

            ProcessDestroyQueue();
            Compact();
        }

        /// <summary>FixedUpdate steps run in the last Tick (8 = the clamp hit: the simulation is falling behind real time).</summary>
        public int FixedStepsLastFrame { get; private set; }
        /// <summary>How far the frame is between the last fixed step and the next one (0..1): the interpolation factor for fixed-step motion.</summary>
        public float FixedAlpha => Time.fixedDeltaTime > 0f ? Mathf.Clamp01(_fixedAccumulator / Time.fixedDeltaTime) : 1f;
        public int BehaviourCount => _behaviours.Count;
        public int CoroutineCount => _coroutines.Count;

        private static bool CanRun(Behaviour b) => !b.destroyed && b.started && b.isActiveAndEnabled;

        /// <summary>Awake + OnEnable for every newly added behaviour, then Start for the enabled ones (Unity order).</summary>
        private void RunStarts()
        {
            if (_pendingStart.Count == 0) return;
            // Awake pass (may add more behaviours; loop until stable)
            for (int guard = 0; guard < 8; guard++)
            {
                var toAwake = _pendingStart.FindAll(b => !b.destroyed && !b.awoken && b.gameObject.activeInHierarchy);
                if (toAwake.Count == 0) break;
                foreach (var b in toAwake) b.Initialize();
            }
            // Start pass
            var list = _pendingStart.ToArray();
            _pendingStart.Clear();
            foreach (var b in list)
            {
                if (b.destroyed) continue;
                if (!b.awoken) b.Initialize();
                if (!b.isActiveAndEnabled) { _pendingStart.Add(b); continue; }
                try { b.InternalStart(); }
                catch (Exception e) { Debug.LogError($"Start() of {b.GetType().Name} on '{b.name}' threw: {e}"); }
            }
        }

        // ------------------------------------------------------------------ coroutines

        internal Coroutine StartCoroutine(Behaviour owner, IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            var c = new Coroutine(owner, routine);
            // Unity runs the first step immediately when StartCoroutine is called.
            if (c.Step()) _coroutines.Add(c);
            return c;
        }

        internal void StopCoroutine(Coroutine c)
        {
            if (c == null) return;
            c.finished = true;
            _coroutines.Remove(c);
        }

        internal void StopCoroutine(Behaviour owner, IEnumerator routine)
        {
            for (int i = _coroutines.Count - 1; i >= 0; i--)
                if (_coroutines[i].owner == owner && _coroutines[i].routine == routine)
                {
                    _coroutines[i].finished = true;
                    _coroutines.RemoveAt(i);
                }
        }

        internal void StopAllCoroutines(Behaviour owner)
        {
            for (int i = _coroutines.Count - 1; i >= 0; i--)
                if (_coroutines[i].owner == owner)
                {
                    _coroutines[i].finished = true;
                    _coroutines.RemoveAt(i);
                }
        }

        private void RunCoroutines()
        {
            var snapshot = _coroutines.ToArray();
            foreach (var c in snapshot)
            {
                if (c.finished) { _coroutines.Remove(c); continue; }
                if (c.owner != null && (c.owner.destroyed || !c.owner.gameObject.activeInHierarchy))
                {
                    c.finished = true;
                    _coroutines.Remove(c);
                    continue;
                }
                if (!c.Step()) _coroutines.Remove(c);
            }
        }

        // ------------------------------------------------------------------ destruction

        internal void QueueDestroy(EngineObject obj, float delay)
        {
            _destroyQueue.Add((obj, Time.time + delay));
        }

        private void ProcessDestroyQueue()
        {
            if (_destroyQueue.Count == 0) return;
            for (int i = _destroyQueue.Count - 1; i >= 0; i--)
            {
                var (obj, t) = _destroyQueue[i];
                if (Time.time >= t)
                {
                    _destroyQueue.RemoveAt(i);
                    DestroyNow(obj);
                }
            }
        }

        internal void DestroyNow(EngineObject obj)
        {
            if (obj == null || obj.destroyed) return;
            switch (obj)
            {
                case GameObject go: DestroyGameObject(go); break;
                case Behaviour b:
                    b.destroyed = true;
                    b.enabled = false;
                    b.InvokeOnDestroy();
                    StopAllCoroutines(b);
                    _compact = true;
                    b.gameObject.RemoveComponent(b);
                    break;
                case Component c:
                    c.destroyed = true;
                    c.gameObject.RemoveComponent(c);
                    break;
                default:
                    obj.destroyed = true;
                    break;
            }
        }

        private void DestroyGameObject(GameObject go)
        {
            // children first
            foreach (var child in new List<Transform>(go.transform.Children))
                DestroyGameObject(child.gameObject);

            go.destroyed = true;
            foreach (var c in new List<Component>(go.Components))
            {
                if (c is Behaviour b)
                {
                    b.destroyed = true;
                    if (b.isEnabledAndActiveCached) { b.isEnabledAndActiveCached = false; b.InvokeOnDestroy(); }
                    else b.InvokeOnDestroy();
                    StopAllCoroutines(b);
                }
                else c.destroyed = true;
            }
            go.transform.SetParent(null, false);
            _compact = true;
        }
    }
}
