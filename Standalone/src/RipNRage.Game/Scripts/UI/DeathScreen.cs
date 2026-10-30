using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// The classic "YOU ARE DEAD" screen. Created the moment the player dies (while the death camera is
/// still falling): the screen darkens to blood red, the title fades in, then RESTART / MAIN MENU
/// appear and the mouse is released. RESTART respawns at the last checkpoint when the checkpoint save
/// belongs to this level, otherwise at the level start; both go through <see cref="GameSession"/>.
/// All timing uses unscaled time because the death sequence slows the world down.
/// </summary>
public class DeathScreen : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Modal;

    [Header("Timing (seconds, real time)")]
    public float fadeDelay = 0.35f;
    public float fadeDuration = 1.6f;
    public float titleDelay = 1.1f;
    public float titleDuration = 1.0f;
    public float menuDelay = 2.4f;
    public float menuFadeDuration = 0.45f;

    public string titleText = "YOU ARE DEAD";
    /// <summary>Optional line under the title ("YOU FELL INTO THE HARBOUR"); taken from <see cref="NextCause"/> when the screen is created.</summary>
    public string causeText;
    /// <summary>Set by whatever kills the player right before it does, consumed by the next screen.</summary>
    public static string NextCause;

    private readonly MenuList _menu = new MenuList();
    private AudioSource _audio;
    private float _t;
    private bool _menuReady;
    private bool _done;
    private bool _fromCheckpoint;

    public static DeathScreen Current { get; private set; }

    /// <summary>Creates the screen on its own object (drawn above everything through <see cref="UiLayers.Modal"/>).</summary>
    public static DeathScreen Show()
    {
        if (Current != null && !Current.IsDestroyed) return Current;
        var go = new GameObject("DeathScreen");
        return go.AddComponent<DeathScreen>();
    }

    protected override void Awake()
    {
        Current = this;
        causeText = NextCause; NextCause = null;
        _audio = gameObject.AddComponent<AudioSource>();
        _fromCheckpoint = GameSession.CanRespawnAtCheckpoint;
        _menu.Add("RESTART", GameSession.Respawn, _fromCheckpoint ? "FROM THE LAST CHECKPOINT" : "FROM THE START OF THE LEVEL");
        _menu.Add("MAIN MENU", () => GameSession.ShowMainMenu());
        _menu.selected = 0;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
    }

    protected override void OnDestroy()
    {
        InputSystem.ReleaseUi(this);
        if (Current == this) Current = null;
    }

    protected override void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (!_menuReady && _t >= menuDelay)
        {
            _menuReady = true;
            InputSystem.AcquireUi(this);
            _menu.ResetMouse();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (_menuReady && !_done)
        {
            var item = _menu.HandleKeyboard();
            if (item != null) Activate(item);
        }
    }

    private void Activate(MenuItem item)
    {
        if (_done) return;
        _done = true;
        _menu.interactable = false;
        Play(ProceduralSounds.MenuSelect);
        item.onActivate?.Invoke();
    }

    private void Play(AudioClip clip)
    {
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, 0.8f);
    }

    public void DrawUi(UiBatch ui)
    {
        float s = ui.Scale;
        float cx = ui.Width * 0.5f;

        // 1. the world drains to blood red, then to near black
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_t - fadeDelay) / fadeDuration));
        if (fade > 0f)
        {
            var tint = Color.Lerp(new Color(0.45f, 0.02f, 0.02f, 0f), new Color(0.04f, 0f, 0f, 0.84f), fade);
            ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), tint);
            ui.DrawVignette(new Color(0.5f, 0f, 0f, 1f), fade);
        }

        // 2. the title settles in (slight shrink + fade)
        float tt = Mathf.Clamp01((_t - titleDelay) / titleDuration);
        if (tt > 0f)
        {
            float ease = 1f - (1f - tt) * (1f - tt);
            float scale = (11f + 3f * (1f - ease)) * s;
            float top = ui.Height * 0.66f + ui.MeasureText(titleText, scale).y * 0.5f;
            MenuList.DrawCentered(ui, titleText, cx, top, scale, new Color(0.82f, 0.07f, 0.05f, ease), shadow: true);
            if (!string.IsNullOrEmpty(causeText))
                MenuList.DrawCentered(ui, causeText, cx, top - ui.MeasureText(titleText, scale).y - 10f * s, 2.6f * s, new Color(0.85f, 0.6f, 0.55f, 0.85f * ease));
        }

        // 3. the choices
        if (_menuReady)
        {
            float ma = Mathf.Clamp01((_t - menuDelay) / menuFadeDuration);
            var clicked = _menu.Draw(ui, cx, ui.Height * 0.46f, ma);
            if (clicked != null && !_done) Activate(clicked);
            MenuList.DrawCentered(ui, "UP/DOWN OR MOUSE TO SELECT   ENTER / CLICK TO CONFIRM", cx, 84f * s, 2f * s, new Color(0.7f, 0.7f, 0.72f, 0.7f * ma));
        }
    }
}
