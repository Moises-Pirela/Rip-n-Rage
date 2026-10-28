using System;
using System.Collections.Generic;
using System.Globalization;

namespace RipNRage.Engine.Graphics.Lighting
{
    /// <summary>Sun, ambient, sky, distance fog, shadow-map and volumetric-fog tunables (tunables.cfg).</summary>
    public sealed class LightingSettings
    {
        // [lighting]
        public float sunElevation = 28f;     // degrees above the horizon
        public float sunAzimuth = 35f;       // degrees, 0 = light travelling towards +Z
        public Color sunColor = new Color(1.0f, 0.93f, 0.80f, 1f);
        public float sunIntensity = 1.15f;
        public Color ambientSky = new Color(0.42f, 0.50f, 0.64f, 1f);
        public Color ambientGround = new Color(0.30f, 0.25f, 0.20f, 1f);
        public float specular = 0.25f;
        public float spriteWrap = 0.6f;       // wrapped lighting for billboard sprites (0 = hard lambert, 1 = fully wrapped)
        public int maxPointLights = 16;

        // [sky]
        public Color skyZenith = new Color(0.18f, 0.38f, 0.76f, 1f);
        public Color skyHorizon = new Color(0.78f, 0.80f, 0.84f, 1f);
        public Color skyGround = new Color(0.20f, 0.22f, 0.25f, 1f);

        // [fog]  (classic distance fog applied in the surface shaders)
        public bool fogEnabled = true;
        public Color fogColor = new Color(0.70f, 0.76f, 0.84f, 1f);
        public float fogStart = 150f;
        public float fogEnd = 700f;

        // [shadows]
        public bool shadowsEnabled = true;
        public int shadowResolution = 2048;
        public float shadowDistance = 70f;    // half-size of the shadow box around the camera
        public float shadowBias = 0.0015f;
        public float shadowNormalOffset = 0.05f;
        public float shadowSoftness = 1.0f;   // PCF kernel spread in texels
        public bool spriteShadows = true;

        // [volumetric_fog]
        public bool volumetricEnabled = true;
        public float volDensity = 0.012f;
        public float volHeight = 2.0f;         // height of full density
        public float volHeightFalloff = 0.08f; // density halves every ~8.7 units above volHeight when 0.08
        public Color volColor = new Color(0.80f, 0.85f, 0.95f, 1f);
        public float volSunScattering = 1.0f;
        public float volAnisotropy = 0.55f;    // Henyey-Greenstein g (0 = isotropic, 0.9 = strong forward shafts)
        public float volAmbient = 0.25f;
        public int volSteps = 16;
        public float volMaxDistance = 250f;
        public bool volShadows = true;
        public bool volPointLights = true;
        public float volScale = 0.5f;          // resolution of the march relative to the scene
        public float volDither = 1.0f;

        public Vector3 SunDirection
        {
            get
            {
                float el = sunElevation * Mathf.Deg2Rad, az = sunAzimuth * Mathf.Deg2Rad;
                // direction the light travels (from the sun towards the ground)
                return new Vector3(-Mathf.Sin(az) * Mathf.Cos(el), -Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el)).normalized;
            }
        }

