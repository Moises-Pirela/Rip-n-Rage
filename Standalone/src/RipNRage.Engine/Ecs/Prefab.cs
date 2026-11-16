using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs
{
    /// <summary>What a prefab spawn made: the root and every named entity of the tree (<c>root</c> included).</summary>
    public sealed class PrefabInstance
    {
        public Entity Root;
        /// <summary>Every entity of the tree, root first, children in document order.</summary>
        public readonly List<Entity> All = new List<Entity>();
        private readonly Dictionary<string, Entity> _named = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        public Entity this[string name] => Named(name);
        /// <summary>The entity called <paramref name="name"/> (<c>root</c> or a child), or Null.</summary>
        public Entity Named(string name) => name != null && _named.TryGetValue(name, out var e) ? e : Entity.Null;
        public bool TryGet(string name, out Entity entity) => _named.TryGetValue(name ?? "", out entity);
        public IReadOnlyDictionary<string, Entity> Names => _named;

        internal void Add(string name, Entity e)
        {
            All.Add(e);
            if (name != null) _named[name] = e;
        }
    }

    /// <summary>
    /// An entity template: a tree of nodes, each a list of component configs (10.1: trees, references, overrides).
    /// JSON form:
    /// <code>
    /// { "name": "rifleman", "base": "enemy", "description": "AR grunt",
    ///   "keys": { "hp": "Health.max" },
    ///   "components": { "Health": { "max": 60 }, "EnemyVisual": { "sprite": "sprite" } },
    ///   "children": { "sprite": { "components": { "LocalTransform": { "position": [0, 1.1, 0] }, "SpriteRender": { ... } } } } }
    /// </code>
    /// Component keys name a config class or the component it bakes. <c>base</c> inherits another prefab's tree
    /// (fields of a component given in both are merged, the child winning; children merge by name). Children are
    /// parented to the node above and get an identity <see cref="LocalTransform"/> when they declare none. A config
    /// field of type <see cref="EntityRef"/> names a child, <c>root</c>, or an entity the spawner resolves.
    /// <c>keys</c> are the short names a <c>.map</c> may use for field paths (<c>Config.field</c>, or
    /// <c>child/Config.field</c> for a child's).
    /// </summary>
    public sealed class Prefab
    {
        /// <summary>The prefab's name; for a child node, the child's name.</summary>
        public string Name;
        public string Description;
        public readonly List<IComponentConfig> Components = new List<IComponentConfig>();
        public readonly List<Prefab> Children = new List<Prefab>();
        /// <summary>Short key → field path (root node only).</summary>
        public readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public T Get<T>() where T : class, IComponentConfig => Components.Find(c => c is T) as T;
        public Prefab Child(string name) => Children.Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ spawning

        /// <summary>The root entity, created through the world's command buffer (alive after the next sync).</summary>
        public Entity Instantiate(World world) => Instantiate(world.Commands, world);

        /// <summary>The root entity, created through <paramref name="commands"/> (use the system's own <c>ctx.Commands</c> inside systems).</summary>
        public Entity Instantiate(CommandBuffer commands, World world) => Build(world, commands, Vector3.zero, Quaternion.identity, null, preview: false, posed: false).Root;

        /// <summary>Spawns the tree through <paramref name="commands"/> at a pose (inside systems). Entities are reserved handles until playback.</summary>
        public PrefabInstance Instantiate(CommandBuffer commands, World world, Vector3 position, Quaternion rotation, Func<string, Entity> named = null) =>
            Build(world, commands, position, rotation, named, preview: false, posed: true);

        /// <summary>Spawns the tree now (a build: outside the systems), at the prefab's own pose.</summary>
        public PrefabInstance Spawn(World world) => Build(world, null, Vector3.zero, Quaternion.identity, null, preview: false, posed: false);

        /// <summary>
        /// Spawns the tree now at a pose: the root's configured <see cref="LocalTransform"/> is composed with it (the
        /// prefab's position is an offset above the placed origin, its rotation adds to the yaw). <paramref name="named"/>
        /// resolves <see cref="EntityRef"/>s the tree does not (a level's targetnames).
        /// </summary>
        public PrefabInstance Spawn(World world, Vector3 position, Quaternion rotation, Func<string, Entity> named = null) =>
            Build(world, null, position, rotation, named, preview: false, posed: true);

        /// <summary>The editor's preview: the same tree without the components marked <see cref="NotInPreviewAttribute"/>.</summary>
        public PrefabInstance SpawnPreview(World world, Vector3 position, Quaternion rotation, Func<string, Entity> named = null) =>
            Build(world, null, position, rotation, named, preview: true, posed: true);

        private PrefabInstance Build(World world, CommandBuffer commands, Vector3 position, Quaternion rotation, Func<string, Entity> external, bool preview, bool posed)
        {
            var inst = new PrefabInstance();
            var nodes = new List<(Prefab node, Entity entity, Entity parent)>();
            Create(this, Entity.Null, true);
            inst.Root = nodes[0].entity;
            inst.Add("root", inst.Root);

            Func<string, Entity> lookup = n => inst.TryGet(n, out var e) ? e : external?.Invoke(n) ?? Entity.Null;
            foreach (var (node, entity, parent) in nodes)
            {
                bool isRoot = parent.IsNull;
                var ctx = new BakeContext(entity, inst.Root, position, rotation, lookup, Name);
                bool hasTransform = false;
                foreach (var cfg in node.Components)
                {
                    if (preview && ComponentRegistry.Get(cfg.ComponentType).NotInPreview) continue;
                    if (cfg is LocalTransformConfig ltc)
                    {
                        hasTransform = true;
                        var t = ltc.Bake();
                        if (isRoot && posed) { t.Position = position + rotation * t.Position; t.Rotation = rotation * t.Rotation; }
                        Add(entity, t);
                        continue;
                    }
                    if (commands != null) cfg.BakeInto(commands, entity, in ctx); else cfg.BakeInto(world, entity, in ctx);
                }
                if (!hasTransform && (!isRoot || posed))
                    Add(entity, isRoot ? LocalTransform.At(position, rotation) : LocalTransform.Identity);
                if (!isRoot) Add(entity, new Parent { Value = parent });
            }
            return inst;

            void Create(Prefab node, Entity parent, bool isRoot)
            {
                var e = commands != null ? commands.CreateEntity() : world.CreateEntity();
                if (!isRoot)
                {
                    if (string.IsNullOrEmpty(node.Name)) throw new InvalidDataException($"prefab '{Name}': a child without a name");
                    if (node.Name.Equals("root", StringComparison.OrdinalIgnoreCase) || node.Name.Equals("self", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"prefab '{Name}': '{node.Name}' is a reserved name");
                    if (inst.TryGet(node.Name, out _)) throw new InvalidDataException($"prefab '{Name}': two children named '{node.Name}'");
                    inst.Add(node.Name, e);
                }
                nodes.Add((node, e, parent));
                foreach (var c in node.Children) Create(c, e, false);
            }

            void Add<T>(Entity e, in T value) where T : struct, IComponent
            {
                if (commands != null) commands.Add(e, value); else world.Add(e, value);
            }
        }
    }

    /// <summary>Loads and caches JSON prefabs from a folder; resolves inheritance, applies overrides.</summary>
    public sealed class PrefabLibrary
    {
        private sealed class Node
        {
            public readonly Dictionary<string, JsonObject> Components = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> ChildOrder = new List<string>();
            public readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Description;

            public Node Clone()
            {
                var n = new Node { Description = Description };
                foreach (var kv in Components) n.Components[kv.Key] = (JsonObject)kv.Value.DeepClone();
                foreach (var name in ChildOrder) { n.Children[name] = Children[name].Clone(); n.ChildOrder.Add(name); }
                foreach (var kv in Keys) n.Keys[kv.Key] = kv.Value;
                return n;
            }
        }

        private readonly Dictionary<string, JsonObject> _raw = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Prefab> _built = new Dictionary<string, Prefab>(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(), new EntityRefConverter() }, // "Point" as well as 1; "sprite" → EntityRef
        };
        private static readonly JsonDocumentOptions DocOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        public IEnumerable<string> Names => _raw.Keys;

        /// <summary>Loads every <c>*.json</c> under <paramref name="dir"/>. A name defined in two files throws (names are global).</summary>
        public void LoadDirectory(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                AddJson(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), f);
        }

        /// <summary>Adds (or replaces) a prefab from its JSON text.</summary>
        public void AddJson(string json, string fallbackName = null, string sourceFile = null)
        {
            var node = JsonNode.Parse(json, documentOptions: DocOptions)!.AsObject();
            string name = (string)node["name"] ?? fallbackName ?? throw new InvalidDataException("prefab without a name");
            if (sourceFile != null && _source.TryGetValue(name, out var other) && !string.Equals(other, sourceFile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"prefab '{name}' is defined twice: {other} and {sourceFile}");
            _raw[name] = node;
            if (sourceFile != null) _source[name] = sourceFile;
            _built.Clear(); // a base may have changed
        }

        public bool Contains(string name) => _raw.ContainsKey(name);

        /// <summary>The prefab as its file (and its bases) define it. Cached.</summary>
        public Prefab Get(string name)
        {
            if (_built.TryGetValue(name, out var p)) return p;
            return _built[name] = Build(Resolve(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), name, name);
        }

        /// <summary>
        /// The prefab with field overrides applied last: each key is a short name from the prefab's <c>keys</c> or a
        /// path (<c>Config.field</c>, <c>child/Config.field</c>); each value is the field's text form (<c>"1 2 3"</c>
        /// for a vector, an enum by name, <c>0</c> / <c>1</c> for a bool). An unknown config, field or child throws
        /// naming the prefab and the key. Not cached.
        /// </summary>
        public Prefab Get(string name, IEnumerable<KeyValuePair<string, string>> overrides)
        {
            var node = Resolve(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Clone();
            foreach (var kv in overrides)
            {
                string path = ResolvePath(node, kv.Key) ?? throw new InvalidDataException($"prefab '{name}': key '{kv.Key}' is neither an alias nor a Config.field path");
                ApplyOverride(node, path, kv.Value, name, kv.Key);
            }
            return Build(node, name, name);
        }

        /// <summary>The field path a key addresses on <paramref name="prefabName"/> (its alias, or the key itself when it is
        /// already a path), or null when it is neither — the caller's own key, then.</summary>
        public string ResolveKey(string prefabName, string key)
        {
            var node = Resolve(prefabName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return ResolvePath(node, key);
        }

        private static string ResolvePath(Node node, string key)
        {
            if (node.Keys.TryGetValue(key, out var alias)) return alias;
            return key.Contains('.') ? key : null;
        }

        // ------------------------------------------------------------------ inheritance

        private Node Resolve(string name, HashSet<string> visiting)
        {
            if (!_raw.TryGetValue(name, out var json)) throw new KeyNotFoundException($"prefab '{name}' not found");
            if (!visiting.Add(name)) throw new InvalidDataException($"prefab inheritance cycle at '{name}'");
            Node result = (string)json["base"] is string b ? Resolve(b, visiting).Clone() : new Node();
            MergeInto(result, json, name);
            visiting.Remove(name);
            return result;
        }

        /// <summary>Merges a JSON node body (components, children, description, keys) over <paramref name="target"/>: fields of
        /// a component given in both are overridden field by field, children merge by name.</summary>
        private static void MergeInto(Node target, JsonObject json, string prefabName)
        {
            if (json["description"] is JsonValue d) target.Description = (string)d;
            if (json["keys"] is JsonObject keys)
                foreach (var kv in keys) target.Keys[kv.Key] = (string)kv.Value;
            if (json["components"] is JsonObject comps)
                foreach (var kv in comps)
                {
                    var child = kv.Value as JsonObject ?? new JsonObject();
                    if (!target.Components.TryGetValue(kv.Key, out var parent)) { target.Components[kv.Key] = (JsonObject)child.DeepClone(); continue; }
                    foreach (var f in child) parent[f.Key] = f.Value?.DeepClone(); // field-level override
                }
            if (json["children"] is JsonObject children)
                foreach (var kv in children)
                {
                    var body = kv.Value as JsonObject ?? throw new InvalidDataException($"prefab '{prefabName}': child '{kv.Key}' must be an object");
                    if (!target.Children.TryGetValue(kv.Key, out var childNode)) { target.Children[kv.Key] = childNode = new Node(); target.ChildOrder.Add(kv.Key); }
                    MergeInto(childNode, body, prefabName);
                }
        }

        private static Prefab Build(Node node, string name, string prefabName)
        {
            var p = new Prefab { Name = name, Description = node.Description };
            foreach (var kv in node.Keys) p.Keys[kv.Key] = kv.Value;
            foreach (var kv in node.Components)
            {
                var cfgType = ComponentRegistry.FindConfig(kv.Key) ?? throw new InvalidDataException($"prefab '{prefabName}': no IComponentConfig for '{kv.Key}'");
                try { p.Components.Add((IComponentConfig)kv.Value.Deserialize(cfgType, Options)); }
                catch (JsonException e) { throw new InvalidDataException($"prefab '{prefabName}': bad '{kv.Key}': {e.Message}", e); }
            }
            foreach (var childName in node.ChildOrder) p.Children.Add(Build(node.Children[childName], childName, prefabName));
            return p;
        }

        // ------------------------------------------------------------------ overrides

        private static void ApplyOverride(Node root, string path, string value, string prefabName, string key)
        {
            var segs = path.Split('/');
            var node = root;
            for (int i = 0; i < segs.Length - 1; i++)
                if (!node.Children.TryGetValue(segs[i], out node)) throw new InvalidDataException($"prefab '{prefabName}': key '{key}': no child '{segs[i]}'");
            string last = segs[segs.Length - 1];
            int dot = last.LastIndexOf('.');
            if (dot <= 0 || dot == last.Length - 1) throw new InvalidDataException($"prefab '{prefabName}': key '{key}': '{last}' is not Config.field");
            string cfgName = last.Substring(0, dot), fieldName = last.Substring(dot + 1);
            var cfgType = ComponentRegistry.FindConfig(cfgName) ?? throw new InvalidDataException($"prefab '{prefabName}': key '{key}': no config '{cfgName}'");
            var field = cfgType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?? throw new InvalidDataException($"prefab '{prefabName}': key '{key}': {cfgType.Name} has no field '{fieldName}'");
            JsonNode converted;
            try { converted = ConvertValue(field.FieldType, value); }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            { throw new InvalidDataException($"prefab '{prefabName}': key '{key}': '{value}' is not a {Pretty(field.FieldType)}", e); }

            // the component may be keyed by the config's or the component's name already; else key it by the config
            string compKey = null;
            foreach (var k in node.Components.Keys) if (ComponentRegistry.FindConfig(k) == cfgType) { compKey = k; break; }
            if (compKey == null) node.Components[compKey = cfgType.Name] = new JsonObject();
            node.Components[compKey][field.Name] = converted;
        }

        /// <summary>A field's text form → JSON, by its type: numbers, bools (0 / 1 / true / false), enums by name, strings,
        /// <see cref="EntityRef"/>s, numeric arrays ("1 2 3"), nullable numbers ("null" clears).</summary>
        internal static JsonNode ConvertValue(Type type, string value)
        {
            value = value?.Trim() ?? "";
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (value.Length == 0 || value.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
                type = underlying;
            }
            if (type == typeof(float) || type == typeof(double)) return JsonValue.Create(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return JsonValue.Create(long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));
            if (type == typeof(bool))
            {
                if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
                if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);
                throw new FormatException("not a bool");
            }
            if (type == typeof(string) || type == typeof(EntityRef)) return JsonValue.Create(value);
            if (type.IsEnum) return JsonValue.Create(Enum.Parse(type, value, ignoreCase: true).ToString());
            if (type == typeof(float[]) || type == typeof(double[]) || type == typeof(int[]))
            {
                var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var arr = new JsonArray();
                foreach (var p in parts)
                    arr.Add(type == typeof(int[]) ? JsonValue.Create(long.Parse(p, NumberStyles.Integer, CultureInfo.InvariantCulture))
                                                  : JsonValue.Create(double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)));
                return arr;
            }
            throw new ArgumentException($"fields of type {Pretty(type)} cannot be overridden from text");
        }

        private static string Pretty(Type t) => Nullable.GetUnderlyingType(t) is Type u ? Pretty(u) + "?" : t.IsArray ? Pretty(t.GetElementType()) + "[]" : t.Name;

        private sealed class EntityRefConverter : JsonConverter<EntityRef>
        {
            public override EntityRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType == JsonTokenType.Null ? default : new EntityRef(reader.GetString());
            public override void Write(Utf8JsonWriter writer, EntityRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.name ?? "");
        }
    }
}
