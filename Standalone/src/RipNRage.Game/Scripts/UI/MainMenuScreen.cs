using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// Title screen: NEW GAME / CONTINUE / QUIT over an attract-mode view of the level (a menu camera entity, 9.3).
/// A plain screen (8.8), put up by <see cref="GameSession.ShowMainMenu"/>; every choice goes back through
/// <see cref="GameSession"/>, which rebuilds the scene at the start of the next frame — the scene reset closes it.
/// </summary>
public sealed class MainMenuScreen : IUiScreen, IUiLayer
{
    private readonly ClayUi _clay = new ClayUi();
    public int UiLayer => UiLayers.Menu;

    public string title = "RIP-N-RAGE";
    public string subtitle = "A FAST FIRST-PERSON SHOOTER";
    public float fadeInDuration = 0.9f;

    private readonly MenuList _menu = new MenuList();
    private float _t;
    private bool _done;
    private string _continueSlot;

    public MainMenuScreen()
    {
        _continueSlot = GameSaveSystem.ContinueSlot();
        bool canContinue = _continueSlot != null;

        _menu.Add("NEW GAME", () => RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.NewGame), "START FROM THE BEGINNING");
        _menu.Add("CONTINUE", () => RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.Continue),
            canContinue ? (_continueSlot == "checkpoint" ? "FROM THE LAST CHECKPOINT" : "FROM THE QUICK SAVE") : "NO SAVED GAME",
            canContinue);
        _menu.Add("GRAPHICS", OpenGraphics, "TOGGLE EVERY VISUAL EFFECT");
        _menu.Add("CONTROLS", OpenControls, "REBIND KEYS, MOUSE AND GAMEPAD");
        _menu.Add("QUIT", () => RipNRage.Game.Ecs.Flows.Ask(RipNRage.Game.Ecs.FlowAction.Quit));
        _menu.selected = 0;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
        _menu.ResetMouse();
        InputSystem.AcquireUi(this);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        _clay.Dispose();
        InputSystem.ReleaseUi(this);
    }

    public void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (SubmenuOpen) return;
        _menu.interactable = !RipNRage.Game.Ecs.Flows.Busy; // a move is pending: the first choice won (8.8b)
        if (_done || !_menu.interactable || _t < fadeInDuration * 0.5f) return;
        var item = _menu.HandleKeyboard();
        if (item != null) Activate(item);
    }

    /// <summary>A choice: the transitions ask the flow (which takes the first and refuses the rest); the submenus
    /// lock this screen until they close.</summary>
    private void Activate(MenuItem item)
    {
        if (_done || RipNRage.Game.Ecs.Flows.Busy) return;
        Play(ProceduralSounds.MenuSelect);
        item.onActivate?.Invoke();
    }

    private GraphicsMenu _graphics;
    private ControlsMenu _controls;

    private void OpenControls()
    {
        _done = true;
        _controls = ControlsMenu.Open(() =>
        {
            _controls = null;
            _done = false;
            _menu.interactable = true;
            _menu.ResetMouse();
        });
    }

    private bool SubmenuOpen => _graphics != null || GraphicsMenu.IsOpen || _controls != null || ControlsMenu.IsOpen;

    private void OpenGraphics()
    {
        _done = true;
        _graphics = GraphicsMenu.Open(() =>
        {
            _graphics = null;
            _done = false;
            _menu.interactable = true;
            _menu.ResetMouse();
        });
    }

    private void Play(AudioClip clip)
    {
        if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 0.8f);
    }

    public void DrawUi(UiBatch ui)
    {
        float fade = Mathf.Clamp01(_t / fadeInDuration);

        // darken the backdrop so the text reads, plus a heavy vignette (full-screen effects, outside the layout)
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.42f * fade));
        ui.DrawVignette(new Color(0.05f, 0f, 0f, 1f), 0.9f * fade);

        float pulse = 0.92f + 0.08f * Mathf.Sin(_t * 2.2f);
        var titleColor = new Color(0.92f * pulse, 0.12f * pulse, 0.08f * pulse, 1f);
        var dim = new Color(0.7f, 0.7f, 0.72f, 0.75f);

        var c = _clay;
        c.Begin(ui);
        // title block: top at 86% of the height, subtitle, red separator
        using (c.Box(ClayScreen.Column(0.14f * ClayScreen.RefHeight)))
        {
            c.Text(title, ClayScreen.Line(13f, titleColor, shadow: true));
            ClayScreen.Gap(c, 10f);
            c.Text(subtitle, ClayScreen.Line(2.6f, new Color(0.85f, 0.85f, 0.88f, 0.8f)));
            ClayScreen.Gap(c, 9f);
            c.Leaf(new El { width = Size.Fixed(520f), height = Size.Fixed(2f), background = new Color(0.8f, 0.15f, 0.1f, 0.7f) });
        }
        // menu (hidden while the graphics screen is up), top at 56% of the height
        if (!SubmenuOpen)
        {
            MenuItem clicked;
            using (c.Box(ClayScreen.FloatingColumn(0.44f * ClayScreen.RefHeight))) clicked = _menu.Build(c);
            if (clicked != null && _t >= fadeInDuration * 0.5f) Activate(clicked);
        }
        ClayScreen.Footer(c, "UP/DOWN OR MOUSE TO SELECT   ENTER / CLICK TO CONFIRM", 84f, ClayScreen.Line(2f, dim));
        ClayScreen.Footer(c, "WASD MOVE   MOUSE LOOK   LMB FIRE   RMB JAB   1-4 WEAPONS   SPACE JUMP   ESC PAUSE", 54f, ClayScreen.Line(2f, dim));
        c.End(fade);

        // black fade-in
        if (fade < 1f) ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 1f - fade));
    }
}
