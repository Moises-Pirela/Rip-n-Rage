using System;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Level;

/// <summary>
/// Title screen: NEW GAME / CONTINUE / QUIT over an attract-mode view of the level (<see cref="MenuCamera"/>).
/// Created by <see cref="GameSession.ShowMainMenu"/> on the session object; every choice goes back through
/// <see cref="GameSession"/>, which rebuilds the scene at the start of the next frame.
/// </summary>
public class MainMenuScreen : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Menu;

    public string title = "RIP-N-RAGE";
    public string subtitle = "A FAST FIRST-PERSON SHOOTER";
    public float fadeInDuration = 0.9f;

    private readonly MenuList _menu = new MenuList();
    private AudioSource _audio;
    private float _t;
    private bool _done;
    private string _continueSlot;

    protected override void Awake()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _continueSlot = GameSaveSystem.ContinueSlot();
        bool canContinue = _continueSlot != null;

        _menu.Add("NEW GAME", GameSession.NewGame, "START FROM THE BEGINNING");
        _menu.Add("CONTINUE", OnContinue,
            canContinue ? (_continueSlot == "checkpoint" ? "FROM THE LAST CHECKPOINT" : "FROM THE QUICK SAVE") : "NO SAVED GAME",
            canContinue);
        _menu.Add("GRAPHICS", OpenGraphics, "TOGGLE EVERY VISUAL EFFECT");
        _menu.Add("CONTROLS", OpenControls, "REBIND KEYS, MOUSE AND GAMEPAD");
        _menu.Add("QUIT", GameSession.Quit);
        _menu.selected = 0;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
        _menu.ResetMouse();
        InputSystem.AcquireUi(this);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    protected override void OnDestroy()
    {
        InputSystem.ReleaseUi(this);
    }

    protected override void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (SubmenuOpen) return;
        if (_done || _t < fadeInDuration * 0.5f) return;
        var item = _menu.HandleKeyboard();
        if (item != null) Activate(item);
    }

    private void Activate(MenuItem item)
    {
        if (_done) return;
        _done = true;
        _menu.interactable = false;
        Play(ProceduralSounds.MenuSelect);
        item.onActivate?.Invoke();
    }

    private GraphicsMenu _graphics;
    private ControlsMenu _controls;

    private void OpenControls()
    {
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
        _graphics = GraphicsMenu.Open(() =>
        {
            _graphics = null;
            _done = false;
            _menu.interactable = true;
            _menu.ResetMouse();
        });
    }

    private void OnContinue()
    {
        if (GameSession.Continue()) return;
        // the save vanished or is unreadable: stay on the menu
        _done = false;
        _menu.interactable = true;
        Play(ProceduralSounds.MenuBack);
    }

    private void Play(AudioClip clip)
    {
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, 0.8f);
    }

    public void DrawUi(UiBatch ui)
    {
        float s = ui.Scale;
        float fade = Mathf.Clamp01(_t / fadeInDuration);
        float cx = ui.Width * 0.5f;

        // darken the backdrop so the text reads, plus a heavy vignette
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.42f * fade));
        ui.DrawVignette(new Color(0.05f, 0f, 0f, 1f), 0.9f * fade);

        // title block
        float titleScale = 13f * s;
        float titleTop = ui.Height * 0.86f;
        float pulse = 0.92f + 0.08f * Mathf.Sin(_t * 2.2f);
        var titleColor = new Color(0.92f * pulse, 0.12f * pulse, 0.08f * pulse, fade);
        float th = MenuList.DrawCentered(ui, title, cx, titleTop, titleScale, titleColor, shadow: true);
        MenuList.DrawCentered(ui, subtitle, cx, titleTop - th - 10f * s, 2.6f * s, new Color(0.85f, 0.85f, 0.88f, 0.8f * fade));

        // separator
        float lineW = 520f * s;
        ui.FillRect(new Rect(cx - lineW * 0.5f, titleTop - th - 40f * s, lineW, Mathf.Max(1f, 2f * s)), new Color(0.8f, 0.15f, 0.1f, 0.7f * fade));

        // menu (hidden while the graphics screen is up)
        if (!SubmenuOpen)
        {
            float menuTop = ui.Height * 0.56f;
            var clicked = _menu.Draw(ui, cx, menuTop, fade);
            if (clicked != null && !_done && _t >= fadeInDuration * 0.5f) Activate(clicked);
        }

        // footer
        var dim = new Color(0.7f, 0.7f, 0.72f, 0.75f * fade);
        MenuList.DrawCentered(ui, "UP/DOWN OR MOUSE TO SELECT   ENTER / CLICK TO CONFIRM", cx, 84f * s, 2f * s, dim);
        MenuList.DrawCentered(ui, "WASD MOVE   MOUSE LOOK   LMB FIRE   RMB JAB   1-4 WEAPONS   SPACE JUMP   ESC PAUSE", cx, 54f * s, 2f * s, dim);

        // black fade-in
        if (fade < 1f) ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 1f - fade));
    }
}

/// <summary>
/// Camera used behind the main menu: stands at the player start and slowly swings its yaw over the
/// level (attract mode). It is the scene's main camera while the menu is shown.
/// </summary>
public class MenuCamera : MonoBehaviour
{
    public float baseYaw;
    public float pitch = 3f;
    public float swingDegrees = 30f;
    public float periodSeconds = 28f;

    public static MenuCamera Create(Vector3 playerStart, Quaternion playerRotation)
    {
        var go = new GameObject("MenuCamera");
        go.tag = "MainCamera";
        go.transform.position = playerStart + Vector3.up * 0.6f;
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 72f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000f;
        Camera.main = cam;
        var mc = go.AddComponent<MenuCamera>();
        mc.baseYaw = playerRotation.eulerAngles.y;
        go.transform.rotation = Quaternion.Euler(mc.pitch, mc.baseYaw, 0f);
        return mc;
    }

    protected override void Update()
    {
        float yaw = baseYaw + Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / periodSeconds) * swingDegrees;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
