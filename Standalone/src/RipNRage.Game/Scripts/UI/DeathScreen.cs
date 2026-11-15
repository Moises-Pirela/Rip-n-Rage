using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// The classic "YOU ARE DEAD" screen. Created the moment the player dies (while the death camera is
/// still falling): the screen darkens to blood red, the title fades in, then RESTART / MAIN MENU
/// appear and the mouse is released. RESTART respawns at the last checkpoint when the checkpoint save
/// belongs to this level, otherwise at the level start; both go through <see cref="GameSession"/>.
/// All timing is <c>Flow.DeathClock</c> (real seconds since the death — the death sequence slows the world down),
/// and the menu opens when the flow reaches Dead (8.2: the clock and the Dying → Dead rule are FlowSystem's).
/// </summary>
public sealed class DeathScreen : IUiScreen, IUiLayer
{
    private readonly ClayUi _clay = new ClayUi();
    public int UiLayer => UiLayers.Modal;

    [Header("Timing (seconds, real time)")]
    public float fadeDelay = 0.35f;
    public float fadeDuration = 1.6f;
    public float titleDelay = 1.1f;
    public float titleDuration = 1.0f;
    public float menuFadeDuration = 0.45f;

    public string titleText = "YOU ARE DEAD";
    /// <summary>Optional line under the title ("YOU FELL INTO THE HARBOUR"); taken from <see cref="NextCause"/> when the screen is created.</summary>
    public string causeText;
    /// <summary>Set by whatever kills the player right before it does, consumed by the next screen.</summary>
    public static string NextCause;

    private readonly MenuList _menu = new MenuList();
    private bool _menuReady;
    /// <summary>The flow's death clock: every fade on this screen is timed from it.</summary>
    private static float T => RipNRage.Game.Ecs.Flows.Current.DeathClock;
    /// <summary>The flow took a choice: the screen stays, frozen, until the scene goes (8.8b; was a local flag).</summary>
    private static bool Busy => RipNRage.Game.Ecs.Flows.Busy;
    private bool _fromCheckpoint;

    public static DeathScreen Current { get; private set; }

    /// <summary>Puts the screen up (drawn above everything through <see cref="UiLayers.Modal"/>); a plain screen since 8.4b,
    /// closed by the scene reset like every other.</summary>
    public static DeathScreen Show() => Current ?? UiScreens.Add(new DeathScreen());

    private DeathScreen()
    {
        Current = this;
        causeText = NextCause; NextCause = null;
        _fromCheckpoint = GameSession.CanRespawnAtCheckpoint;
        _menu.Add("RESTART", () => RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.Respawn), _fromCheckpoint ? "FROM THE LAST CHECKPOINT" : "FROM THE START OF THE LEVEL");
        _menu.Add("MAIN MENU", () => RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.MainMenu));
        _menu.selected = 0;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
    }

    public void Close()
    {
        _clay.Dispose();
        InputSystem.ReleaseUi(this);
        if (Current == this) Current = null;
    }

    public void Update()
    {
        if (!_menuReady && RipNRage.Game.Ecs.Flows.Current.Phase == RipNRage.Game.Ecs.FlowPhase.Dead)
        {
            _menuReady = true;
            InputSystem.AcquireUi(this);
            _menu.ResetMouse();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (_menuReady && !Busy)
        {
            var item = _menu.HandleKeyboard();
            if (item != null) Activate(item);
        }
    }

    private void Activate(MenuItem item)
    {
        if (Busy) return;
        Play(ProceduralSounds.MenuSelect);
        item.onActivate?.Invoke();
    }

    private void Play(AudioClip clip)
    {
        if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 0.8f);
    }

    public void DrawUi(UiBatch ui)
    {
        // 1. the world drains to blood red, then to near black (full-screen effects, outside the layout)
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((T - fadeDelay) / fadeDuration));
        if (fade > 0f)
        {
            var tint = Color.Lerp(new Color(0.45f, 0.02f, 0.02f, 0f), new Color(0.04f, 0f, 0f, 0.84f), fade);
            ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), tint);
            ui.DrawVignette(new Color(0.5f, 0f, 0f, 1f), fade);
        }

        var c = _clay;
        c.Begin(ui);
        // 2. the title settles in (slight shrink + fade), centred on 34% from the top
        float tt = Mathf.Clamp01((T - titleDelay) / titleDuration);
        if (tt > 0f)
        {
            float ease = 1f - (1f - tt) * (1f - tt);
            float scale = 11f + 3f * (1f - ease);
            using (c.Box(ClayScreen.FloatingColumn(0.34f * ClayScreen.RefHeight - scale * BitmapFont.Cell * 0.5f, "death-title", 10f)))
            {
                c.Text(titleText, ClayScreen.Line(scale, new Color(0.82f, 0.07f, 0.05f, ease), shadow: true));
                if (!string.IsNullOrEmpty(causeText))
                    c.Text(causeText, ClayScreen.Line(2.6f, new Color(0.85f, 0.6f, 0.55f, 0.85f * ease)));
            }
        }

        // 3. the choices, top at 46% of the height
        MenuItem clicked = null;
        if (_menuReady)
        {
            float ma = Mathf.Clamp01((T - RipNRage.Game.Ecs.Flows.DeathMenuDelay) / menuFadeDuration);
            using (c.Box(ClayScreen.FloatingColumn(0.54f * ClayScreen.RefHeight))) clicked = _menu.Build(c, ma);
            ClayScreen.Footer(c, "UP/DOWN OR MOUSE TO SELECT   ENTER / CLICK TO CONFIRM", 84f, ClayScreen.Line(2f, new Color(0.7f, 0.7f, 0.72f, 0.7f * ma)));
        }
        c.End();
        if (clicked != null && !Busy) Activate(clicked);
    }
}
