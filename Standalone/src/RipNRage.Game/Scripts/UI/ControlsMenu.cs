using System;
using System.Collections.Generic;
using RipNRage.Game.Content;

/// <summary>
/// Controls screen: every gameplay action with its keyboard / mouse binding and its gamepad binding side by side.
/// Select a row and press Enter, A, or click it → "PRESS A KEY OR BUTTON": the next key, mouse button or wheel
/// notch fills the keyboard slot, the next gamepad button, trigger or stick push fills the gamepad slot
/// (Esc / B cancels, Backspace clears the keyboard slot, the gamepad BACK button clears the gamepad slot).
/// A control already used by another action is taken away from it. Plus look sensitivity / inversion for mouse
/// and stick, deadzone and stick response. Everything is saved to controls.json by <see cref="InputSystem"/>.
/// </summary>
public class ControlsMenu : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Modal + 10;

    public static ControlsMenu Current { get; private set; }
    public static bool IsOpen => Current != null && !Current.IsDestroyed;

    public Action onClose;
    private readonly MenuList _menu = new MenuList();
    private readonly Dictionary<MenuItem, InputAction> _rows = new Dictionary<MenuItem, InputAction>();
    private AudioSource _audio;
    private float _anim;
    private bool _closed;
    private float _blink;

    public static ControlsMenu Open(Action onClose)
    {
        if (IsOpen) return Current;
        var go = new GameObject("ControlsMenu");
        var m = go.AddComponent<ControlsMenu>();
        m.onClose = onClose;
        return m;
    }

    protected override void Awake()
    {
        Current = this;
        _audio = gameObject.AddComponent<AudioSource>();
        _menu.itemWidth = 860f; _menu.itemHeight = 40f; _menu.itemGap = 5f; _menu.labelScale = 2.6f; _menu.hintScale = 1.8f;
        _menu.visibleRows = 14;
        _menu.onSelectionChanged = _ => Play(ProceduralSounds.MenuMove);
        _menu.onAdjusted = item => { if (!_rows.ContainsKey(item)) { InputSystem.Save(); Play(ProceduralSounds.MenuSelect); } };
        Build();
        _menu.ResetMouse();
        InputSystem.AcquireUi(this);
    }

    protected override void OnDestroy()
    {
        if (InputSystem.Rebinding) InputSystem.CancelRebind();
        InputSystem.ReleaseUi(this);
        if (Current == this) Current = null;
    }

    private void Build()
    {
        foreach (var a in InputSystem.Gameplay.actions)
        {
            var action = a;
            var item = _menu.AddSetting(action.label, () => BindingText(action), _ => StartRebind(action), "ENTER / CLICK: PRESS THE NEW KEY OR BUTTON.  BACKSPACE CLEARS KEYBOARD, PAD BACK CLEARS GAMEPAD");
            _rows[item] = action;
        }
        Choice("MOUSE SENSITIVITY", new[] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f }, () => InputSystem.mouseSensitivity, v => InputSystem.mouseSensitivity = v, v => $"{v * 100f:0}%");
        Toggle("INVERT MOUSE Y", () => InputSystem.invertMouseY, v => InputSystem.invertMouseY = v);
        Choice("GAMEPAD LOOK SPEED", new[] { 100f, 150f, 200f, 250f, 300f, 400f }, () => InputSystem.gamepadLookSpeed, v => InputSystem.gamepadLookSpeed = v, v => $"{v:0} DEG/S");
        Toggle("INVERT GAMEPAD Y", () => InputSystem.invertGamepadY, v => InputSystem.invertGamepadY = v);
        Choice("STICK DEADZONE", new[] { 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f }, () => InputSystem.gamepadDeadzone, v => InputSystem.gamepadDeadzone = v, v => $"{v * 100f:0}%");
        Choice("STICK RESPONSE", new[] { 1f, 1.5f, 1.8f, 2.2f, 3f }, () => InputSystem.gamepadCurve, v => InputSystem.gamepadCurve = v, v => v <= 1f ? "LINEAR" : $"CURVE {v:0.0}", "HIGHER = FINER CONTROL NEAR THE CENTRE");
        _menu.Add("RESET TO DEFAULTS", () => { InputSystem.ResetToDefaults(); Play(ProceduralSounds.MenuBack); });
        _menu.Add("BACK", Close);
        _menu.selected = 0;
    }

    private string BindingText(InputAction a)
    {
        if (InputSystem.Rebinding && InputSystem.RebindingAction == a) return _blink % 1f < 0.6f ? "PRESS A KEY OR BUTTON..." : "";
        return a.keyboard.DisplayName + "   |   " + a.gamepad.DisplayName;
    }

    private void StartRebind(InputAction action)
    {
        if (InputSystem.Rebinding) return;
        Play(ProceduralSounds.MenuSelect);
        _menu.interactable = false;
        InputSystem.StartRebind(action, _ => { _menu.interactable = true; _menu.ResetMouse(); Play(ProceduralSounds.MenuSelect); });
    }

    private void Toggle(string label, Func<bool> get, Action<bool> set, string hint = null) =>
        _menu.AddSetting(label, () => get() ? "ON" : "OFF", _ => set(!get()), hint);

    private void Choice(string label, float[] options, Func<float> get, Action<float> set, Func<float, string> fmt, string hint = null)
    {
        _menu.AddSetting(label, () => fmt(get()), dir =>
        {
            int best = 0; float bestD = float.MaxValue; float cur = get();
            for (int i = 0; i < options.Length; i++) { float d = Mathf.Abs(options[i] - cur); if (d < bestD) { bestD = d; best = i; } }
            set(options[Mathf.Clamp(best + dir, 0, options.Length - 1)]);
        }, hint);
    }

    protected override void Update()
    {
        _anim = Mathf.MoveTowards(_anim, 1f, Time.unscaledDeltaTime / 0.15f);
        _blink += Time.unscaledDeltaTime;
        if (_closed) return;
        if (InputSystem.Rebinding) return; // the InputSystem owns the keys now (Esc cancels there); the callback re-enables the list
        if (!_menu.interactable) _menu.interactable = true; // rebind was cancelled from the InputSystem side
        if (InputSystem.UI["cancel"].Pressed) { Close(); return; }
        var item = _menu.HandleKeyboard();
        if (item != null && !item.IsSetting) { Play(ProceduralSounds.MenuSelect); item.onActivate?.Invoke(); }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        if (InputSystem.Rebinding) InputSystem.CancelRebind();
        InputSystem.Save();
        Play(ProceduralSounds.MenuBack);
        GameObject.Destroy(gameObject);
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
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, 0.72f * a));

        float top = ui.Height * 0.93f;
        float th = MenuList.DrawCentered(ui, "CONTROLS", cx, top, 7f * s, new Color(0.95f, 0.85f, 0.55f, a), shadow: true);
        string pad = Input.GamepadConnected ? "GAMEPAD: " + Input.GamepadName.ToUpperInvariant() : "NO GAMEPAD CONNECTED (PLUG ONE IN ANY TIME)";
        float ph = MenuList.DrawCentered(ui, pad, cx, top - th - 6f * s, 2.2f * s, new Color(0.75f, 0.85f, 1f, 0.85f * a));
        MenuList.DrawCentered(ui, "ACTION                                   KEYBOARD / MOUSE   |   GAMEPAD", cx, top - th - ph - 8f * s, 2f * s, new Color(0.7f, 0.7f, 0.72f, 0.7f * a));

        float menuTop = top - th - ph - 30f * s;
        var clicked = _menu.Draw(ui, cx, menuTop, a);
        if (clicked != null && !_closed && !clicked.IsSetting) { Play(ProceduralSounds.MenuSelect); clicked.onActivate?.Invoke(); }

        string footer = InputSystem.Rebinding
            ? "PRESS A KEY, MOUSE BUTTON, WHEEL, GAMEPAD BUTTON, TRIGGER OR STICK DIRECTION   -   ESC / B CANCEL   BACKSPACE CLEARS"
            : "ENTER / A / CLICK REBIND   LEFT/RIGHT CHANGE VALUES   UP/DOWN SELECT   ESC / B BACK   SAVED AUTOMATICALLY";
        MenuList.DrawCentered(ui, footer, cx, 60f * s, 2f * s, new Color(0.7f, 0.7f, 0.72f, 0.7f * a));
    }
}
