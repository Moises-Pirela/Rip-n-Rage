using System;
using System.Collections.Generic;
using System.Globalization;

namespace RipNRage.Engine.Graphics
{
    /// <summary>[camera], [viewmodel] and [culling] tunables.</summary>
    public sealed class CameraSettings
    {
        // [camera]
        /// <summary>World vertical FOV in degrees; 0 = keep whatever the game set on the camera.</summary>
        public float fov = 0f;
        public float near = 0f;   // 0 = keep
        public float far = 0f;

        // [viewmodel]
        public bool viewmodelEnabled = true;
        public float viewmodelFov = 77f;
        public float viewmodelNear = 0.02f;
        public float viewmodelFar = 50f;
        /// <summary>Light the weapon/hands with the scene lights (sun, point lights, muzzle flash) instead of drawing them unlit.</summary>
        public bool viewmodelLit = true;
        public float viewmodelWrap = 0.85f;
        /// <summary>Extra brightness multiplier for the lit viewmodel.</summary>
        public float viewmodelExposure = 1.15f;

        // [culling]
        public bool frustumCulling = true;
        public bool occlusionCulling = true;
        public int occlusionWidth = 192;
        public int occlusionHeight = 96;
        public bool cullLights = true;
        public bool occlusionDebugView = false;

        public void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            bool B(string sec, string key, bool cur) => Get(sections, sec, key, cur, v => v == "true" || v == "1" || v == "yes" || v == "on");
            float F(string sec, string key, float cur) => Get(sections, sec, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string sec, string key, int cur) => Get(sections, sec, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));

            fov = Mathf.Clamp(F("camera", "fov", fov), 0f, 170f);
            near = F("camera", "near", near);
            far = F("camera", "far", far);

            viewmodelEnabled = B("viewmodel", "enabled", viewmodelEnabled);
            viewmodelFov = Mathf.Clamp(F("viewmodel", "fov", viewmodelFov), 10f, 170f);
            viewmodelNear = Mathf.Max(0.001f, F("viewmodel", "near", viewmodelNear));
            viewmodelFar = Mathf.Max(viewmodelNear + 0.1f, F("viewmodel", "far", viewmodelFar));
            viewmodelLit = B("viewmodel", "lit", viewmodelLit);
            viewmodelWrap = Mathf.Clamp01(F("viewmodel", "wrap", viewmodelWrap));
            viewmodelExposure = Mathf.Max(0f, F("viewmodel", "exposure", viewmodelExposure));

            frustumCulling = B("culling", "frustum", frustumCulling);
            occlusionCulling = B("culling", "occlusion", occlusionCulling);
            occlusionWidth = Mathf.Clamp(I("culling", "buffer_width", occlusionWidth), 32, 1024);
            occlusionHeight = Mathf.Clamp(I("culling", "buffer_height", occlusionHeight), 16, 512);
            cullLights = B("culling", "lights", cullLights);
            occlusionDebugView = B("culling", "debug_view", occlusionDebugView);
        }

        private static T Get<T>(Dictionary<string, Dictionary<string, string>> sections, string sec, string key, T cur, Func<string, T> parse)
        {
            if (!sections.TryGetValue(sec, out var kv) || !kv.TryGetValue(key, out var v)) return cur;
            try { return parse(v.Trim().ToLowerInvariant()); }
            catch (Exception) { Debug.LogWarning($"tunables.cfg: bad value for [{sec}] {key} = '{v}'"); return cur; }
        }
    }
}
