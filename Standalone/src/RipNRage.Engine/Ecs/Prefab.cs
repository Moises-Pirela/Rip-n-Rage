using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// An entity template: a list of component configs. JSON form:
    /// <code>
    /// { "name": "rifleman", "base": "enemy",
    ///   "components": { "Health": { "max": 60 }, "EnemyBrain": { "sight": 25 } } }
    /// </code>
    /// Keys name a config class or the component it bakes. "base" inherits another prefab's components (fields of
    /// a component given in both are merged, the child winning).
    /// </summary>
    public sealed class Prefab
    {
        public string Name;
        public readonly List<IComponentConfig> Components = new List<IComponentConfig>();

        public Entity Instantiate(World world) => Instantiate(world.Commands, world);

        /// <summary>Spawns through a command buffer (use the system's own <c>ctx.Commands</c> inside systems).</summary>
        public Entity Instantiate(CommandBuffer commands, World world)
        {
            var e = commands.CreateEntity();
            foreach (var c in Components) c.BakeInto(commands, e);
            return e;
        }

        public T Get<T>() where T : class, IComponentConfig => Components.Find(c => c is T) as T;
    }

    /// <summary>Loads and caches JSON prefabs from a folder; resolves inheritance.</summary>
    public sealed class PrefabLibrary
    {
        private readonly Dictionary<string, JsonObject> _raw = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Prefab> _built = new Dictionary<string, Prefab>(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, // "Point" as well as 1
        };

        public IEnumerable<string> Names => _raw.Keys;

        public void LoadDirectory(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)) AddJson(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f));
        }

        public void AddJson(string json, string fallbackName = null)
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            string name = (string)node["name"] ?? fallbackName ?? throw new InvalidDataException("prefab without a name");
            _raw[name] = node; _built.Remove(name);
        }

        public bool Contains(string name) => _raw.ContainsKey(name);

        public Prefab Get(string name)
        {
            if (_built.TryGetValue(name, out var p)) return p;
            var merged = Resolve(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            p = new Prefab { Name = name };
            foreach (var kv in merged)
            {
                var cfgType = ComponentRegistry.FindConfig(kv.Key) ?? throw new InvalidDataException($"prefab '{name}': no IComponentConfig for '{kv.Key}'");
                p.Components.Add((IComponentConfig)kv.Value.Deserialize(cfgType, Options));
            }
            return _built[name] = p;
        }

        private Dictionary<string, JsonObject> Resolve(string name, HashSet<string> visiting)
        {
            if (!_raw.TryGetValue(name, out var node)) throw new KeyNotFoundException($"prefab '{name}' not found");
            if (!visiting.Add(name)) throw new InvalidDataException($"prefab inheritance cycle at '{name}'");
            var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            if ((string)node["base"] is string b)
                foreach (var kv in Resolve(b, visiting)) result[kv.Key] = (JsonObject)kv.Value.DeepClone();
            if (node["components"] is JsonObject comps)
                foreach (var kv in comps)
                {
                    var child = kv.Value as JsonObject ?? new JsonObject();
                    if (!result.TryGetValue(kv.Key, out var parent)) { result[kv.Key] = (JsonObject)child.DeepClone(); continue; }
                    foreach (var f in child) parent[f.Key] = f.Value?.DeepClone(); // field-level override
                }
            visiting.Remove(name);
            return result;
        }
    }
}
