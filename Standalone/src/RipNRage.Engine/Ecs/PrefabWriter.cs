using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// Writes a <see cref="Prefab"/> back to the JSON the library reads (10.6: the editor's prefab window). A
    /// component writes only the fields that differ from its reference — the same component on the prefab's
    /// <c>base</c> when it has one, else a fresh config's defaults — so a file stays as short as a hand would have
    /// kept it, and inheritance is preserved rather than flattened. Comments in the original file are not kept: a
    /// saved prefab is regenerated.
    /// </summary>
    public static class PrefabWriter
    {
        /// <summary>The prefab as JSON text. <paramref name="reference"/> is the resolved base prefab, or null.</summary>
        public static string ToJson(Prefab prefab, Prefab reference, string baseName)
        {
            var root = new JsonObject { ["name"] = prefab.Name };
            if (!string.IsNullOrEmpty(baseName)) root["base"] = baseName;
            if (!string.IsNullOrEmpty(prefab.Description) && prefab.Description != reference?.Description) root["description"] = prefab.Description;
            if (prefab.Keys.Count > 0)
            {
                var keys = new JsonObject();
                foreach (var kv in prefab.Keys)
                {
                    if (reference != null && reference.Keys.TryGetValue(kv.Key, out var inherited) && SameStrings(inherited, kv.Value)) continue;
                    keys[kv.Key] = kv.Value.Length == 1 ? JsonValue.Create(kv.Value[0]) : new JsonArray(Array.ConvertAll(kv.Value, v => (JsonNode)JsonValue.Create(v)));
                }
                if (keys.Count > 0) root["keys"] = keys;
            }
            if (prefab.Extra != null)
                foreach (var kv in prefab.Extra)
                    if (reference?.Extra == null || !reference.Extra.TryGetPropertyValue(kv.Key, out var inheritedExtra) || !JsonNode.DeepEquals(inheritedExtra, kv.Value))
                        root[kv.Key] = kv.Value?.DeepClone();
            WriteNode(root, prefab, reference);
            return root.ToJsonString(PrefabLibrary.Options);
        }

        /// <summary>Writes the file and tells the library (which rebuilds anything that spawned from the old text).</summary>
        public static void Save(PrefabLibrary library, Prefab prefab, string baseName, string path)
        {
            var reference = baseName != null && library.Contains(baseName) ? library.Fresh(baseName) : null;
            string json = ToJson(prefab, reference, baseName);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, json + "\n");
            library.AddJson(json, prefab.Name, Path.GetFullPath(path));
        }

        private static void WriteNode(JsonObject into, Prefab node, Prefab reference)
        {
            if (node.Detached) into["detached"] = true;
            if (node.PreviewOnly) into["previewOnly"] = true;
            if (node.GameOnly) into["gameOnly"] = true;
            var components = new JsonObject();
            foreach (var cfg in node.Components)
            {
                var refCfg = reference?.Components.Find(c => c.GetType() == cfg.GetType());
                var diff = ConfigDiff(cfg, refCfg);
                // a component the reference lacks is written even when all its fields are defaults: its presence is the point
                if (refCfg == null || diff.Count > 0) components[cfg.ComponentType.Name] = diff;
            }
            if (components.Count > 0) into["components"] = components;
            if (node.Children.Count > 0)
            {
                var children = new JsonObject();
                foreach (var child in node.Children)
                {
                    var refChild = reference?.Child(child.Name);
                    var body = new JsonObject();
                    WriteNode(body, child, refChild);
                    if (refChild == null || body.Count > 0) children[child.Name] = body;
                }
                if (children.Count > 0) into["children"] = children;
            }
        }

        /// <summary>The fields of <paramref name="cfg"/> whose JSON differs from the reference's (a fresh config when null).</summary>
        public static JsonObject ConfigDiff(IComponentConfig cfg, IComponentConfig reference)
        {
            reference ??= (IComponentConfig)Activator.CreateInstance(cfg.GetType());
            var mine = JsonSerializer.SerializeToNode(cfg, cfg.GetType(), PrefabLibrary.Options)!.AsObject();
            var theirs = JsonSerializer.SerializeToNode(reference, cfg.GetType(), PrefabLibrary.Options)!.AsObject();
            var diff = new JsonObject();
            foreach (var kv in mine)
                if (!theirs.TryGetPropertyValue(kv.Key, out var other) || !JsonNode.DeepEquals(other, kv.Value))
                    diff[kv.Key] = kv.Value?.DeepClone();
            foreach (var kv in theirs)
                if (!mine.ContainsKey(kv.Key)) diff[kv.Key] = null; // cleared to null on purpose (a nullable)
            return diff;
        }

        private static bool SameStrings(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
