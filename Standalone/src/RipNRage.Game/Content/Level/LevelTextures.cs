using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;

namespace RipNRage.Game.Content.Level
{
    /// <summary>A texture name usable on brush faces in .map files, resolved to a texture (and optionally a material).</summary>
    public sealed class LevelTexture
    {
        public string name;
        public Func<Texture2D> texture;
        public Func<Material> material;
        public Color tint = Color.white;
        public bool visible = true;      // "clip"/"sky" brushes are collision only
        public bool solid = true;
        private Texture2D _cached;
        public Texture2D Texture => _cached ??= texture?.Invoke();
    }

    /// <summary>The texture table shared by the game loader and the level editor.</summary>
    public static class LevelTextures
    {
        private static readonly Dictionary<string, LevelTexture> Table = new Dictionary<string, LevelTexture>(StringComparer.OrdinalIgnoreCase);
        private static bool _init;

        public static IEnumerable<LevelTexture> All { get { Init(); return Table.Values; } }
        public static IReadOnlyList<string> Names { get { Init(); return new List<string>(Table.Keys); } }

        public static LevelTexture Get(string name)
        {
            Init();
            if (Table.TryGetValue(name, out var t)) return t;
            return Table["grid"];
        }

        private static void Init()
        {
            if (_init) return;
            _init = true;
            GameAssets.Load();
            Materials.Load();
            Add("planks", () => ProceduralTextures.WoodPlanks());
            Add("sand", () => ProceduralTextures.Sand());
            Add("grid", () => GameAssets.GridTexture);
            Add("dark", () => GameAssets.Texture13);
            Add("orange", () => GameAssets.OrangeProto);
            Add("green", () => GameAssets.GreenProto);
            Add("purple", () => GameAssets.PurpleProto);
            Add("red", () => GameAssets.RedProto);
            Add("white", () => Texture2D.White);
            Add("metal", () => GameAssets.Texture13).tint = new Color(0.35f, 0.35f, 0.38f, 1f);
            Add("lamp", () => Texture2D.White).material = () => Materials.Lamp;
            Add("water", () => Texture2D.White).material = () => Materials.Water;
            var clip = Add("clip", () => Texture2D.White); clip.visible = false;
            var sky = Add("sky", () => Texture2D.White); sky.visible = false;
        }

        private static LevelTexture Add(string name, Func<Texture2D> tex)
        {
            var t = new LevelTexture { name = name, texture = tex };
            Table[name] = t;
            return t;
        }
    }
}
