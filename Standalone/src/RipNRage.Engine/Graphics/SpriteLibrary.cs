using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Registry that gives every sprite a stable string reference ("Atlas/spriteName") so animation clips can be
    /// saved to files and edited outside the code that loaded the atlases.
    /// </summary>
    public static class SpriteLibrary
    {
        private static readonly Dictionary<string, SpriteAtlas> _atlases = new Dictionary<string, SpriteAtlas>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _atlasOrder = new List<string>();
        private static readonly Dictionary<string, Sprite> _loose = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Sprite, string> _refs = new Dictionary<Sprite, string>();

        /// <summary>Registered atlases in registration order (key, atlas).</summary>
        public static IEnumerable<(string key, SpriteAtlas atlas)> Atlases
        {
            get { foreach (var k in _atlasOrder) yield return (k, _atlases[k]); }
        }

        /// <summary>Stand-alone sprites registered under a key ("SonicShotgun/Sonic01").</summary>
        public static IEnumerable<KeyValuePair<string, Sprite>> LooseSprites => _loose;

        public static void RegisterAtlas(string key, SpriteAtlas atlas)
        {
            if (atlas == null) return;
            if (!_atlases.ContainsKey(key)) _atlasOrder.Add(key);
            _atlases[key] = atlas;
            foreach (var kv in atlas.Sprites) _refs[kv.Value] = key + "/" + kv.Key;
        }

        public static void RegisterSprite(string reference, Sprite sprite)
        {
            if (sprite == null) return;
            _loose[reference] = sprite;
            _refs[sprite] = reference;
        }

        public static SpriteAtlas GetAtlas(string key) => _atlases.TryGetValue(key, out var a) ? a : null;

        /// <summary>"Atlas/name" → sprite (null when unknown).</summary>
        public static Sprite Resolve(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            if (_loose.TryGetValue(reference, out var loose)) return loose;
            int slash = reference.IndexOf('/');
            if (slash <= 0) return null;
            string atlasKey = reference.Substring(0, slash), spriteName = reference.Substring(slash + 1);
            if (!_atlases.TryGetValue(atlasKey, out var atlas)) return null;
            if (atlas.TryGet(spriteName, out var s)) return s;
            try { return atlas.Get(spriteName); } catch (KeyNotFoundException) { return null; }
        }

        /// <summary>Stable reference of a registered sprite (null for unregistered sprites).</summary>
        public static string ReferenceOf(Sprite sprite) => sprite != null && _refs.TryGetValue(sprite, out var r) ? r : null;

        public static void Clear()
        {
            _atlases.Clear(); _atlasOrder.Clear(); _loose.Clear(); _refs.Clear();
        }
    }
}
