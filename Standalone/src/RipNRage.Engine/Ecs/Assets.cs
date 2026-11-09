using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// A plain-data reference to a shared asset in an <see cref="AssetRegistry{T}"/>. Components store handles
    /// instead of object references so they stay blittable and serialisable; systems resolve them at use sites.
    /// Id 0 is "none" (default), so a default-constructed component has no asset.
    /// </summary>
    public readonly struct AssetHandle<T> : IEquatable<AssetHandle<T>> where T : class
    {
        public readonly int Id;
        public AssetHandle(int id) { Id = id; }
        public bool IsNone => Id == 0;
        public static AssetHandle<T> None => default;
        public bool Equals(AssetHandle<T> other) => Id == other.Id;
        public override bool Equals(object obj) => obj is AssetHandle<T> h && h.Id == Id;
        public override int GetHashCode() => Id;
        public override string ToString() => Id == 0 ? $"{typeof(T).Name}:none" : $"{typeof(T).Name}:{Id}";
    }

    /// <summary>
    /// Maps handles to shared asset objects. Entries are interned by reference and may carry a name — the name a
    /// config baked from — so a handle can be turned back into config data (save / load, editors). Thread-safe for
    /// concurrent resolve; interning takes a lock (configs bake on the main thread, systems only resolve).
    /// </summary>
    public sealed class AssetRegistry<T> where T : class
    {
        private readonly List<T> _items = new List<T> { null };          // index 0 = none
        private readonly List<string> _names = new List<string> { null };
        private readonly Dictionary<T, int> _byRef = new Dictionary<T, int>(ReferenceEqualityComparer<T>.Instance);
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        /// <summary>Intern an asset (optionally under a name) and get its handle; the same reference always returns the same handle.</summary>
        public AssetHandle<T> Intern(T item, string name = null)
        {
            if (item == null) return AssetHandle<T>.None;
            lock (_gate)
            {
                if (_byRef.TryGetValue(item, out int id))
                {
                    if (name != null && _names[id] == null) { _names[id] = name; _byName[name] = id; }
                    return new AssetHandle<T>(id);
                }
                id = _items.Count;
                _items.Add(item); _names.Add(name);
                _byRef[item] = id;
                if (name != null) _byName[name] = id;
                return new AssetHandle<T>(id);
            }
        }

        /// <summary>The handle previously interned under <paramref name="name"/>, or None.</summary>
        public AssetHandle<T> Find(string name)
            { lock (_gate) return _byName.TryGetValue(name, out int id) ? new AssetHandle<T>(id) : AssetHandle<T>.None; }

        /// <summary>Resolve by name, loading and interning on first use.</summary>
        public AssetHandle<T> GetOrLoad(string name, Func<string, T> loader)
        {
            if (string.IsNullOrEmpty(name)) return AssetHandle<T>.None;
            lock (_gate) if (_byName.TryGetValue(name, out int id)) return new AssetHandle<T>(id);
            return Intern(loader(name), name);
        }

        /// <summary>The asset behind a handle (null for None). Lock-free: entries are append-only.</summary>
        public T Resolve(AssetHandle<T> handle)
        {
            int id = handle.Id;
            return (uint)id < (uint)_items.Count ? _items[id] : null;
        }

        /// <summary>The name an asset was interned under (null if interned anonymously). For serialisation.</summary>
        public string NameOf(AssetHandle<T> handle)
            { lock (_gate) return (uint)handle.Id < (uint)_names.Count ? _names[handle.Id] : null; }

        private sealed class ReferenceEqualityComparer<TK> : IEqualityComparer<TK> where TK : class
        {
            public static readonly ReferenceEqualityComparer<TK> Instance = new ReferenceEqualityComparer<TK>();
            public bool Equals(TK a, TK b) => ReferenceEquals(a, b);
            public int GetHashCode(TK obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
