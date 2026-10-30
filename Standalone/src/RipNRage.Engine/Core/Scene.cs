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
        private readonly List<Behaviour> _pendingStart = new List<Behaviour>();
        private readonly List<Coroutine> _coroutines = new List<Coroutine>();
        private readonly List<(EngineObject obj, float time)> _destroyQueue = new List<(EngineObject, float)>();
        private float _fixedAccumulator;

        public IReadOnlyList<GameObject> GameObjects => _gameObjects;

        public Scene()
        {
            Current = this;
        }

        /// <summary>Called by the engine when a frame starts: drops stale coroutines of a previous scene.</summary>
        internal void ClearAll()
        {
            _coroutines.Clear(); _destroyQueue.Clear(); _pendingStart.Clear(); _behaviours.Clear(); _gameObjects.Clear();
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
            while (_fixedAccumulator >= Time.fixedDeltaTime && steps < 8)
            {
                _fixedAccumulator -= Time.fixedDeltaTime;
                Time.fixedTime += Time.fixedDeltaTime;
                Time.inFixedTimeStep = true;
                Time.deltaTime = Time.fixedDeltaTime;
                RunStarts();
                foreach (var b in SnapshotBehaviours())
                    if (CanRun(b)) SafeCall(b, b.CallFixedUpdate, "FixedUpdate");
                steps++;
            }
            Time.inFixedTimeStep = false;
            Time.deltaTime = savedDt;
            if (steps >= 8) _fixedAccumulator = 0f;
            FixedStepsLastFrame = steps;
            Profiler.End();

            Profiler.Begin("update");
            RunStarts();
            foreach (var b in SnapshotBehaviours())
                if (CanRun(b)) SafeCall(b, b.CallUpdate, "Update");
            Profiler.End();

            Profiler.Begin("coroutines");
            RunCoroutines();
            Profiler.End();

            Profiler.Begin("late update");
            foreach (var b in SnapshotBehaviours())
                if (CanRun(b)) SafeCall(b, b.CallLateUpdate, "LateUpdate");
            Profiler.End();

            ProcessDestroyQueue();
        }

        /// <summary>FixedUpdate steps run in the last Tick (8 = the clamp hit: the simulation is falling behind real time).</summary>
        public int FixedStepsLastFrame { get; private set; }
        public int BehaviourCount => _behaviours.Count;
        public int CoroutineCount => _coroutines.Count;

        private Behaviour[] SnapshotBehaviours() => _behaviours.ToArray();

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

        private static void SafeCall(Behaviour b, Action a, string what)
        {
            try { a(); }
            catch (Exception e) { Debug.LogError($"{what}() of {b.GetType().Name} on '{b.name}' threw: {e}"); }
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
                    _behaviours.Remove(b);
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
                    _behaviours.Remove(b);
                    _pendingStart.Remove(b);
                }
                else c.destroyed = true;
            }
            go.transform.SetParent(null, false);
            _gameObjects.Remove(go);
        }
    }
}
