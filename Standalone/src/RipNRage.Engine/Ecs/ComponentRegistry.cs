using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RipNRage.Engine.Ecs
{
    /// <summary>Static facts about one component type: its signature bit and storage kind.</summary>
    public sealed class ComponentInfo
    {
        public Type Type;
        public int Id;            // the signature bit
        public bool Sparse;
        public Type ConfigType;   // the IComponentConfig that bakes it (null = runtime-only)
        public override string ToString() => $"{Type.Name}#{Id}{(Sparse ? " sparse" : "")}";
    }

    /// <summary>
    /// Discovers every <see cref="IComponent"/> struct and <see cref="IComponentConfig"/> class by reflection and
    /// assigns signature bits. Ids are assigned in a stable order (sorted by full type name) so they are identical
    /// across runs and machines. Types can also register on first use (assemblies loaded later).
    /// </summary>
    public static class ComponentRegistry
    {
        private static readonly Dictionary<Type, ComponentInfo> _byType = new Dictionary<Type, ComponentInfo>();
        private static readonly List<ComponentInfo> _byId = new List<ComponentInfo>();
        private static readonly Dictionary<string, Type> _configsByName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static bool _scanned;

        public static IReadOnlyList<ComponentInfo> All { get { EnsureScanned(); return _byId; } }

        /// <summary>Scans all loaded assemblies (call again after loading plugins; already known types keep their ids).</summary>
        public static void Scan(IEnumerable<Assembly> assemblies = null)
        {
            lock (_lock)
            {
                _scanned = true;
                var asms = assemblies ?? AppDomain.CurrentDomain.GetAssemblies();
                var comps = new List<Type>(); var configs = new List<Type>();
                foreach (var asm in asms)
                {
                    if (asm.IsDynamic) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var t in types)
                    {
                        if (t.IsValueType && !t.IsGenericTypeDefinition && typeof(IComponent).IsAssignableFrom(t)) comps.Add(t);
                        else if (t.IsClass && !t.IsAbstract && typeof(IComponentConfig).IsAssignableFrom(t)) configs.Add(t);
                    }
                }
                foreach (var t in comps.OrderBy(t => t.FullName, StringComparer.Ordinal)) RegisterLocked(t);
                foreach (var c in configs)
                {
                    var cfg = (IComponentConfig)Activator.CreateInstance(c);
                    var info = RegisterLocked(cfg.ComponentType);
                    if (info.ConfigType != null && info.ConfigType != c)
                        throw new InvalidOperationException($"{info.Type.Name} has two configs ({info.ConfigType.Name}, {c.Name}): one config per component");
                    info.ConfigType = c;
                    _configsByName[c.Name] = c;
                    _configsByName[info.Type.Name] = c; // prefabs may name either the config or the component
                }
            }
        }

        private static void EnsureScanned() { if (!_scanned) Scan(); }

        private static ComponentInfo RegisterLocked(Type t)
        {
            if (_byType.TryGetValue(t, out var info)) return info;
            if (_byId.Count >= Signature.Capacity) throw new InvalidOperationException($"more than {Signature.Capacity} component types");
            info = new ComponentInfo { Type = t, Id = _byId.Count, Sparse = t.GetCustomAttribute<SparseComponentAttribute>() != null };
            _byType[t] = info; _byId.Add(info);
            return info;
        }

        public static ComponentInfo Get(Type t)
        {
            EnsureScanned();
            lock (_lock) return RegisterLocked(t);
        }

        public static ComponentInfo Get(int id) => _byId[id];

        /// <summary>The config class for a prefab key (config or component type name).</summary>
        public static Type FindConfig(string name) { EnsureScanned(); return _configsByName.TryGetValue(name, out var t) ? t : null; }
    }

    /// <summary>Per-type cached id: <c>ComponentType&lt;Health&gt;.Id</c> costs a static field read.</summary>
    public static class ComponentType<T> where T : struct, IComponent
    {
        public static readonly ComponentInfo Info = ComponentRegistry.Get(typeof(T));
        public static readonly int Id = Info.Id;
        public static readonly bool Sparse = Info.Sparse;
    }
}
