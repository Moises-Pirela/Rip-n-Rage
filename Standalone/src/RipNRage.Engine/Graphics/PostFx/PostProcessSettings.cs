using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RipNRage.Engine.Graphics.PostFx
{
    /// <summary>All post-processing tunables. Loaded from <c>tunables.cfg</c> (see <see cref="TunablesFile"/>).</summary>
    public sealed class PostProcessSettings
    {
        // [postfx]
        public bool enabled = true;
        /// <summary>Resolution of the 3D scene relative to the window (1 = native). Lower = faster on software/weak GPUs.</summary>
        public float renderScale = 1f;
        /// <summary>Render-scale for the bloom buffers (0.25 = quarter res; cheap and soft).</summary>
        public float bloomScale = 0.25f;
        /// <summary>Use RGBA16F targets (needed only for HDR content; slower on software renderers).</summary>
        public bool hdr = false;

        /// <summary>True when at least one effect or colour adjustment would change the image.</summary>
        public bool AnyEffect =>
            bloomEnabled || chromaticEnabled || vignetteEnabled || motionBlurEnabled ||
            exposure != 1f || saturation != 1f || contrast != 1f || renderScale < 0.999f;

        // [bloom]
        public bool bloomEnabled = true;
        public float bloomThreshold = 0.8f;
        public float bloomSoftKnee = 0.5f;
        public float bloomIntensity = 0.7f;
        public float bloomRadius = 1.0f;
        public int bloomPasses = 3;

        // [chromatic_aberration]
        public bool chromaticEnabled = true;
        public float chromaticIntensity = 0.5f;
        public bool chromaticRadial = true;

        // [vignette]
        public bool vignetteEnabled = true;
        public float vignetteIntensity = 0.35f;
        public float vignetteSmoothness = 0.5f;
        public float vignetteRoundness = 1.0f;
        public Color vignetteColor = Color.black;

        // [motion_blur]
        public bool motionBlurEnabled = true;
        public float motionBlurIntensity = 0.6f;
        public int motionBlurSamples = 6;
        public float motionBlurMaxVelocity = 0.04f;

        // [color]
        public float exposure = 1.0f;
        public float saturation = 1.0f;
        public float contrast = 1.0f;

        public PostProcessSettings Clone() => (PostProcessSettings)MemberwiseClone();

        /// <summary>Applies key/value pairs grouped by section (case-insensitive).</summary>
        public void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            bool B(string sec, string key, bool cur) => Get(sections, sec, key, cur, v => v == "true" || v == "1" || v == "yes" || v == "on");
            float F(string sec, string key, float cur) => Get(sections, sec, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string sec, string key, int cur) => Get(sections, sec, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));
            Color C(string sec, string key, Color cur) => Get(sections, sec, key, cur, ParseColor);

            enabled = B("postfx", "enabled", enabled);
            renderScale = Mathf.Clamp(F("postfx", "render_scale", renderScale), 0.25f, 1f);
            bloomScale = Mathf.Clamp(F("postfx", "bloom_scale", bloomScale), 0.125f, 1f);
            hdr = B("postfx", "hdr", hdr);

            bloomEnabled = B("bloom", "enabled", bloomEnabled);
            bloomThreshold = F("bloom", "threshold", bloomThreshold);
            bloomSoftKnee = Mathf.Clamp01(F("bloom", "soft_knee", bloomSoftKnee));
            bloomIntensity = F("bloom", "intensity", bloomIntensity);
            bloomRadius = F("bloom", "radius", bloomRadius);
            bloomPasses = Mathf.Clamp(I("bloom", "passes", bloomPasses), 1, 8);

            chromaticEnabled = B("chromatic_aberration", "enabled", chromaticEnabled);
            chromaticIntensity = F("chromatic_aberration", "intensity", chromaticIntensity);
            chromaticRadial = B("chromatic_aberration", "radial", chromaticRadial);

            vignetteEnabled = B("vignette", "enabled", vignetteEnabled);
            vignetteIntensity = F("vignette", "intensity", vignetteIntensity);
            vignetteSmoothness = Mathf.Clamp(F("vignette", "smoothness", vignetteSmoothness), 0.01f, 1f);
            vignetteRoundness = Mathf.Clamp(F("vignette", "roundness", vignetteRoundness), 0f, 1f);
            vignetteColor = C("vignette", "color", vignetteColor);


            motionBlurEnabled = B("motion_blur", "enabled", motionBlurEnabled);
            motionBlurIntensity = F("motion_blur", "intensity", motionBlurIntensity);
            motionBlurSamples = Mathf.Clamp(I("motion_blur", "samples", motionBlurSamples), 2, 32);
            motionBlurMaxVelocity = F("motion_blur", "max_velocity", motionBlurMaxVelocity);

            exposure = F("color", "exposure", exposure);
            saturation = F("color", "saturation", saturation);
            contrast = F("color", "contrast", contrast);
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

    /// <summary>
    /// Per-frame additive override pushed by gameplay (the rage "volumes"): extra vignette,
    /// extra chromatic aberration and a multiplicative colour filter. Reset every frame.
    /// </summary>
    public struct PostProcessVolumeOverride
    {
        public float vignetteIntensity;
        public Color vignetteColor;
        public float chromaticIntensity;
        public Color colorFilter;
        public float bloomIntensity;

        public static PostProcessVolumeOverride None => new PostProcessVolumeOverride { colorFilter = Color.white, vignetteColor = Color.black };
    }

    /// <summary>
    /// INI-style <c>tunables.cfg</c> loader with hot reload: the file's modification time is polled and
    /// the settings are re-applied whenever it changes, so values can be tweaked while the game runs.
    /// </summary>
    public sealed class TunablesFile
    {
        public string Path { get; private set; }
        public PostProcessSettings Settings { get; } = new PostProcessSettings();
        public Lighting.LightingSettings Lighting { get; } = new Lighting.LightingSettings();
        public CameraSettings Camera { get; } = new CameraSettings();
        public event Action<PostProcessSettings> Reloaded;
        private DateTime _lastWrite;
        private double _nextPoll;

        public static string Locate(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)) return System.IO.Path.GetFullPath(explicitPath);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_TUNABLES");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = System.IO.Path.Combine(dir.FullName, "tunables.cfg");
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        public TunablesFile(string path)
        {
            Path = path;
            Reload(initial: true);
        }

        public bool Reload(bool initial = false)
        {
            if (Path == null || !File.Exists(Path)) return false;
            try
            {
                var sections = Parse(File.ReadAllText(Path));
                var fresh = new PostProcessSettings();
                fresh.Apply(sections);
                CopyInto(fresh, Settings);
                var freshLighting = new Lighting.LightingSettings();
                freshLighting.Apply(sections);
                foreach (var f in typeof(Lighting.LightingSettings).GetFields()) f.SetValue(Lighting, f.GetValue(freshLighting));
                var freshCamera = new CameraSettings();
                freshCamera.Apply(sections);
                foreach (var f in typeof(CameraSettings).GetFields()) f.SetValue(Camera, f.GetValue(freshCamera));
                MaterialLibrary.ApplyConfig(sections);
                _lastWrite = File.GetLastWriteTimeUtc(Path);
                if (!initial) Debug.LogWarning("tunables.cfg reloaded");
                Reloaded?.Invoke(Settings);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("tunables.cfg: failed to read: " + e.Message);
                return false;
            }
        }

        /// <summary>Call once per frame; reloads when the file changed on disk.</summary>
        public void Poll(double now)
        {
            if (Path == null || now < _nextPoll) return;
            _nextPoll = now + 0.5;
            try
            {
                if (File.Exists(Path) && File.GetLastWriteTimeUtc(Path) != _lastWrite) Reload();
            }
            catch (IOException) { }
        }

        private static void CopyInto(PostProcessSettings from, PostProcessSettings to)
        {
            foreach (var f in typeof(PostProcessSettings).GetFields()) f.SetValue(to, f.GetValue(from));
        }

        public static Dictionary<string, Dictionary<string, string>> Parse(string text)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (var raw in text.Split('\n'))
            {
                string line = raw;
                int c = line.IndexOfAny(new[] { '#', ';' });
                if (c >= 0) line = line.Substring(0, c);
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[' && line[line.Length - 1] == ']') { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                if (!result.TryGetValue(section, out var kv)) result[section] = kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }
    }
}
