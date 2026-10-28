using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Game.Content
{
    /// <summary>Brief point light at the muzzle whenever the equipped weapon fires.</summary>
    public class MuzzleFlashLight : MonoBehaviour
    {
        public Transform cameraTransform;
        public Color color = new Color(1f, 0.8f, 0.45f, 1f);
        public float intensity = 4f;
        public float range = 14f;
        public float duration = 0.07f;
        private Light _light;
        private float _timer;

        protected override void Start()
        {
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = color; _light.range = range; _light.intensity = 0f;
            var combat = EnragedInjector<CombatController>.GetInstance();
            if (combat != null) combat.OnFireWeapon += () => _timer = duration;
        }

        protected override void Update()
        {
            if (_light == null) return;
            if (cameraTransform != null) transform.position = cameraTransform.position + cameraTransform.forward * 1.5f - Vector3.up * 0.2f;
            if (_timer > 0f) { _timer -= Time.deltaTime; _light.intensity = intensity * Mathf.Clamp01(_timer / duration + 0.3f); }
            else _light.intensity = 0f;
        }
    }

    /// <summary>Point light that decays over a lifetime (explosions).</summary>
    public class FlashLight : MonoBehaviour
    {
        public float lifetime = 0.4f;
        public float intensity = 6f;
        private Light _light;
        private float _t;

        protected override void Awake()
        {
            _light = GetComponent<Light>();
        }

        protected override void Update()
        {
            _t += Time.deltaTime;
            if (_light != null) _light.intensity = intensity * Mathf.Clamp01(1f - _t / lifetime);
        }
    }
}
