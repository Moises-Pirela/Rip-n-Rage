using RipNRage.Engine.Graphics;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Materials used by the TestArea. They are registered in the MaterialLibrary so
    /// <c>[material.&lt;name&gt;]</c> sections in tunables.cfg can retune them while the game runs,
    /// and their shaders live in Standalone/shaders/*.glsl (hot-reloaded on save).
    /// </summary>
    public static class Materials
    {
        public static Material Water, Lamp, EnemyDissolve, Crate;

        public static void Load()
        {
            if (Water != null) return;

            // Animated water using the normal maps that shipped with the Unity project.
            Water = new Material("water") { blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000 };
            Water.SetTexture("_NormalA", Texture2D.Load(Application.AssetPath("Textures/Normals/waternormal.png"), FilterMode.Trilinear, TextureWrapMode.Repeat));
            Water.SetTexture("_NormalB", Texture2D.Load(Application.AssetPath("Textures/Normals/waternormal2.png"), FilterMode.Trilinear, TextureWrapMode.Repeat));
            Water.SetFloat("_WaveAmplitude", 0.15f);
            Water.SetFloat("_WaveLength", 18f);
            Water.SetFloat("_WaveSpeed", 1.2f);
            Water.SetFloat("_Tiling", 0.05f);
            Water.SetFloat("_ScrollSpeed", 0.03f);
            Water.SetFloat("_NormalStrength", 0.6f);
            Water.SetColor("_ShallowColor", new Color(0.45f, 0.80f, 0.95f, 1f));
            Water.SetColor("_DeepColor", new Color(0.05f, 0.25f, 0.45f, 1f));
            Water.SetFloat("_Fresnel", 1.0f);
            Water.SetFloat("_Opacity", 0.82f);
            Water.SetFloat("_Specular", 1.5f);
            MaterialLibrary.Register("water", Water);

            // Lamp heads: lit mesh with a pulsing emissive term.
            Lamp = new Material("mesh_emissive_pulse");
            Lamp.SetFloat("_PulseSpeed", 2.5f);
            Lamp.SetFloat("_PulseMin", 0.9f);
            Lamp.SetFloat("_PulseMax", 1.6f);
            Lamp.SetColor("_EmissiveColor", new Color(1f, 0.82f, 0.5f, 1f));
            MaterialLibrary.Register("lamp", Lamp);

            // Enemy death dissolve (cloned per enemy so each one animates its own _Dissolve).
            EnemyDissolve = new Material("sprite_dissolve") { blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000 };
            EnemyDissolve.SetFloat("_Dissolve", 0f);
            EnemyDissolve.SetFloat("_EdgeWidth", 0.08f);
            EnemyDissolve.SetFloat("_NoiseScale", 24f);
            EnemyDissolve.SetColor("_EdgeColor", new Color(1f, 0.45f, 0.1f, 1f));
            MaterialLibrary.Register("enemy_dissolve", EnemyDissolve);

            // Crates share the default mesh shader but are registered so their tint can be tweaked from the cfg.
            Crate = new Material("mesh") { color = new Color(0.9f, 0.9f, 0.9f, 1f) };
            MaterialLibrary.Register("crate", Crate);
        }
    }

    /// <summary>
    /// Sprite-shader showcase on enemies: a white hit flash through the per-object property block
    /// (<c>_Flash</c> on the default sprite shader) and, after death, a dissolve material.
    /// Hooks the ported EntityState events instead of modifying the ported scripts.
    /// </summary>
}
