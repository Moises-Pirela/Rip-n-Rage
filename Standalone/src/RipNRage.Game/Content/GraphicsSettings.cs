using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Engine.Graphics.Particles;
using RipNRage.Engine.Graphics.PostFx;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// The player's graphics choices: every entry is an <b>override</b> on top of <c>tunables.cfg</c> (null = use
    /// the config value). Stored as JSON in <c>&lt;persistentDataPath&gt;/graphics.json</c>, re-applied whenever the
    /// config hot-reloads, so the menu's choices stick while designers keep tuning the file underneath.
    /// </summary>
    public sealed class GraphicsSettings
    {
        // display (window mode, monitor, fullscreen resolution, HiDPI)
        public string displayMode;        // "windowed" | "borderless" | "fullscreen"
        public int? monitor;              // monitor index for borderless / fullscreen
        public int? fullscreenWidth;      // exclusive fullscreen mode (null = desktop mode)
        public int? fullscreenHeight;
        public int? refreshRate;
        public bool? hiDpi;               // 3D at native HiDPI pixels (true) or at the logical window size (false)
        // master / frame
        public bool? postProcessing;      // whole post-fx stack (lens + world effects)
        public float? renderScale;        // 3D resolution relative to the window
        public bool? vsync;
        public bool? hdr;
        // lens effects
        public bool? bloom;
        public int? bloomPasses;
        public bool? chromaticAberration;
        public bool? vignette;
        public bool? motionBlur;
        // atmosphere
        public bool? volumetricFog;
        public bool? volumetricShadows;   // light shafts
        public int? volumetricSteps;
        public float? volumetricScale;
        public bool? distanceFog;
        // lighting & shadows
        public bool? shadows;
        public int? shadowResolution;
        public bool? softShadows;
        public bool? spriteShadows;
        public int? lightsPerDraw;
        public bool? viewmodelLit;
        public bool? specular;
        // effects
        public float? particleDensity;
        public bool? rageEffects;         // the red rage tint / vignette pushed by gameplay
        // culling / misc
        public string occlusion;          // "gpu" | "cpu" | "off"
        public bool? frustumCulling;
        public int? overlayMode;          // debug overlay default (0 off, 1 compact, 2 full)

        public static GraphicsSettings Current { get; private set; } = new GraphicsSettings();
        public static string FilePath => Path.Combine(Application.persistentDataPath, "graphics.json");
        /// <summary>Gameplay-side flag read by RagePostprocess.</summary>
        public static bool RageEffectsEnabled => Current.rageEffects ?? true;

        /// <summary>Default for <see cref="hiDpi"/>: Retina Macs render the 3D at point resolution (4x fewer pixels; the UI stays sharp).</summary>
        public static bool DefaultHiDpi => !RipNRage.Engine.Platform.Window.IsMacOS;
        public static bool HiDpiEnabled => Current.hiDpi ?? DefaultHiDpi;

        public static RipNRage.Engine.Platform.DisplayMode ParseMode(string s) => s switch
        {
            "borderless" => RipNRage.Engine.Platform.DisplayMode.Borderless,
            "fullscreen" => RipNRage.Engine.Platform.DisplayMode.Fullscreen,
            _ => RipNRage.Engine.Platform.DisplayMode.Windowed,
        };
        public static string ModeName(RipNRage.Engine.Platform.DisplayMode m) => m.ToString().ToLowerInvariant();

        /// <summary>Copies the saved display mode into the start-up options, so the window opens straight in it.</summary>
        public static void ApplyStartupDisplay(ApplicationOptions options)
        {
            Load();
            var g = Current;
            if (string.IsNullOrEmpty(g.displayMode)) return;
            options.DisplayMode = ParseMode(g.displayMode);
            if (g.monitor.HasValue && options.Monitor < 0) options.Monitor = g.monitor.Value;
            if (options.FullscreenWidth == 0 && g.fullscreenWidth.HasValue && g.fullscreenHeight.HasValue)
            {
                options.FullscreenWidth = g.fullscreenWidth.Value; options.FullscreenHeight = g.fullscreenHeight.Value;
                options.RefreshRate = g.refreshRate ?? 0;
            }
        }

        /// <summary>Switches the window to the saved display mode / monitor / resolution (menu changes).</summary>
        public static void ApplyDisplay()
        {
            var w = Application.Current?.Window;
            if (w == null) return;
            var g = Current;
            var mode = string.IsNullOrEmpty(g.displayMode) ? w.Mode : ParseMode(g.displayMode);
            int mon = g.monitor ?? -1;
            int fw = 0, fh = 0, hz = 0;
            if (mode == RipNRage.Engine.Platform.DisplayMode.Fullscreen && g.fullscreenWidth.HasValue && g.fullscreenHeight.HasValue)
            { fw = g.fullscreenWidth.Value; fh = g.fullscreenHeight.Value; hz = g.refreshRate ?? 0; }
            w.SetDisplayMode(mode, mon, fw, fh, hz);
            ApplyPixelScale();
        }

        /// <summary>HiDPI off: the 3D renders at window units (1/2 the pixels per axis on Retina) and is upsampled.</summary>
        public static void ApplyPixelScale()
        {
            var app = Application.Current;
            if (app == null) return;
            float cs = app.Window.PixelRatio > 1.01f ? app.Window.PixelRatio : app.Window.ContentScale;
            app.Renderer.PostFx.PixelScale = HiDpiEnabled || cs <= 1.01f ? 1f : 1f / cs;
        }

        private static bool _installed;

        /// <summary>Loads the file and keeps the overrides applied (also after every tunables.cfg reload). Call once after the Application exists.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Load();
            Apply();
            var t = Application.Current?.Tunables;
            if (t != null) t.Reloaded += _ => Apply();
            var win = Application.Current?.Window;
            if (win != null)
            {
                win.ContentScaleChanged += _ => ApplyPixelScale();
                win.Resized += (_, _) => ApplyPixelScale();
                // Alt+Enter / F11 / Cmd+Ctrl+F: remember the result like a menu change
                win.DisplayModeChanged += m => { if (Current.displayMode != ModeName(m)) { Current.displayMode = ModeName(m); Save(); } };
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var g = JsonSerializer.Deserialize<GraphicsSettings>(File.ReadAllText(FilePath), EngineJson.Options);
                    if (g != null) Current = g;
                }
            }
            catch (Exception e) { Debug.LogWarning("graphics.json: " + e.Message); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, EngineJson.Options));
            }
            catch (Exception e) { Debug.LogWarning("graphics.json: could not save: " + e.Message); }
        }

        /// <summary>Forgets every effect override (display mode / resolution are kept): the config file's values apply again.</summary>
        public static void ResetToConfig()
        {
            var old = Current;
            // display choices aren't tunables.cfg values: keep them
            Current = new GraphicsSettings
            {
                displayMode = old.displayMode, monitor = old.monitor, fullscreenWidth = old.fullscreenWidth,
                fullscreenHeight = old.fullscreenHeight, refreshRate = old.refreshRate, hiDpi = old.hiDpi,
            };
            Save();
            Application.Current?.Tunables?.Reload();
            Apply();
        }

        /// <summary>Pushes the overrides into the live renderer settings.</summary>
        public static void Apply()
        {
            var app = Application.Current;
            if (app == null) return;
            var g = Current;
            var pp = app.Renderer.PostFx.Settings;
            var lt = app.Renderer.Lighting;
            var cam = app.Renderer.CameraConfig;

            if (g.postProcessing.HasValue) app.Renderer.PostFx.Enabled = g.postProcessing.Value;
            if (g.renderScale.HasValue) pp.renderScale = Mathf.Clamp(g.renderScale.Value, 0.25f, 1f);
            ApplyPixelScale();
            if (g.vsync.HasValue && app.Window.VSync != g.vsync.Value) app.Window.SetVSync(g.vsync.Value);
            if (g.hdr.HasValue) pp.hdr = g.hdr.Value;
            if (g.bloom.HasValue) pp.bloomEnabled = g.bloom.Value;
            if (g.bloomPasses.HasValue) pp.bloomPasses = Mathf.Clamp(g.bloomPasses.Value, 1, 8);
            if (g.chromaticAberration.HasValue) pp.chromaticEnabled = g.chromaticAberration.Value;
            if (g.vignette.HasValue) pp.vignetteEnabled = g.vignette.Value;
            if (g.motionBlur.HasValue) pp.motionBlurEnabled = g.motionBlur.Value;
            if (g.volumetricFog.HasValue) lt.volumetricEnabled = g.volumetricFog.Value;
            if (g.volumetricShadows.HasValue) lt.volShadows = g.volumetricShadows.Value;
            if (g.volumetricSteps.HasValue) lt.volSteps = Mathf.Clamp(g.volumetricSteps.Value, 4, 64);
            if (g.volumetricScale.HasValue) lt.volScale = Mathf.Clamp(g.volumetricScale.Value, 0.125f, 1f);
            if (g.distanceFog.HasValue) lt.fogEnabled = g.distanceFog.Value;
            if (g.shadows.HasValue) lt.shadowsEnabled = g.shadows.Value;
            if (g.shadowResolution.HasValue) lt.shadowResolution = Mathf.Clamp(g.shadowResolution.Value, 256, 8192);
            if (g.softShadows.HasValue) lt.shadowSoftness = g.softShadows.Value ? Mathf.Max(lt.shadowSoftness, 1f) : 0f;
            if (g.spriteShadows.HasValue) lt.spriteShadows = g.spriteShadows.Value;
            if (g.lightsPerDraw.HasValue) lt.lightsPerDraw = Mathf.Clamp(g.lightsPerDraw.Value, 0, 16);
            if (g.specular.HasValue) lt.specular = g.specular.Value ? (lt.specular > 0f ? lt.specular : 0.25f) : 0f;
            if (g.viewmodelLit.HasValue) cam.viewmodelLit = g.viewmodelLit.Value;
            if (g.particleDensity.HasValue) ParticleWorld.Density = Mathf.Clamp(g.particleDensity.Value, 0f, 2f);
            if (!string.IsNullOrEmpty(g.occlusion))
                cam.occlusionMode = g.occlusion == "cpu" ? OcclusionMode.Cpu : g.occlusion == "off" ? OcclusionMode.Off : OcclusionMode.Gpu;
            if (g.frustumCulling.HasValue) cam.frustumCulling = g.frustumCulling.Value;
        }
    }
}
