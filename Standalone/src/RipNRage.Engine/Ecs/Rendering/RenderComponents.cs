using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Engine.Ecs.Rendering
{
    /// <summary>Makes the entity a camera; the highest <see cref="Priority"/> one renders the frame.</summary>
    public struct CameraLens : IComponent
    {
        public float FieldOfView, Near, Far;
        public Color Background;
        public int Priority;
    }

    /// <summary>A sprite quad in the entity's XY plane (same semantics as the legacy SpriteRenderer).</summary>
    public struct SpriteRender : IComponent
    {
        public AssetHandle<Sprite> Sprite;
        public Color Color;
        public bool FlipX, FlipY, Billboard, Lit, CastsShadow, FullBright;
        public float BillboardRoll;
        public int SortingOrder;
        public RenderLayer Layer;
        public AssetHandle<Material> Material;
        /// <summary>Switched off by the sprite's animation (clip "enabled" keys); <see cref="Hidden"/> is the owner's own switch.</summary>
        public bool Disabled;
    }

    /// <summary>A textured mesh (same semantics as the legacy MeshRenderer).</summary>
    public struct MeshRender : IComponent
    {
        public AssetHandle<Mesh> Mesh;
        public AssetHandle<Texture2D> Texture;
        public Vector2 UvScale;
        public float Emissive;
        public bool CastsShadow, Transparent, Occluder;
        public Color Color;
        public RenderLayer Layer;
        public AssetHandle<Material> Material;
    }

    public struct LightSource : IComponent
    {
        public LightType Type;
        public Color Color;
        public float Intensity, Range;
    }

    /// <summary>Tag: skip this entity when rendering (cheap to toggle: sparse).</summary>
    [SparseComponent] public struct Hidden : IComponent { }

    /// <summary>
    /// Per-entity sprite shader parameters — the per-entity material override layer 1 deferred "until something needs
    /// it": the enemy hit flash and attack telegraph (<see cref="Flash"/> blends the sprite towards
    /// <see cref="FlashColor"/>; alpha 0 = white) and the death dissolve (<see cref="Dissolve"/> 0..1, read by the
    /// sprite_dissolve shader). <see cref="EcsRenderProducer"/> turns a non-zero one into the draw's property block.
    /// </summary>
    public struct SpriteFx : IComponent
    {
        public Color FlashColor;
        public float Flash;
        public float Dissolve;
    }

    /// <summary>The registries render handles resolve through. Configs intern under the name they baked from.</summary>
    public static class RenderAssets
    {
        public static readonly AssetRegistry<Sprite> Sprites = new AssetRegistry<Sprite>();
        public static readonly AssetRegistry<Mesh> Meshes = new AssetRegistry<Mesh>();
        public static readonly AssetRegistry<Texture2D> Textures = new AssetRegistry<Texture2D>();
        public static readonly AssetRegistry<Material> Materials = new AssetRegistry<Material>();
    }

    // ---------------------------------------------------------------- configs (JSON prefabs)

    public sealed class CameraLensConfig : ComponentConfig<CameraLens>
    {
        public float fov = 72f, near = 0.05f, far = 2000f;
        public float[] background = { 0.19f, 0.3f, 0.47f, 1f };
        public int priority;
        public override CameraLens Bake() => new CameraLens { FieldOfView = fov, Near = near, Far = far, Background = ConfigUtil.Color(background), Priority = priority };
    }

    public sealed class SpriteRenderConfig : ComponentConfig<SpriteRender>
    {
        /// <summary>Sprite reference as SpriteLibrary resolves it ("Atlas/name" or a loose sprite path).</summary>
        public string sprite;
        public float[] color = { 1, 1, 1, 1 };
        public bool flipX, flipY, billboard, lit, castsShadow = true, fullBright;
        public float billboardRoll;
        public int sortingOrder;
        public RenderLayer layer;
        public override SpriteRender Bake() => new SpriteRender
        {
            Sprite = RenderAssets.Sprites.GetOrLoad(sprite, SpriteLibrary.Resolve),
            Color = ConfigUtil.Color(color), FlipX = flipX, FlipY = flipY, Billboard = billboard, Lit = lit,
            CastsShadow = castsShadow, FullBright = fullBright, BillboardRoll = billboardRoll, SortingOrder = sortingOrder, Layer = layer,
        };
    }

    public sealed class MeshRenderConfig : ComponentConfig<MeshRender>
    {
        /// <summary>A <see cref="MeshLibrary"/> name ("box", "plane", a sized form, or an .obj). Empty = a shape
        /// component (<see cref="BoxMesh"/>, <see cref="PlaneMesh"/>) names it as the entity is created.</summary>
        public string mesh;
        /// <summary>Texture path under assets (null = white).</summary>
        public string texture;
        /// <summary>A <see cref="MaterialLibrary"/> name (null = the default lit material). 10.3a: the lamp head's pulse.</summary>
        public string material;
        public float[] color = { 1, 1, 1, 1 };
        public float[] uvScale = { 1, 1 };
        public float emissive;
        public bool castsShadow = true, transparent, occluder;
        public RenderLayer layer;
        public override MeshRender Bake() => new MeshRender
        {
            Mesh = string.IsNullOrEmpty(mesh) ? AssetHandle<Mesh>.None : RenderAssets.Meshes.GetOrLoad(mesh, MeshLibrary.Get),
            Texture = string.IsNullOrEmpty(texture)
                ? RenderAssets.Textures.Intern(Texture2D.White, "white")
                : RenderAssets.Textures.GetOrLoad(texture, t => Texture2D.Load(Application.AssetPath(t), FilterMode.Trilinear, TextureWrapMode.Repeat)),
            Material = MaterialByName(material),
            Color = ConfigUtil.Color(color), UvScale = new Vector2(uvScale[0], uvScale[1]), Emissive = emissive,
            CastsShadow = castsShadow, Transparent = transparent, Occluder = occluder, Layer = layer,
        };

        /// <summary>The render handle of a <see cref="MaterialLibrary"/> name (null / empty = none; unregistered = an error).</summary>
        public static AssetHandle<Material> MaterialByName(string name) => string.IsNullOrEmpty(name)
            ? AssetHandle<Material>.None
            : RenderAssets.Materials.GetOrLoad(name, n => MaterialLibrary.Get(n) ?? throw new KeyNotFoundException($"material '{n}' is not registered"));
    }

    public sealed class LightSourceConfig : ComponentConfig<LightSource>
    {
        public LightType type = LightType.Point;
        public float[] color = { 1, 1, 1, 1 };
        public float intensity = 1f, range = 10f;
        public override LightSource Bake() => new LightSource { Type = type, Color = ConfigUtil.Color(color), Intensity = intensity, Range = range };
    }

    public sealed class HiddenConfig : ComponentConfig<Hidden> { public override Hidden Bake() => default; }

    /// <summary>Per-entity sprite parameters: a fresh entity flashes and dissolves not at all.</summary>
    public sealed class SpriteFxConfig : ComponentConfig<SpriteFx> { public override SpriteFx Bake() => default; }

    internal static class ConfigUtil
    {
        public static Color Color(float[] c) => c == null || c.Length < 3 ? RipNRage.Engine.Color.white : new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
    }

    /// <summary>
    /// Named meshes for configs: unit <c>box</c> and <c>plane</c>, a sized box <c>box:cx,cy,cz:sx,sy,sz@uv</c>, a sized
    /// plane <c>plane:WxL@uv</c> (10.3a: the forms the level has always interned its meshes under, so a prefab names
    /// the very entry a code spawn made), a registered name, or an .obj path under assets.
    /// </summary>
    public static class MeshLibrary
    {
        private static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static void Register(string name, Mesh mesh) => _meshes[name] = mesh;

        /// <summary>The name of a box mesh with this centre, size and UV scale (what <see cref="Get"/> parses).</summary>
        public static string BoxName(Vector3 center, Vector3 size, float uvScale = 1f) =>
            FormattableString.Invariant($"box:{center.x},{center.y},{center.z}:{size.x},{size.y},{size.z}@{uvScale}");

        /// <summary>The name of a flat plane mesh with this extent and UV scale.</summary>
        public static string PlaneName(float width, float length, float uvScale) =>
            FormattableString.Invariant($"plane:{width}x{length}@{uvScale}");

        public static Mesh Get(string name)
        {
            if (_meshes.TryGetValue(name, out var m)) return m;
            if (name.StartsWith("box:", StringComparison.OrdinalIgnoreCase)) m = ParseBox(name);
            else if (name.StartsWith("plane:", StringComparison.OrdinalIgnoreCase)) m = ParsePlane(name);
            else m = name.ToLowerInvariant() switch
            {
                "box" => Mesh.CreateBox(Vector3.zero, Vector3.one),
                "plane" => Mesh.CreatePlane(Vector3.zero, 1f, 1f),
                _ when name.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) => Mesh.LoadObj(Application.AssetPath(name)),
                _ => throw new KeyNotFoundException($"mesh '{name}' is not registered"),
            };
            return _meshes[name] = m;
        }

        // box:cx,cy,cz:sx,sy,sz@uv
        private static Mesh ParseBox(string name)
        {
            var (body, uv) = SplitUv(name.Substring(4), name);
            var parts = body.Split(':');
            if (parts.Length != 2) throw new FormatException($"mesh '{name}': expected box:cx,cy,cz:sx,sy,sz@uv");
            return Mesh.CreateBox(Vec(parts[0], name), Vec(parts[1], name), uv);
        }

        // plane:WxL@uv
        private static Mesh ParsePlane(string name)
        {
            var (body, uv) = SplitUv(name.Substring(6), name);
            var wl = body.Split('x', 'X');
            if (wl.Length != 2) throw new FormatException($"mesh '{name}': expected plane:WxL@uv");
            return Mesh.CreatePlane(Vector3.zero, Num(wl[0], name), Num(wl[1], name), uv);
        }

        private static (string body, float uv) SplitUv(string s, string name)
        {
            int at = s.LastIndexOf('@');
            return at < 0 ? (s, 1f) : (s.Substring(0, at), Num(s.Substring(at + 1), name));
        }

        private static Vector3 Vec(string s, string name)
        {
            var p = s.Split(',');
            if (p.Length != 3) throw new FormatException($"mesh '{name}': expected x,y,z in '{s}'");
            return new Vector3(Num(p[0], name), Num(p[1], name), Num(p[2], name));
        }

        private static float Num(string s, string name) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out var f) ? f : throw new FormatException($"mesh '{name}': '{s}' is not a number");
    }
}
