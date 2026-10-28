using System.Collections;

namespace RipNRage.Engine
{
    /// <summary>
    /// Replacement for UnityEngine.MonoBehaviour. Lifecycle: Awake (when added to an active object),
    /// OnEnable, Start (before the first Update), Update / FixedUpdate / LateUpdate, OnDisable, OnDestroy.
    /// Coroutines have Unity semantics (resume after Update; WaitForSeconds / WaitUntil / null).
    /// </summary>
    public abstract class Behaviour : Component
    {
        private bool _enabled = true;
        internal bool awoken;
        internal bool started;
        internal bool isEnabledAndActiveCached;

        public bool enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                RefreshEnabledState();
            }
        }

        public bool isActiveAndEnabled => _enabled && gameObject.activeInHierarchy && !destroyed;

        /// <summary>
        /// Registration only: like a Unity prefab/scene object, Awake and OnEnable run in the next
        /// lifecycle pass after the creator has filled in the component's fields.
        /// </summary>
        internal void OnAdded()
        {
            Scene.Current?.RegisterBehaviour(this);
        }

        /// <summary>Runs Awake (once) and OnEnable if the object is active. Called by the scene.</summary>
        internal void Initialize()
        {
            if (!gameObject.activeInHierarchy) return;
            EnsureAwake();
            RefreshEnabledState();
        }

        internal void EnsureAwake()
        {
            if (awoken) return;
            awoken = true;
            try { Awake(); }
            catch (System.Exception e) { Debug.LogError($"Awake() of {GetType().Name} on '{name}' threw: {e}"); }
        }

        internal void OnHierarchyActiveChanged(bool active)
        {
            if (active && !awoken) return; // not initialised yet: the scene will Awake/OnEnable it
            RefreshEnabledState();
        }

        private void RefreshEnabledState()
        {
            bool now = isActiveAndEnabled;
            if (now == isEnabledAndActiveCached) return;
            isEnabledAndActiveCached = now;
            if (now)
            {
                try { OnEnable(); }
                catch (System.Exception e) { Debug.LogError($"OnEnable() of {GetType().Name} threw: {e}"); }
            }
            else
            {
                try { OnDisable(); }
                catch (System.Exception e) { Debug.LogError($"OnDisable() of {GetType().Name} threw: {e}"); }
                // Unity stops coroutines when the owning GameObject is deactivated (not when only the behaviour is disabled).
                if (!gameObject.activeInHierarchy) Scene.Current?.StopAllCoroutines(this);
            }
        }

        internal void InternalStart()
        {
            if (started) return;
            started = true;
            Start();
        }

        protected virtual void Awake() { }
        protected virtual void Start() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }
        protected virtual void Update() { }
        protected virtual void FixedUpdate() { }
        protected virtual void LateUpdate() { }

        internal void CallUpdate() => Update();
        internal void CallFixedUpdate() => FixedUpdate();
        internal void CallLateUpdate() => LateUpdate();

        internal void InvokeOnDestroy()
        {
            try { OnDestroy(); }
            catch (System.Exception e) { Debug.LogError($"OnDestroy() of {GetType().Name} threw: {e}"); }
        }

        public Coroutine StartCoroutine(IEnumerator routine) => Scene.Current.StartCoroutine(this, routine);

        public void StopCoroutine(Coroutine routine) => Scene.Current?.StopCoroutine(routine);

        public void StopCoroutine(IEnumerator routine) => Scene.Current?.StopCoroutine(this, routine);

        public void StopAllCoroutines() => Scene.Current?.StopAllCoroutines(this);

        public void Invoke(string methodName, float delay)
        {
            StartCoroutine(InvokeRoutine(methodName, delay));
        }

        private IEnumerator InvokeRoutine(string methodName, float delay)
        {
            yield return new WaitForSeconds(delay);
            gameObject.SendMessage(methodName);
        }
    }
}
