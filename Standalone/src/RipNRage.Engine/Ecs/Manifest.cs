using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// A text dump of everything a world holds: one block per entity (its components, every public field), ordered by
    /// content rather than by entity id, so two builds of the same scene produce the same text whatever order they
    /// created things in. The gate of layer 10 (10.0): a level class moved from code to a prefab must build the same
    /// entities, and a byte-identical manifest says so. Entity-valued fields print the rank of the entity they point
    /// at, which makes the hierarchy and the cross-references part of the comparison; asset handles print the name
    /// they were interned under, not their id.
    /// </summary>
    public static class EntityManifest
    {
        private static readonly MethodInfo GetGeneric = typeof(World).GetMethod(nameof(World.Get));
        private static readonly Dictionary<Type, MethodInfo> _getters = new Dictionary<Type, MethodInfo>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The manifest text, with a header line (<c>entities N</c>) and a trailing <c>manifest &lt;hash&gt;</c>.</summary>
        public static string Build(World world)
        {
            var entities = new List<Entity>(world.AllEntities());

            // pass 1: each entity's block with references blanked, plus its ancestors' — identical siblings (six lamp
            // heads, four pickup sprites) would otherwise tie and take their rank from creation order, which made the
            // Parent of a tied entity wobble between builds and showed up as a diff that meant nothing (10.3c).
            var own = new Dictionary<Entity, string>(entities.Count);
            foreach (var e in entities) own[e] = Block(world, e, _ => "?");
            var keys = new Dictionary<Entity, string>(entities.Count);
            string Key(Entity e, int depth)
            {
                if (keys.TryGetValue(e, out var k)) return k;
                k = own[e];
                if (depth < 32 && world.Has<Transforms.Parent>(e))
                {
                    var p = world.Get<Transforms.Parent>(e).Value;
                    if (own.ContainsKey(p)) k += "|^" + Key(p, depth + 1);
                }
                return keys[e] = k;
            }
            var blocks = new List<(Entity entity, string sortKey)>(entities.Count);
            foreach (var e in entities) blocks.Add((e, Key(e, 0)));
            blocks.Sort((a, b) => string.CompareOrdinal(a.sortKey, b.sortKey));

            // pass 2: ranks are positions in that order, so references are stable across builds
            var rank = new Dictionary<Entity, int>(blocks.Count);
            for (int i = 0; i < blocks.Count; i++) rank[blocks[i].entity] = i;
            string Name(Entity e) => e.IsNull ? "null" : rank.TryGetValue(e, out int r) ? "#" + r.ToString(Inv) : "dead";

            var sb = new StringBuilder();
            sb.Append("entities ").Append(blocks.Count.ToString(Inv)).Append('\n');
            for (int i = 0; i < blocks.Count; i++)
                sb.Append('#').Append(i.ToString(Inv)).Append('\n').Append(Block(world, blocks[i].entity, Name));
            sb.Append("manifest ").Append(Hash(sb.ToString())).Append('\n');
            return sb.ToString();
        }

        /// <summary>Writes <see cref="Build"/> to a file and returns its <see cref="Summary"/>.</summary>
        public static string Write(World world, string path)
        {
            string text = Build(world);
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, text);
            return Summary(text);
        }

        /// <summary>One line for a log: the entity count and the hash of a manifest text.</summary>
        public static string Summary(string manifest)
        {
            int nl = manifest.IndexOf('\n');
            return $"{manifest.Substring(0, nl)}, {manifest.Substring(manifest.LastIndexOf("manifest ", StringComparison.Ordinal)).TrimEnd('\n')}";
        }

        /// <summary>FNV-1a over the text: a short name for a whole scene.</summary>
        public static string Hash(string text)
        {
            uint h = 2166136261;
            foreach (char c in text) { h ^= c; h *= 16777619; }
            return h.ToString("x8", Inv);
        }

        private static string Block(World world, Entity e, Func<Entity, string> name)
        {
            var sig = world.SignatureOf(e);
            var lines = new List<string>();
            for (int id = 0; id < Signature.Capacity; id++)
            {
                if (!sig.Has(id)) continue;
                var info = ComponentRegistry.Get(id);
                var value = Read(world, info.Type, e);
                var sb = new StringBuilder("  ").Append(info.Type.Name);
                foreach (var f in info.Type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType == typeof(Matrix4x4)) continue; // derived, and 16 floats of noise
                    sb.Append(' ').Append(f.Name).Append('=').Append(Val(f.GetValue(value), name));
                }
                lines.Add(sb.ToString());
            }
            lines.Sort(StringComparer.Ordinal); // component order follows the name, not the registry's ids
            return string.Join("\n", lines) + "\n";
        }

        private static object Read(World world, Type component, Entity e)
        {
            if (!_getters.TryGetValue(component, out var m)) _getters[component] = m = GetGeneric.MakeGenericMethod(component);
            return m.Invoke(world, new object[] { e });
        }

        private static string Val(object v, Func<Entity, string> name)
        {
            switch (v)
            {
                case null: return "null";
                case float f: return F(f);
                case double d: return F((float)d);
                case bool b: return b ? "1" : "0";
                case string s: return s.Length == 0 ? "\"\"" : s;
                case Entity e: return name(e);
                case Vector2 v2: return $"{F(v2.x)},{F(v2.y)}";
                case Vector3 v3: return $"{F(v3.x)},{F(v3.y)},{F(v3.z)}";
                case Quaternion q: return $"{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}";
                case Color c: return $"{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}";
                case Enum en: return en.ToString();
                case IFormattable n: return n.ToString(null, Inv);
            }
            var t = v.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AssetHandle<>))
            {
                int id = (int)t.GetField("Id").GetValue(v);
                if (id == 0) return "none";
                var asset = t.GetGenericArguments()[0];
                return asset.Name + ":" + (AssetRegistries.NameOf(asset, id) ?? "#" + id.ToString(Inv));
            }
            return v.ToString();
        }

        /// <summary>Floats to four places, without a negative zero and without the culture's ideas about separators.</summary>
        private static string F(float f)
        {
            if (!float.IsFinite(f)) return f.ToString(Inv);
            if (MathF.Abs(f) < 5E-5f) return "0";
            return f.ToString("0.####", Inv);
        }
    }

    /// <summary>
    /// Every <see cref="AssetRegistry{T}"/> that exists, by asset type, so a dump (or an editor) can turn a handle
    /// back into the name it was interned under without knowing which registry owns it.
    /// </summary>
    public static class AssetRegistries
    {
        private static readonly Dictionary<Type, List<Func<int, string>>> _byAsset = new Dictionary<Type, List<Func<int, string>>>();

        internal static void Register(Type asset, Func<int, string> nameOf)
        {
            lock (_byAsset)
            {
                if (!_byAsset.TryGetValue(asset, out var list)) _byAsset[asset] = list = new List<Func<int, string>>();
                list.Add(nameOf);
            }
        }

        /// <summary>The name handle <paramref name="id"/> of this asset type was interned under (null = anonymous / unknown).</summary>
        public static string NameOf(Type asset, int id)
        {
            List<Func<int, string>> list;
            lock (_byAsset) if (!_byAsset.TryGetValue(asset, out list)) return null;
            foreach (var f in list) { var n = f(id); if (n != null) return n; }
            return null;
        }
    }
}
