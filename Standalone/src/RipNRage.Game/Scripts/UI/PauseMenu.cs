using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// Escape during play: asks the flow to pause (<see cref="RipNRage.Game.Ecs.PauseRequest"/>); once
/// <see cref="RipNRage.Game.Ecs.FlowSystem"/> has frozen the world (Paused), releases the mouse and shows
/// RESUME / RESTART LEVEL / LOAD CHECKPOINT / MAIN MENU / QUIT. Lives on the session object of every
/// level (<see cref="GameSession.StartLevel"/>) as a plain <see cref="IUiScreen"/>. The flow refuses a pause while the player is dying or
/// dead: the <see cref="DeathScreen"/> owns the screen then.
/// </summary>
public sealed class PauseMenu : IUiScreen, IUiLayer
{
    private readonly ClayUi _clay = new ClayUi();
    public int UiLayer => UiLayers.Menu;

    private readonly MenuList _menu = new MenuList();
    private MenuItem _loadCheckpoint;
    private bool _open;
    private bool _done;
    private float _anim;

    public PauseMenu()
    {
        _menu.itemHeight = 56f;
        _menu.labelScale = 3.5f;
        _menu.Add("RESUME", Resume);
        _menu.Add("RESTART LEVEL", () => Choose(() => GameSession.RestartLevel(GameSession.LevelPath)));
        _loadCheckpoint = _menu.Add("LOAD CHECKPOINT", () => Choose(() => { if (!GameSaveSystem.Load("checkpoint")) Resume(); }), null, false);
        _menu.Add("GRAPHICS", OpenGraphics, "TOGGLE EVERY VISUAL EFFECT, SEE THE FRAME TIME LIVE");
        _menu.Add("CONTROLS", OpenControls, "REBIND KEYS, MOUSE AND GAMEPAD");
        _menu.Add("MAIN MENU", () => Choose(() => GameSession.ShowMainMenu()));
        _menu.Add("QUIT", () => Choose(GameSession.Quit));
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
    }

    public void Close()
    {
        _clay.Dispose();
        InputSystem.ReleaseUi(this);
        // the scene is going: the fresh Flow it builds is Playing at full speed, so there is nothing to undo
    }

    private void OpenGraphics()
    {
        if (_graphics != null) return;
        _graphics = GraphicsMenu.Open(() => { _graphics = null; _menu.ResetMouse(); });
    }

    private GraphicsMenu _graphics;
    private ControlsMenu _controls;

    private void OpenControls()
    {
        if (_controls != null) return;
        _controls = ControlsMenu.Open(() => { _controls = null; _menu.ResetMouse(); });
    }

    private bool SubmenuOpen => _graphics != null || GraphicsMenu.IsOpen || _controls != null || ControlsMenu.IsOpen;

