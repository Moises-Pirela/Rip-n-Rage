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
    public class EnemyHitFlash : MonoBehaviour
    {
        public SpriteRenderer target;
        public float flashDuration = 0.12f;
        public float dissolveDelay = 2.5f;
        public float dissolveDuration = 1.6f;
        private float _flash;
        private float _lastHealth;
        private AIState _state;
        private float _deadTimer = -1f;
        private Material _dissolve;
        private float _telegraphUntil, _telegraphDuration;
        private Color _telegraphColor;

        /// <summary>Standalone addition: pulses the sprite in <paramref name="color"/> for <paramref name="duration"/> seconds (attack wind-up, alert).</summary>
        public void Telegraph(float duration, Color color)
        {
            _telegraphUntil = Time.time + duration;
            _telegraphDuration = Mathf.Max(0.01f, duration);
            _telegraphColor = color;
        }

        protected override void Start()
        {
            _state = GetComponent<AIState>();
            if (_state == null) return;
            _lastHealth = _state.Health;
            _state.OnHealthChanged += OnHealthChanged;
            _state.OnDieCallback += () => _deadTimer = 0f;
            target ??= GetComponentInChildren<SpriteRenderer>();
        }

        private void OnHealthChanged()
        {
            if (_state.Health < _lastHealth) _flash = 1f;
            _lastHealth = _state.Health;
        }

        protected override void Update()
        {
            if (target == null) return;
            if (Time.time < _telegraphUntil && _flash <= 0f)
            {
                // ramping pulse in the telegraph colour: the player reads "about to fire" / "noticed you"
                float left = (_telegraphUntil - Time.time) / _telegraphDuration;
                float pulse = 0.35f + 0.45f * (1f - left) * (0.6f + 0.4f * Mathf.Sin(Time.time * 28f));
                target.propertyBlock.SetColor("_FlashColor", _telegraphColor);
                target.propertyBlock.SetFloat("_Flash", pulse);
            }
            else if (_flash > 0f)
            {
                _flash -= Time.deltaTime / flashDuration;
                target.propertyBlock.SetColor("_FlashColor", new Color(1f, 1f, 1f, 0f)); // alpha 0 = white hit flash
                target.propertyBlock.SetFloat("_Flash", Mathf.Clamp01(_flash) * 0.85f);
            }
            else if (target.propertyBlock.GetFloat("_Flash") != 0f) target.propertyBlock.SetFloat("_Flash", 0f);
            if (_deadTimer >= 0f)
            {
                _deadTimer += Time.deltaTime;
                float t = (_deadTimer - dissolveDelay) / dissolveDuration;
                if (t >= 0f)
                {
                    if (_dissolve == null) { _dissolve = Materials.EnemyDissolve.Clone(); target.material = _dissolve; }
                    _dissolve.SetFloat("_Dissolve", Mathf.Clamp01(t));
                    if (t >= 1f) Destroy(transform.root.gameObject);
                }
            }
        }
    }
}