        public void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            bool B(string sec, string key, bool cur) => Get(sections, sec, key, cur, v => v == "true" || v == "1" || v == "yes" || v == "on");
            float F(string sec, string key, float cur) => Get(sections, sec, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string sec, string key, int cur) => Get(sections, sec, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));
            Color C(string sec, string key, Color cur) => Get(sections, sec, key, cur, ParseColor);

            sunElevation = F("lighting", "sun_elevation", sunElevation);
            sunAzimuth = F("lighting", "sun_azimuth", sunAzimuth);
            sunColor = C("lighting", "sun_color", sunColor);
            sunIntensity = F("lighting", "sun_intensity", sunIntensity);
            ambientSky = C("lighting", "ambient_sky", ambientSky);
            ambientGround = C("lighting", "ambient_ground", ambientGround);
            specular = F("lighting", "specular", specular);
            spriteWrap = Mathf.Clamp01(F("lighting", "sprite_wrap", spriteWrap));
            maxPointLights = Mathf.Clamp(I("lighting", "max_point_lights", maxPointLights), 0, 16);

            skyZenith = C("sky", "zenith", skyZenith);
            skyHorizon = C("sky", "horizon", skyHorizon);
            skyGround = C("sky", "ground", skyGround);

            fogEnabled = B("fog", "enabled", fogEnabled);
            fogColor = C("fog", "color", fogColor);
            fogStart = F("fog", "start", fogStart);
            fogEnd = F("fog", "end", fogEnd);

            shadowsEnabled = B("shadows", "enabled", shadowsEnabled);
            shadowResolution = Mathf.Clamp(I("shadows", "resolution", shadowResolution), 256, 8192);
            shadowDistance = Mathf.Max(5f, F("shadows", "distance", shadowDistance));
            shadowBias = F("shadows", "bias", shadowBias);
            shadowNormalOffset = F("shadows", "normal_offset", shadowNormalOffset);
            shadowSoftness = Mathf.Clamp(F("shadows", "softness", shadowSoftness), 0f, 4f);
            spriteShadows = B("shadows", "sprite_shadows", spriteShadows);

            volumetricEnabled = B("volumetric_fog", "enabled", volumetricEnabled);
            volDensity = Mathf.Max(0f, F("volumetric_fog", "density", volDensity));
            volHeight = F("volumetric_fog", "height", volHeight);
            volHeightFalloff = Mathf.Max(0f, F("volumetric_fog", "height_falloff", volHeightFalloff));
            volColor = C("volumetric_fog", "color", volColor);
            volSunScattering = Mathf.Max(0f, F("volumetric_fog", "sun_scattering", volSunScattering));
            volAnisotropy = Mathf.Clamp(F("volumetric_fog", "anisotropy", volAnisotropy), -0.95f, 0.95f);
            volAmbient = Mathf.Max(0f, F("volumetric_fog", "ambient", volAmbient));
            volSteps = Mathf.Clamp(I("volumetric_fog", "steps", volSteps), 4, 64);
            volMaxDistance = Mathf.Max(1f, F("volumetric_fog", "max_distance", volMaxDistance));
            volShadows = B("volumetric_fog", "shadows", volShadows);
            volPointLights = B("volumetric_fog", "point_lights", volPointLights);
            volScale = Mathf.Clamp(F("volumetric_fog", "scale", volScale), 0.125f, 1f);
            volDither = Mathf.Clamp01(F("volumetric_fog", "dither", volDither));
        }

        private static T Get<T>(Dictionary<string, Dictionary<string, string>> sections, string sec, string key, T cur, Func<string, T> parse)
        {
            if (!sections.TryGetValue(sec, out var kv) || !kv.TryGetValue(key, out var v)) return cur;
            try { return parse(v.Trim().ToLowerInvariant()); }
            catch (Exception) { Debug.LogWarning($"tunables.cfg: bad value for [{sec}] {key} = '{v}'"); return cur; }
        }

        private static Color ParseColor(string v)
        {
            var p = v.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float r = float.Parse(p[0], CultureInfo.InvariantCulture), g = float.Parse(p[1], CultureInfo.InvariantCulture), b = float.Parse(p[2], CultureInfo.InvariantCulture);
            float a = p.Length > 3 ? float.Parse(p[3], CultureInfo.InvariantCulture) : 1f;
            return new Color(r, g, b, a);
        }
    }

    public enum LightType { Directional, Point }

    /// <summary>
    /// Scene light (UnityEngine.Light subset). One directional light overrides the sun from the
    /// config; point lights are gathered per frame (nearest <c>max_point_lights</c> to the camera).
    /// </summary>
    public sealed class Light : Behaviour
    {
        public LightType type = LightType.Point;
        public Color color = Color.white;
        public float intensity = 1f;
        public float range = 10f;

        private static readonly List<Light> All = new List<Light>();
        public static IReadOnlyList<Light> Lights => All;

        protected override void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        protected override void OnDisable() => All.Remove(this);
        protected override void OnDestroy() => All.Remove(this);
    }
}
