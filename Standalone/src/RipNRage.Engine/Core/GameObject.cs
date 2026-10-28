using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>Base of everything that lives in a scene (mirrors UnityEngine.Object's tiny surface).</summary>
    public abstract class EngineObject
    {
        private static int _nextId = 1;
        public int instanceId { get; } = _nextId++;
        public string name { get; set; } = "";
        internal bool destroyed;
        /// <summary>True once Destroy() has run for this object.</summary>
        public bool IsDestroyed => destroyed;

        public override string ToString() => $"{GetType().Name}({name})";

        /// <summary>Unity-style null check: destroyed objects compare equal to null.</summary>
        public static bool IsAlive(EngineObject o) => o != null && !o.destroyed;
    }

    public abstract class Component : EngineObject
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public string tag { get => gameObject.tag; set => gameObject.tag = value; }

        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : class => gameObject.GetComponentInChildren<T>();
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
        public T[] GetComponents<T>() where T : class => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>() where T : class => gameObject.GetComponentsInChildren<T>();
        public bool CompareTag(string t) => gameObject.CompareTag(t);
    }

    /// <summary>A node in the scene: a Transform plus a list of components.</summary>
    public sealed class GameObject : EngineObject
    {
        private readonly List<Component> _components = new List<Component>();
        private bool _activeSelf = true;

        public Transform transform { get; }
        public string tag { get; set; } = "Untagged";
        public int layer { get; set; } = 0;
        public Scene scene { get; internal set; }

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            transform = new Transform(this);
            _components.Add(transform);
            Scene.Current?.Register(this);
        }

        public GameObject(string name, params Type[] components) : this(name)
        {
            foreach (var t in components) AddComponent(t);
        }

        public bool activeSelf => _activeSelf;

        public bool activeInHierarchy
        {
            get
            {
                if (!_activeSelf || destroyed) return false;
                var p = transform.parent;
                return p == null || p.gameObject.activeInHierarchy;
            }
        }

        public void SetActive(bool value)
        {
            if (_activeSelf == value) return;
            bool wasActive = activeInHierarchy;
            _activeSelf = value;
            bool isActive = activeInHierarchy;
            if (wasActive != isActive) PropagateActiveChange(isActive);
        }

        internal void PropagateActiveChange(bool active)
        {
            foreach (var c in _components.ToArray())
                if (c is Behaviour b) b.OnHierarchyActiveChanged(active);
            foreach (var child in transform.Children)
                if (child.gameObject._activeSelf) child.gameObject.PropagateActiveChange(active);
        }

        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));

        public Component AddComponent(Type type)
        {
            var c = (Component)Activator.CreateInstance(type, nonPublic: true);
            c.gameObject = this;
            c.name = name;
            _components.Add(c);
            if (c is Behaviour b) b.OnAdded();
            return c;
        }

        /// <summary>Registers an already-constructed component (used by the engine for special components).</summary>
        internal void AttachComponent(Component c)
        {
            c.gameObject = this;
            c.name = name;
            _components.Add(c);
            if (c is Behaviour b) b.OnAdded();
        }

        public T GetComponent<T>() where T : class
        {
            foreach (var c in _components)
                if (c is T t && !c.destroyed) return t;
            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }

        public T[] GetComponents<T>() where T : class
        {
            var list = new List<T>();
            foreach (var c in _components)
                if (c is T t && !c.destroyed) list.Add(t);
            return list.ToArray();
        }

        public T GetComponentInChildren<T>() where T : class
        {
            var own = GetComponent<T>();
            if (own != null) return own;
            foreach (var child in transform.Children)
            {
                var r = child.gameObject.GetComponentInChildren<T>();
                if (r != null) return r;
            }
            return null;
        }

        public T[] GetComponentsInChildren<T>() where T : class
        {
            var list = new List<T>();
            CollectInChildren(list);
            return list.ToArray();
        }

        private void CollectInChildren<T>(List<T> list) where T : class
        {
            list.AddRange(GetComponents<T>());
            foreach (var child in transform.Children) child.gameObject.CollectInChildren(list);
        }

        public T GetComponentInParent<T>() where T : class
        {
            for (var t = transform; t != null; t = t.parent)
            {
                var c = t.gameObject.GetComponent<T>();
                if (c != null) return c;
            }
            return null;
        }

        public bool CompareTag(string t) => tag == t;

        internal IReadOnlyList<Component> Components => _components;

        internal void RemoveComponent(Component c) => _components.Remove(c);

        /// <summary>Unity's SendMessage: invokes a parameterless method by name on every behaviour of this object.</summary>
        /// <summary>Components implementing this receive every SendMessage regardless of name (editors, loggers).</summary>
        public interface IMessageSink { void OnMessage(string methodName, object value); }

        public void SendMessage(string methodName, object value = null, bool requireReceiver = false)
        {
            bool any = false;
            foreach (var c in _components) if (c is IMessageSink sink) { sink.OnMessage(methodName, value); any = true; }
            foreach (var c in _components.ToArray())
            {
                if (!(c is Behaviour)) continue;
                var m = c.GetType().GetMethod(methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m == null) continue;
                var ps = m.GetParameters();
                if (ps.Length == 0) { m.Invoke(c, null); any = true; }
                else if (ps.Length == 1) { m.Invoke(c, new[] { value }); any = true; }
            }
            if (!any && requireReceiver) Debug.LogWarning($"SendMessage {methodName} has no receiver on {name}");
        }

        // ---- static helpers that mirror UnityEngine.Object ----

        public static void Destroy(EngineObject obj, float delay = 0f)
        {
            if (obj == null) return;
            Scene.Current?.QueueDestroy(obj, delay);
        }

        public static void DestroyImmediate(EngineObject obj)
        {
            if (obj == null) return;
            Scene.Current?.DestroyNow(obj);
        }

        public static GameObject Find(string name) => Scene.Current?.Find(name);
    }
}
