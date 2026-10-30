using System;
using RipNRage.Game;
using RipNRage.Game.Content;
using RipNRage.Engine.Graphics.Particles;

/// <summary>
/// Graphics settings screen: every visual effect as its own row (ON/OFF or a value cycled with Left/Right,
/// Enter or a click), applied live and saved to graphics.json as overrides on top of tunables.cfg. Shows the
/// real frame time while you toggle so the cost of each effect is visible immediately. Opened from the pause
/// menu and the title screen; Esc / BACK closes it.
/// </summary>
public class GraphicsMenu : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Modal + 10;

    public static GraphicsMenu Current { get; private set; }
    public static bool IsOpen => Current != null && !Current.IsDestroyed;

    public Action onClose;
    private readonly MenuList _menu = new MenuList();
    private AudioSource _audio;
    private float _anim;
    private bool _closed;

    public static GraphicsMenu Open(Action onClose)
    {
        if (IsOpen) return Current;
        var go = new GameObject("GraphicsMenu");
        var m = go.AddComponent<GraphicsMenu>();
        m.onClose = onClose;
        return m;
    }

    protected override void Awake()
    {
        Current = this;
        _audio = gameObject.AddComponent<AudioSource>();
        _menu.itemWidth = 820f; _menu.itemHeight = 42f; _menu.itemGap = 6f; _menu.labelScale = 2.8f; _menu.hintScale = 1.8f;
        _menu.visibleRows = 13;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
        _menu.onAdjusted = _ => { GraphicsSettings.Apply(); GraphicsSettings.Save(); Play(ProceduralSounds.MenuSelect); };
        Build();
        _menu.ResetMouse();
        InputSystem.AcquireUi(this);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    protected override void OnDestroy()
    {
        InputSystem.ReleaseUi(this);
        if (Current == this) Current = null;
    }

    private void Build()
    {
        var r = Application.Current.Renderer;
        var pp = r.PostFx.Settings;
        var lt = r.Lighting;
        var cam = r.CameraConfig;

        // ---- frame
        Toggle("POST-PROCESSING", () => r.PostFx.Enabled, v => GraphicsSettings.Current.postProcessing = v, "MASTER SWITCH FOR EVERY SCREEN EFFECT BELOW (F4)");
        Choice("RENDER SCALE", new[] { 0.5f, 0.6f, 0.75f, 0.85f, 1f }, () => pp.renderScale, v => GraphicsSettings.Current.renderScale = v, v => $"{v * 100f:0}%", "3D RESOLUTION RELATIVE TO THE WINDOW - THE BIGGEST SINGLE PERFORMANCE LEVER");
        Toggle("VSYNC", () => Application.Current.Window.VSync, v => GraphicsSettings.Current.vsync = v, "OFF = UNCAPPED FRAME RATE (TEARING POSSIBLE)");
        Toggle("HDR BUFFERS", () => pp.hdr, v => GraphicsSettings.Current.hdr = v, "16-BIT RENDER TARGETS: BRIGHTER BLOOM, SLOWER");
        // ---- lens
        Toggle("BLOOM", () => pp.bloomEnabled, v => GraphicsSettings.Current.bloom = v);
        Choice("BLOOM PASSES", new[] { 1, 2, 3, 4, 6 }, () => pp.bloomPasses, v => GraphicsSettings.Current.bloomPasses = v, v => v.ToString(), "MORE PASSES = WIDER, SOFTER GLOW");
        Toggle("CHROMATIC ABERRATION", () => pp.chromaticEnabled, v => GraphicsSettings.Current.chromaticAberration = v);
        Toggle("VIGNETTE", () => pp.vignetteEnabled, v => GraphicsSettings.Current.vignette = v);
        Toggle("MOTION BLUR", () => pp.motionBlurEnabled, v => GraphicsSettings.Current.motionBlur = v, "CAMERA MOTION BLUR FROM DEPTH REPROJECTION");
        // ---- atmosphere
        Toggle("VOLUMETRIC FOG", () => lt.volumetricEnabled, v => GraphicsSettings.Current.volumetricFog = v, "RAY-MARCHED HEIGHT FOG WITH LIGHT SHAFTS");
        Toggle("LIGHT SHAFTS", () => lt.volShadows, v => GraphicsSettings.Current.volumetricShadows = v, "FOG SAMPLES THE SHADOW MAP (GOD RAYS); OFF = NO RAY MARCH AT ALL");
        Choice("FOG QUALITY", new[] { 6, 10, 16, 24 }, () => lt.volSteps, v => GraphicsSettings.Current.volumetricSteps = v, v => $"{v} STEPS", "SHADOW SAMPLES PER PIXEL ALONG THE RAY");
        Choice("FOG RESOLUTION", new[] { 0.25f, 0.35f, 0.5f, 1f }, () => lt.volScale, v => GraphicsSettings.Current.volumetricScale = v, v => $"{v * 100f:0}%", "THE FOG IS UPSAMPLED DEPTH-AWARE, 25-35% LOOKS FINE");
        Toggle("DISTANCE FOG", () => lt.fogEnabled, v => GraphicsSettings.Current.distanceFog = v);
        // ---- lighting & shadows
        Toggle("SHADOWS", () => lt.shadowsEnabled, v => GraphicsSettings.Current.shadows = v, "SUN SHADOW MAP (STATIC GEOMETRY IS CACHED)");
        Choice("SHADOW RESOLUTION", new[] { 512, 1024, 2048, 4096 }, () => lt.shadowResolution, v => GraphicsSettings.Current.shadowResolution = v, v => v.ToString());
        Toggle("SOFT SHADOWS", () => lt.shadowSoftness > 0f, v => GraphicsSettings.Current.softShadows = v, "4 HARDWARE PCF TAPS INSTEAD OF 1");
        Toggle("SPRITE SHADOWS", () => lt.spriteShadows, v => GraphicsSettings.Current.spriteShadows = v, "ENEMIES AND PICKUPS CAST SHADOWS");
        Choice("LIGHTS PER OBJECT", new[] { 0, 2, 4, 6, 8, 16 }, () => lt.lightsPerDraw, v => GraphicsSettings.Current.lightsPerDraw = v, v => v == 0 ? "ALL" : v.ToString(), "POINT LIGHTS EVALUATED PER PIXEL (ONLY THOSE TOUCHING THE OBJECT)");
        Toggle("SPECULAR HIGHLIGHTS", () => lt.specular > 0f, v => GraphicsSettings.Current.specular = v);
        Toggle("LIT WEAPON", () => cam.viewmodelLit, v => GraphicsSettings.Current.viewmodelLit = v, "FIRST-PERSON WEAPON SHADED BY THE SCENE LIGHTS");
        // ---- effects
        Choice("PARTICLE DENSITY", new[] { 0.25f, 0.5f, 1f, 1.5f }, () => ParticleWorld.Density, v => GraphicsSettings.Current.particleDensity = v, v => $"{v * 100f:0}%", "SCALES EVERY BURST AND EMISSION RATE");
        Toggle("RAGE SCREEN EFFECTS", () => GraphicsSettings.RageEffectsEnabled, v => GraphicsSettings.Current.rageEffects = v, "RED TINT / VIGNETTE / ABERRATION WHEN ENRAGED");
        // ---- culling / tools
        ChoiceStr("OCCLUSION CULLING", new[] { "gpu", "cpu", "off" }, () => cam.occlusionMode.ToString().ToLowerInvariant(), v => GraphicsSettings.Current.occlusion = v, "GPU = HARDWARE QUERIES, CPU = SOFTWARE RASTERIZER");
        Choice("PERFORMANCE OVERLAY", new[] { 0, 1, 2 }, () => OverlayMode(), v => { GraphicsSettings.Current.overlayMode = v; SetOverlayMode(v); }, v => v == 0 ? "OFF" : v == 1 ? "FRAME STATS" : "FULL PROFILER", "F3 CYCLES THIS IN GAME");

        _menu.Add("RESET TO CONFIG DEFAULTS", () => { GraphicsSettings.ResetToConfig(); Play(ProceduralSounds.MenuBack); }, "FORGET EVERY CHOICE MADE HERE AND USE TUNABLES.CFG");
        _menu.Add("BACK", Close);
        _menu.selected = 0;
    }

    // ------------------------------------------------------------------ row builders

    private void Toggle(string label, Func<bool> get, Action<bool> set, string hint = null)
    {
        _menu.AddSetting(label, () => get() ? "ON" : "OFF", _ => set(!get()), hint);
    }

    private void Choice<T>(string label, T[] options, Func<T> get, Action<T> set, Func<T, string> fmt, string hint = null) where T : struct
    {
        _menu.AddSetting(label, () => fmt(get()), dir =>
        {
            int i = Nearest(options, get());
            i = Mathf.Clamp(i + dir, 0, options.Length - 1);
            set(options[i]);
        }, hint);
    }

    private void ChoiceStr(string label, string[] options, Func<string> get, Action<string> set, string hint = null)
    {
        _menu.AddSetting(label, () => get().ToUpperInvariant(), dir =>
        {
            int i = Math.Max(0, Array.IndexOf(options, get()));
            i = ((i + dir) % options.Length + options.Length) % options.Length;
            set(options[i]);
        }, hint);
    }

    private static int Nearest<T>(T[] options, T value) where T : struct
    {
        double v = Convert.ToDouble(value);
        int best = 0; double bestD = double.MaxValue;
        for (int i = 0; i < options.Length; i++)
        {
            double d = Math.Abs(Convert.ToDouble(options[i]) - v);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static int OverlayMode()
    {
        var o = Scene.Current?.FindObjectOfType<DebugOverlay>();
        return o != null ? o.mode : GraphicsSettings.Current.overlayMode ?? 2;
    }

    private static void SetOverlayMode(int v)
    {
        var o = Scene.Current?.FindObjectOfType<DebugOverlay>();
        if (o != null) o.mode = v;
    }

    // ------------------------------------------------------------------ per frame

    protected override void Update()
    {
        _anim = Mathf.MoveTowards(_anim, 1f, Time.unscaledDeltaTime / 0.15f);
        if (_closed) return;
        if (InputSystem.UI["cancel"].Pressed || Input.GetKeyDown(KeyCode.Backspace)) { Close(); return; }
        var item = _menu.HandleKeyboard();
        if (item != null && !item.IsSetting) { Play(ProceduralSounds.MenuSelect); item.onActivate?.Invoke(); }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        GraphicsSettings.Save();
        Play(ProceduralSounds.MenuBack);
        GameObject.Destroy(gameObject); // Current stays set until the object is gone, so parents ignore this frame's Esc
        onClose?.Invoke();
    }

    private void Play(AudioClip clip)
    {
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, 0.6f);
    }

    public void DrawUi(UiBatch ui)
    {
        float s = ui.Scale;
        float a = _anim;
        float cx = ui.Width * 0.5f;
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.7f * a));

        float top = ui.Height * 0.93f;
        float th = MenuList.DrawCentered(ui, "GRAPHICS", cx, top, 7f * s, new Color(0.95f, 0.85f, 0.55f, a), shadow: true);
        // live cost readout: the point of toggling things is seeing what they cost
        string perf = $"FPS {Profiler.Fps:0.0}   FRAME {Profiler.FrameMsAvg:0.0} MS   1% LOW {Profiler.FpsLow1:0.0}" + (Profiler.GpuTimersAvailable ? $"   GPU {Profiler.GpuFrameMsAvg:0.0} MS" : "");
        Color pc = Profiler.FrameMsAvg > 33.4 ? new Color(1f, 0.45f, 0.35f, a) : Profiler.FrameMsAvg > 16.8 ? new Color(1f, 0.85f, 0.4f, a) : new Color(0.6f, 1f, 0.6f, a);
        float ph = MenuList.DrawCentered(ui, perf, cx, top - th - 6f * s, 2.4f * s, pc);
        if (RipNRage.Engine.Platform.GpuInfo.IsSoftware)
            MenuList.DrawCentered(ui, "SOFTWARE RENDERER: THESE EFFECTS RUN ON THE CPU", cx, top - th - ph - 10f * s, 2f * s, new Color(1f, 0.5f, 0.4f, a));

        float menuTop = top - th - ph - 30f * s;
        var clicked = _menu.Draw(ui, cx, menuTop, a);
        if (clicked != null && !_closed && !clicked.IsSetting) { Play(ProceduralSounds.MenuSelect); clicked.onActivate?.Invoke(); }

        MenuList.DrawCentered(ui, "LEFT/RIGHT OR CLICK CHANGE   UP/DOWN SELECT   ESC BACK   CHANGES APPLY IMMEDIATELY AND ARE SAVED", cx, 60f * s, 2f * s, new Color(0.7f, 0.7f, 0.72f, 0.7f * a));
    }
}
