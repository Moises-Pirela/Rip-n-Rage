using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// Escape during play: freezes the world (<c>Time.timeScale = 0</c>), releases the mouse and shows
/// RESUME / RESTART LEVEL / LOAD CHECKPOINT / MAIN MENU / QUIT. Lives on the session object of every
/// level (<see cref="GameSession.StartLevel"/>). Ignored while the player is dead: the
/// <see cref="DeathScreen"/> owns the screen then.
/// </summary>
public class PauseMenu : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Menu;

    public static bool IsPaused { get; private set; }

    private readonly MenuList _menu = new MenuList();
    private MenuItem _loadCheckpoint;
    private AudioSource _audio;
    private bool _open;
    private bool _done;
    private float _anim;
    private float _savedTimeScale = 1f;

    protected override void Awake()
    {
        IsPaused = false;
        _audio = gameObject.AddComponent<AudioSource>();
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

    protected override void OnDestroy()
    {
        InputSystem.ReleaseUi(this);
        if (_open) { IsPaused = false; Time.timeScale = _savedTimeScale; }
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

    protected override void Update()
    {
        if (SubmenuOpen)
        {
            _anim = Mathf.MoveTowards(_anim, _open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            return; // the graphics screen owns the input (including Esc) while it is up
        }
        // gameplay scheme: "pause" (Esc / Start); UI scheme while open: "cancel" (Esc / B) or "menu" (Esc / Start)
        bool wantToggle = _open ? (InputSystem.UI["cancel"].Pressed || InputSystem.UI["menu"].Pressed) : InputSystem.Gameplay["pause"].Pressed;
        if (wantToggle && !_done)
        {
            var player = PlayerState.Current;
            bool canPause = player != null && !player.IsDead && GameSession.State == GameState.Playing;
            if (_open) Resume();
            else if (canPause) Open();
        }

        _anim = Mathf.MoveTowards(_anim, _open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);

        if (_open && !_done)
        {
            var item = _menu.HandleKeyboard();
            if (item != null) Activate(item);
        }
    }

    private void Open()
    {
        if (_open) return;
        _open = true;
        IsPaused = true;
        InputSystem.AcquireUi(this); // UI scheme takes over: gameplay actions idle, cursor free
        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        _menu.selected = 0;
        _menu.interactable = true;
        _menu.ResetMouse();
        _loadCheckpoint.enabled = GameSaveSystem.CheckpointAvailable(GameSession.LevelPath);
        _loadCheckpoint.hint = _loadCheckpoint.enabled ? null : "NO CHECKPOINT REACHED";
        SetPlayerInput(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Play(ProceduralSounds.MenuSelect);
    }

    private void Resume()
    {
        if (!_open) return;
        _open = false;
        IsPaused = false;
        _done = false;
        InputSystem.ReleaseUi(this);
        _menu.interactable = true;
        Time.timeScale = _savedTimeScale;
        var player = PlayerState.Current;
        if (player != null && !player.IsDead)
        {
            SetPlayerInput(true);
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

    private static void SetPlayerInput(bool on)
    {
        var input = EnragedInjector<PlayerInputActions>.GetInstance();
        if (input != null && !input.IsDestroyed) input.enabled = on;
    }

    private void Play(AudioClip clip)
    {
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, 0.6f);
    }

    public void DrawUi(UiBatch ui)
    {
        if (_anim <= 0.001f) return;
        if (SubmenuOpen) return; // the settings screens draw their own backdrop
        float s = ui.Scale;
        float cx = ui.Width * 0.5f;
        float a = _anim;

        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.62f * a));
        ui.DrawVignette(new Color(0f, 0f, 0f, 1f), 0.8f * a);

        float titleScale = 9f * s;
        float titleTop = ui.Height * 0.78f;
        float th = MenuList.DrawCentered(ui, "PAUSED", cx, titleTop, titleScale, new Color(0.95f, 0.85f, 0.55f, a), shadow: true);

        var ps = PlayerState.Current;
        if (ps != null)
        {
            string status = $"HEALTH {ps.Health:0}   ARMOR {ps.Armor:0}   RAGE {ps.Rage * 100f:0}%";
            MenuList.DrawCentered(ui, status, cx, titleTop - th - 8f * s, 2.4f * s, new Color(0.8f, 0.8f, 0.82f, 0.85f * a));
        }

        var clicked = _menu.Draw(ui, cx, ui.Height * 0.6f, a);
        if (clicked != null && _open && !_done) Activate(clicked);

        MenuList.DrawCentered(ui, "ESC RESUME   UP/DOWN SELECT   ENTER CONFIRM", cx, 84f * s, 2f * s, new Color(0.7f, 0.7f, 0.72f, 0.7f * a));
    }
}
