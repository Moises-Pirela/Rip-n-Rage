using System;
using System.Collections.Generic;
using System.Globalization;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    public enum BlendMode { Opaque, AlphaBlend, Additive, Multiply, PremultipliedAlpha }
    public enum CullMode { Back, Front, Off }

    /// <summary>
    /// Typed shader properties (UnityEngine.MaterialPropertyBlock equivalent). Property names are
    /// uniform names; by convention material properties start with an underscore (<c>_Flash</c>)
    /// while engine-provided uniforms start with <c>u</c> (<c>uModel</c>, <c>uSunDir</c>, <c>uTime</c>).
    /// </summary>
    public class MaterialPropertyBlock
    {
        internal readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        internal readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        internal readonly Dictionary<string, Vector4> Vectors = new Dictionary<string, Vector4>();
        internal readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        internal readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        public bool IsEmpty => Floats.Count == 0 && Ints.Count == 0 && Vectors.Count == 0 && Colors.Count == 0 && Textures.Count == 0;

        public void SetFloat(string name, float v) => Floats[name] = v;
        public void SetInt(string name, int v) => Ints[name] = v;
        public void SetVector(string name, Vector4 v) => Vectors[name] = v;
        public void SetVector(string name, Vector3 v) => Vectors[name] = new Vector4(v.x, v.y, v.z, 0f);
        public void SetVector(string name, Vector2 v) => Vectors[name] = new Vector4(v.x, v.y, 0f, 0f);
        public void SetColor(string name, Color c) => Colors[name] = c;
        public void SetTexture(string name, Texture2D t) { if (t == null) Textures.Remove(name); else Textures[name] = t; }

        public float GetFloat(string name, float def = 0f) => Floats.TryGetValue(name, out var v) ? v : def;
        public int GetInt(string name, int def = 0) => Ints.TryGetValue(name, out var v) ? v : def;
        public Vector4 GetVector(string name) => Vectors.TryGetValue(name, out var v) ? v : default;
        public Color GetColor(string name, Color def = default) => Colors.TryGetValue(name, out var v) ? v : def;
        public Texture2D GetTexture(string name) => Textures.TryGetValue(name, out var v) ? v : null;
        public bool HasProperty(string name) => Floats.ContainsKey(name) || Ints.ContainsKey(name) || Vectors.ContainsKey(name) || Colors.ContainsKey(name) || Textures.ContainsKey(name);
        public void Remove(string name) { Floats.Remove(name); Ints.Remove(name); Vectors.Remove(name); Colors.Remove(name); Textures.Remove(name); }
        public void Clear() { Floats.Clear(); Ints.Clear(); Vectors.Clear(); Colors.Clear(); Textures.Clear(); }

        public void CopyFrom(MaterialPropertyBlock o)
        {
            foreach (var kv in o.Floats) Floats[kv.Key] = kv.Value;
            foreach (var kv in o.Ints) Ints[kv.Key] = kv.Value;
            foreach (var kv in o.Vectors) Vectors[kv.Key] = kv.Value;
            foreach (var kv in o.Colors) Colors[kv.Key] = kv.Value;
            foreach (var kv in o.Textures) Textures[kv.Key] = kv.Value;
        }

        /// <summary>Uploads the properties to the currently bound shader. Textures are bound from <paramref name="firstUnit"/> upwards.</summary>
        internal int Upload(Shader sh, int firstUnit)
        {
            foreach (var kv in Floats) { int l = sh.Loc(kv.Key); if (l >= 0) GL.Uniform1f(l, kv.Value); }
            foreach (var kv in Ints) { int l = sh.Loc(kv.Key); if (l >= 0) GL.Uniform1i(l, kv.Value); }
            foreach (var kv in Vectors) { int l = sh.Loc(kv.Key); if (l >= 0) GL.Uniform4f(l, kv.Value.x, kv.Value.y, kv.Value.z, kv.Value.w); }
            foreach (var kv in Colors) { int l = sh.Loc(kv.Key); if (l >= 0) GL.Uniform4f(l, kv.Value.r, kv.Value.g, kv.Value.b, kv.Value.a); }
            int unit = firstUnit;
            foreach (var kv in Textures)
            {
                if (kv.Key == "uTex") continue; // main texture: bound to unit 0 by the renderer
                int l = sh.Loc(kv.Key);
                if (l < 0) continue;
                kv.Value.Bind(unit);
                GL.Uniform1i(l, unit);
                unit++;
            }
            return unit;
        }

        /// <summary>Applies "key = value" pairs (from tunables.cfg): numbers → float, "r,g,b[,a]" → colour, "x,y,z,w" → vector, "tex:path" → texture.</summary>
        public void SetFromString(string key, string value, Func<string, Texture2D> textureLoader = null)
        {
            value = value.Trim();
            if (value.StartsWith("tex:", StringComparison.OrdinalIgnoreCase))
            {
                if (textureLoader != null) { var t = textureLoader(value.Substring(4).Trim()); if (t != null) SetTexture(key, t); }
                return;
            }
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<float>();
            foreach (var p in parts)
            {
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) nums.Add(f);
                else if (p == "true" || p == "on") nums.Add(1f);
                else if (p == "false" || p == "off") nums.Add(0f);
                else throw new FormatException("not a number: " + p);
            }
            if (nums.Count == 1) SetFloat(key, nums[0]);
            else if (nums.Count == 2) SetVector(key, new Vector2(nums[0], nums[1]));
            else if (nums.Count == 3) SetColor(key, new Color(nums[0], nums[1], nums[2], 1f));
            else if (nums.Count >= 4) SetColor(key, new Color(nums[0], nums[1], nums[2], nums[3]));
        }
    }

    /// <summary>
    /// Shader + properties + render state (UnityEngine.Material equivalent). Assign to
    /// <see cref="MeshRenderer.material"/> / <see cref="SpriteRenderer.material"/>; change properties
    /// at any time. Per-object variations go in the renderer's <see cref="Renderer.propertyBlock"/>.
    /// </summary>
    public sealed class Material : MaterialPropertyBlock
    {
        public string name;
        public Shader shader;
        public BlendMode blend = BlendMode.Opaque;
        public CullMode cull = CullMode.Back;
        public bool depthWrite = true;
        public bool depthTest = true;
        /// <summary>Draw order: &lt; 3000 opaque (sorted by queue), &gt;= 3000 transparent (drawn after, back to front).</summary>
        public int renderQueue = 2000;
        /// <summary>Alpha threshold below which fragments are discarded (sprites).</summary>
        public float alphaCutoff = 0.02f;

        public Material(Shader shader, string name = null)
        {
            this.shader = shader;
            this.name = name ?? "Material";
        }

        public Material(string shaderName, string name = null) : this(ShaderLibrary.Get(shaderName), name ?? shaderName) { }

        public bool IsTransparent => blend != BlendMode.Opaque || renderQueue >= 3000;

        /// <summary>Main texture (uniform <c>uTex</c>, unit 0).</summary>
        public Texture2D mainTexture { get => GetTexture("uTex"); set => SetTexture("uTex", value); }
        /// <summary>Tint colour (uniform <c>uTint</c>).</summary>
        public Color color { get => GetColor("uTint", Color.white); set => SetColor("uTint", value); }

        public Material Clone(string newName = null)
        {
            var m = new Material(shader, newName ?? name + " (clone)") { blend = blend, cull = cull, depthWrite = depthWrite, depthTest = depthTest, renderQueue = renderQueue, alphaCutoff = alphaCutoff };
            m.CopyFrom(this);
            return m;
        }

        /// <summary>Sets the fixed-function state for this material.</summary>
        internal void ApplyState()
        {
            switch (blend)
            {
                case BlendMode.Opaque: GL.Disable(GL.BLEND); break;
                case BlendMode.AlphaBlend: GL.Enable(GL.BLEND); GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); break;
                case BlendMode.PremultipliedAlpha: GL.Enable(GL.BLEND); GL.BlendFunc(GL.ONE, GL.ONE_MINUS_SRC_ALPHA); break;
                case BlendMode.Additive: GL.Enable(GL.BLEND); GL.BlendFunc(GL.SRC_ALPHA, GL.ONE); break;
                case BlendMode.Multiply: GL.Enable(GL.BLEND); GL.BlendFunc(GL.DST_COLOR, GL.ZERO); break;
            }
            if (cull == CullMode.Off) GL.Disable(GL.CULL_FACE);
            else { GL.Enable(GL.CULL_FACE); GL.CullFace(cull == CullMode.Back ? GL.BACK : GL.FRONT); }
            if (depthTest) GL.Enable(GL.DEPTH_TEST); else GL.Disable(GL.DEPTH_TEST);
            GL.DepthMask(depthWrite && !IsTransparent);
        }

        /// <summary>Uploads material properties then the per-object block (which wins). Returns the next free texture unit.</summary>
        internal int UploadProperties(MaterialPropertyBlock perObject, int firstUnit)
        {
            int unit = Upload(shader, firstUnit);
            if (perObject != null && !perObject.IsEmpty) unit = perObject.Upload(shader, unit);
            return unit;
        }
    }

    /// <summary>
    /// Named materials so tunables.cfg can retune them live (<c>[material.water] _WaveSpeed = 0.4</c>)
    /// and gameplay code can look them up.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Dictionary<string, string>> _pendingConfig;
        public static Func<string, Texture2D> TextureLoader;

        public static Material Register(string name, Material m)
        {
            m.name = name;
            Materials[name] = m;
            // apply config values that were loaded before the material existed
            if (_pendingConfig != null && _pendingConfig.TryGetValue("material." + name, out var kv)) ApplyTo(m, kv);
            return m;
        }

        public static Material Get(string name) => Materials.TryGetValue(name, out var m) ? m : null;
        public static IEnumerable<Material> All => Materials.Values;

        /// <summary>Applies every <c>[material.&lt;name&gt;]</c> section (called by the tunables loader).</summary>
        public static void ApplyConfig(Dictionary<string, Dictionary<string, string>> sections)
        {
            _pendingConfig = sections;
            foreach (var kv in sections)
            {
                if (!kv.Key.StartsWith("material.", StringComparison.OrdinalIgnoreCase)) continue;
                string name = kv.Key.Substring("material.".Length);
                if (Materials.TryGetValue(name, out var m)) ApplyTo(m, kv.Value);
            }
        }

        private static void ApplyTo(Material m, Dictionary<string, string> kv)
        {
            foreach (var prop in kv)
            {
                try
                {
                    switch (prop.Key.ToLowerInvariant())
                    {
                        case "shader": m.shader = ShaderLibrary.Get(prop.Value.Trim()); break;
                        case "blend": m.blend = Enum.Parse<BlendMode>(prop.Value.Trim(), true); break;
                        case "cull": m.cull = Enum.Parse<CullMode>(prop.Value.Trim(), true); break;
                        case "queue": m.renderQueue = int.Parse(prop.Value.Trim(), CultureInfo.InvariantCulture); break;
                        case "depth_write": m.depthWrite = prop.Value.Trim().ToLowerInvariant() is "true" or "1" or "on"; break;
                        case "alpha_cutoff": m.alphaCutoff = float.Parse(prop.Value.Trim(), CultureInfo.InvariantCulture); break;
                        default: m.SetFromString(prop.Key, prop.Value, TextureLoader); break;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"tunables.cfg [material.{m.name}] {prop.Key}: {e.Message}"); }
            }
        }
    }

    [Serializable]
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static readonly Vector4 zero = new Vector4(0, 0, 0, 0);
        public static readonly Vector4 one = new Vector4(1, 1, 1, 1);
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00}, {w:0.00})";
    }
}