    public void Update()
    {
        if (SubmenuOpen)
        {
            _anim = Mathf.MoveTowards(_anim, _open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            return; // the graphics screen owns the input (including Esc) while it is up
        }
        // 8.2: the menu follows the flow — FlowSystem decides whether a pause is allowed (only while Playing) and
        // the menu opens or closes on the phase it settled on, one frame after the press
        bool paused = RipNRage.Game.Ecs.Flows.Current.Phase == RipNRage.Game.Ecs.FlowPhase.Paused;
        if (paused && !_open) Open();
        else if (!paused && _open) HideMenu();

        // gameplay scheme: "pause" (Esc / Start); UI scheme while open: "cancel" (Esc / B) or "menu" (Esc / Start)
        bool wantToggle = _open ? (InputSystem.UI["cancel"].Pressed || InputSystem.UI["menu"].Pressed) : InputSystem.Gameplay["pause"].Pressed;
        if (wantToggle && !_done) RipNRage.Game.Ecs.Flows.RequestPause(!_open);

        _anim = Mathf.MoveTowards(_anim, _open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);

        if (_open && !_done)
        {
            var item = _menu.HandleKeyboard();
            if (item != null) Activate(item);
        }
    }

    /// <summary>The flow went Paused (the world is already frozen): the UI side of opening.</summary>
    private void Open()
    {
        _open = true;
        InputSystem.AcquireUi(this); // UI scheme takes over: gameplay actions idle, cursor free
        _menu.selected = 0;
        _menu.interactable = true;
        _menu.ResetMouse();
        _loadCheckpoint.enabled = GameSaveSystem.CheckpointAvailable(GameSession.LevelPath);
        _loadCheckpoint.hint = _loadCheckpoint.enabled ? null : "NO CHECKPOINT REACHED";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Play(ProceduralSounds.MenuSelect);
    }

    /// <summary>RESUME: asks the flow; the menu closes when it answers.</summary>
    private void Resume() => RipNRage.Game.Ecs.Flows.RequestPause(false);

    /// <summary>The flow left Paused — resumed, or the player died while it was up (a blow already in flight): the
    /// UI side of closing. Control goes back to the player only when the run is Playing.</summary>
    private void HideMenu()
    {
        _open = false;
        _done = false;
        InputSystem.ReleaseUi(this);
        _menu.interactable = true;
        if (RipNRage.Game.Ecs.Flows.Current.Phase == RipNRage.Game.Ecs.FlowPhase.Playing)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        Play(ProceduralSounds.MenuBack);
    }

    /// <summary>Runs a scene transition: the menu stays visible (frozen) until GameSession rebuilds the scene next frame.</summary>
    private void Choose(Action action)
    {
        _done = true;
        _menu.interactable = false;
        action?.Invoke();
    }

    private void Activate(MenuItem item)
    {
        Play(ProceduralSounds.MenuSelect);
        item.onActivate?.Invoke();
    }

    private void Play(AudioClip clip)
    {
        if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 0.6f);
    }

    public void DrawUi(UiBatch ui)
    {
        if (_anim <= 0.001f) return;
        if (SubmenuOpen) return; // the settings screens draw their own backdrop
        float a = _anim;

        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.62f * a));
        ui.DrawVignette(new Color(0f, 0f, 0f, 1f), 0.8f * a);

        var c = _clay;
        c.Begin(ui);
        using (c.Box(ClayScreen.Column(0.22f * ClayScreen.RefHeight)))
        {
            c.Text("PAUSED", ClayScreen.Line(9f, new Color(0.95f, 0.85f, 0.55f, 1f), shadow: true));
            var w = Application.Current?.World;
            var pe = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;
            if (w != null && !pe.IsNull && w.IsAlive(pe) && w.Has<RipNRage.Game.Ecs.Combat.Health>(pe))
            {
                ClayScreen.Gap(c, 8f);
                RipNRage.Game.Ecs.Rages.TryGet(out var rage);
                float hp = w.Get<RipNRage.Game.Ecs.Combat.Health>(pe).Current;
                float armor = w.Has<RipNRage.Game.Ecs.Combat.Armor>(pe) ? w.Get<RipNRage.Game.Ecs.Combat.Armor>(pe).Current : 0f;
                c.Text($"HEALTH {hp:0}   ARMOR {armor:0}   FURY {rage.Fury * 100f:0}%   CALM {rage.Calm * 100f:0}%", ClayScreen.Line(2.4f, new Color(0.8f, 0.8f, 0.82f, 0.85f)));
            }
        }
        MenuItem clicked;
        using (c.Box(ClayScreen.FloatingColumn(0.4f * ClayScreen.RefHeight))) clicked = _menu.Build(c);
        ClayScreen.Footer(c, "ESC RESUME   UP/DOWN SELECT   ENTER CONFIRM", 84f, ClayScreen.Line(2f, new Color(0.7f, 0.7f, 0.72f, 0.7f)));
        c.End(a);
        if (clicked != null && _open && !_done) Activate(clicked);
    }
}
