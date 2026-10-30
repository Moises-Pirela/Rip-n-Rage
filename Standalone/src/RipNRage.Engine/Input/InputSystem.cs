using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine
{
    /// <summary>Physical input a binding refers to.</summary>
    public enum InputSource
    {
        None,
        /// <summary>Keyboard key or mouse button (<see cref="KeyCode"/>, mouse buttons are KeyCode.Mouse0+).</summary>
        Key,
        /// <summary>Gamepad button (GLFW gamepad button index 0..14).</summary>
        PadButton,
        /// <summary>Gamepad axis pushed positive past the threshold (index 0..5; triggers are 0..1).</summary>
        PadAxisPositive,
        /// <summary>Gamepad axis pushed negative past the threshold.</summary>
        PadAxisNegative,
        MouseScrollUp,
        MouseScrollDown,
    }

    /// <summary>One physical control bound to an action.</summary>
    [Serializable]
    public struct InputBinding : IEquatable<InputBinding>
    {
        public InputSource source;
        public int code;
        public InputBinding(InputSource source, int code) { this.source = source; this.code = code; }
        public static InputBinding Key(KeyCode k) => new InputBinding(InputSource.Key, (int)k);
        public static InputBinding Mouse(int button) => new InputBinding(InputSource.Key, (int)KeyCode.Mouse0 + button);
        public static InputBinding Pad(int button) => new InputBinding(InputSource.PadButton, button);
        public static InputBinding Axis(int axis, bool positive) => new InputBinding(positive ? InputSource.PadAxisPositive : InputSource.PadAxisNegative, axis);
        public static readonly InputBinding None = new InputBinding(InputSource.None, 0);
        public bool IsNone => source == InputSource.None;
        /// <summary>True for gamepad sources (the controls screen shows two columns: keyboard / mouse and gamepad).</summary>
        public bool IsGamepad => source == InputSource.PadButton || source == InputSource.PadAxisPositive || source == InputSource.PadAxisNegative;
        public bool Equals(InputBinding o) => source == o.source && code == o.code;
        public override bool Equals(object obj) => obj is InputBinding b && Equals(b);
        public override int GetHashCode() => HashCode.Combine((int)source, code);

        /// <summary>Short human-readable name ("SPACE", "MOUSE 1", "RT", "D-PAD UP", "LS LEFT").</summary>
        public string DisplayName
        {
            get
            {
                switch (source)
                {
                    case InputSource.None: return "-";
                    case InputSource.MouseScrollUp: return "WHEEL UP";
                    case InputSource.MouseScrollDown: return "WHEEL DOWN";
                    case InputSource.PadButton: return code >= 0 && code < PadButtonNames.Length ? PadButtonNames[code] : "PAD " + code;
                    case InputSource.PadAxisPositive: return code >= 0 && code < PadAxisPosNames.Length ? PadAxisPosNames[code] : "AXIS+" + code;
                    case InputSource.PadAxisNegative: return code >= 0 && code < PadAxisNegNames.Length ? PadAxisNegNames[code] : "AXIS-" + code;
                    default:
                    {
                        var k = (KeyCode)code;
                        if (k >= KeyCode.Mouse0) return "MOUSE " + (code - (int)KeyCode.Mouse0 + 1);
                        string n = k.ToString();
                        if (n.StartsWith("Alpha")) return n.Substring(5);
                        if (n.StartsWith("Keypad")) return "NUM " + n.Substring(6).ToUpperInvariant();
                        var sb = new System.Text.StringBuilder();
                        foreach (char c in n) { if (char.IsUpper(c) && sb.Length > 0) sb.Append(' '); sb.Append(char.ToUpperInvariant(c)); }
                        return sb.ToString();
                    }
                }
            }
        }

        private static readonly string[] PadButtonNames = { "A", "B", "X", "Y", "LB", "RB", "BACK", "START", "GUIDE", "L3", "R3", "D-PAD UP", "D-PAD RIGHT", "D-PAD DOWN", "D-PAD LEFT" };
        private static readonly string[] PadAxisPosNames = { "LS RIGHT", "LS DOWN", "RS RIGHT", "RS DOWN", "LT", "RT" };
        private static readonly string[] PadAxisNegNames = { "LS LEFT", "LS UP", "RS LEFT", "RS UP", "LT", "RT" };
    }

    /// <summary>A named, rebindable input with one binding slot for keyboard / mouse and one for the gamepad.</summary>
    public sealed class InputAction
    {
        public readonly string name;
        public readonly string label;
        public InputBinding keyboard;
        public InputBinding gamepad;
        public readonly InputBinding defaultKeyboard, defaultGamepad;
        /// <summary>Actions the controls screen cannot rebind (UI navigation).</summary>
        public bool locked;

        public bool Pressed { get; internal set; }
        public bool Released { get; internal set; }
        public bool Held { get; internal set; }
        /// <summary>0..1 (analog for triggers / stick directions, 0 or 1 for buttons).</summary>
        public float Value { get; internal set; }

        public InputAction(string name, string label, InputBinding keyboard, InputBinding gamepad, bool locked = false)
        {
            this.name = name; this.label = label; this.keyboard = keyboard; this.gamepad = gamepad; this.locked = locked;
            defaultKeyboard = keyboard; defaultGamepad = gamepad;
        }

        internal void Clear() { Pressed = Released = Held = false; Value = 0f; }
    }

    /// <summary>A set of actions that is enabled as a whole (the gameplay scheme and the UI scheme).</summary>
    public sealed class InputMap
    {
        public readonly string name;
        public readonly List<InputAction> actions = new List<InputAction>();
        private readonly Dictionary<string, InputAction> _byName = new Dictionary<string, InputAction>(StringComparer.OrdinalIgnoreCase);
        public bool Enabled { get; internal set; }

        public InputMap(string name) { this.name = name; }

        public InputAction Add(string actionName, string label, InputBinding keyboard, InputBinding gamepad, bool locked = false)
        {
            var a = new InputAction(actionName, label, keyboard, gamepad, locked);
            actions.Add(a); _byName[actionName] = a;
            return a;
        }

        public InputAction this[string actionName] => _byName.TryGetValue(actionName, out var a) ? a : throw new KeyNotFoundException($"input action '{actionName}' in map '{name}'");
        public InputAction Get(string actionName) => _byName.TryGetValue(actionName, out var a) ? a : null;
    }

    /// <summary>
    /// Action-based input on top of the raw <see cref="Input"/> state: two control schemes — <see cref="Gameplay"/>
    /// and <see cref="UI"/> — of which exactly one is active at a time. Any screen with buttons (menus, the death
    /// screen, settings) registers itself as a UI owner while it is up; while at least one owner exists the UI map
    /// receives the input and every gameplay action reads as idle, and the cursor is released. Bindings (keyboard /
    /// mouse and gamepad slot per action) are rebindable at runtime and saved to <c>controls.json</c>; a gamepad
    /// works out of the box: left stick moves, right stick looks, triggers fire, D-pad / sticks navigate menus.
    /// </summary>
    public static class InputSystem
    {
        public static readonly InputMap Gameplay = new InputMap("gameplay");
        public static readonly InputMap UI = new InputMap("ui");

        // ---- tuning (saved with the bindings)
        public static float mouseSensitivity = 1f;
        public static bool invertMouseY;
        /// <summary>Right-stick turn rate at full deflection, degrees per second.</summary>
        public static float gamepadLookSpeed = 200f;
        public static bool invertGamepadY;
        public static float gamepadDeadzone = 0.2f;
        /// <summary>Stick response exponent (1 = linear, 2 = fine control near the centre).</summary>
        public static float gamepadCurve = 1.8f;
        /// <summary>Axis deflection that counts as a digital press for axis bindings (triggers, stick directions).</summary>
        public static float axisPressThreshold = 0.5f;

        /// <summary>True while a UI screen owns the input (UI scheme active, gameplay idle, cursor free).</summary>
        public static bool UiActive => _uiOwners.Count > 0;
        /// <summary>Set by the game: lock / hide the cursor while the gameplay scheme is active.</summary>
        public static bool LockCursorInGameplay;
        /// <summary>Whether the system drives <see cref="Cursor"/> every frame (editors keep their own cursor handling).</summary>
        public static bool ManageCursor;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "controls.json");

        // UI navigation with auto-repeat
        public static float repeatDelay = 0.4f, repeatRate = 0.1f;
        private static readonly Dictionary<string, float> _repeatTimers = new Dictionary<string, float>();

        private static readonly HashSet<object> _uiOwners = new HashSet<object>();
        private static bool _initialised;

        // rebinding
        private static InputAction _rebindAction;
        private static Action<InputBinding> _rebindDone;
        public static bool Rebinding => _rebindAction != null;
        public static InputAction RebindingAction => _rebindAction;

        // UI navigation flags: D-pad / arrow actions plus the left stick, with auto-repeat
        public static bool UiUp { get; private set; }
        public static bool UiDown { get; private set; }
        public static bool UiLeft { get; private set; }
        public static bool UiRight { get; private set; }

        // ------------------------------------------------------------------ setup

        /// <summary>Creates the default action set (idempotent).</summary>
        public static void Initialize()
        {
            if (_initialised) return;
            _initialised = true;
            var G = Gameplay;
            G.Add("move_forward", "MOVE FORWARD", InputBinding.Key(KeyCode.W), InputBinding.Axis(Glfw.GAMEPAD_AXIS_LEFT_Y, false));
            G.Add("move_back", "MOVE BACK", InputBinding.Key(KeyCode.S), InputBinding.Axis(Glfw.GAMEPAD_AXIS_LEFT_Y, true));
            G.Add("move_left", "STRAFE LEFT", InputBinding.Key(KeyCode.A), InputBinding.Axis(Glfw.GAMEPAD_AXIS_LEFT_X, false));
            G.Add("move_right", "STRAFE RIGHT", InputBinding.Key(KeyCode.D), InputBinding.Axis(Glfw.GAMEPAD_AXIS_LEFT_X, true));
            G.Add("jump", "JUMP", InputBinding.Key(KeyCode.Space), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_A));
            G.Add("slide", "SLIDE / CROUCH", InputBinding.Key(KeyCode.LeftControl), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_B));
            G.Add("dash", "DASH", InputBinding.Key(KeyCode.LeftShift), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_LEFT_THUMB));
            G.Add("fire", "FIRE", InputBinding.Mouse(0), InputBinding.Axis(Glfw.GAMEPAD_AXIS_RIGHT_TRIGGER, true));
            G.Add("alt_fire", "JAB / GRAB", InputBinding.Mouse(1), InputBinding.Axis(Glfw.GAMEPAD_AXIS_LEFT_TRIGGER, true));
            G.Add("weapon_1", "GLOCK", InputBinding.Key(KeyCode.Alpha1), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_UP));
            G.Add("weapon_2", "M1887", InputBinding.Key(KeyCode.Alpha2), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_RIGHT));
            G.Add("weapon_3", "MG338", InputBinding.Key(KeyCode.Alpha3), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_DOWN));
            G.Add("weapon_4", "ROCKET LAUNCHER", InputBinding.Key(KeyCode.Alpha4), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_LEFT));
            G.Add("weapon_next", "NEXT WEAPON", new InputBinding(InputSource.MouseScrollUp, 0), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_RIGHT_BUMPER));
            G.Add("weapon_prev", "PREVIOUS WEAPON", new InputBinding(InputSource.MouseScrollDown, 0), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_LEFT_BUMPER));
            G.Add("weapon_last", "QUICK SWAP (LAST WEAPON)", InputBinding.Key(KeyCode.Q), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_Y));
            G.Add("pause", "PAUSE", InputBinding.Key(KeyCode.Escape), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_START));
            G.Add("quick_save", "QUICK SAVE", InputBinding.Key(KeyCode.F6), InputBinding.None);
            G.Add("quick_load", "QUICK LOAD", InputBinding.Key(KeyCode.F9), InputBinding.None);

            var U = UI;
            U.Add("up", "MENU UP", InputBinding.Key(KeyCode.UpArrow), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_UP), locked: true);
            U.Add("down", "MENU DOWN", InputBinding.Key(KeyCode.DownArrow), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_DOWN), locked: true);
            U.Add("left", "MENU LEFT", InputBinding.Key(KeyCode.LeftArrow), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_LEFT), locked: true);
            U.Add("right", "MENU RIGHT", InputBinding.Key(KeyCode.RightArrow), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_DPAD_RIGHT), locked: true);
            U.Add("submit", "CONFIRM", InputBinding.Key(KeyCode.Return), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_A), locked: true);
            U.Add("cancel", "BACK", InputBinding.Key(KeyCode.Escape), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_B), locked: true);
            U.Add("menu", "CLOSE MENU", InputBinding.Key(KeyCode.Escape), InputBinding.Pad(Glfw.GAMEPAD_BUTTON_START), locked: true);
            Load();
        }

        /// <summary>Everything back to the defaults (and saved).</summary>
        public static void ResetToDefaults()
        {
            foreach (var m in new[] { Gameplay, UI })
                foreach (var a in m.actions) { a.keyboard = a.defaultKeyboard; a.gamepad = a.defaultGamepad; }
            mouseSensitivity = 1f; invertMouseY = false; gamepadLookSpeed = 200f; invertGamepadY = false; gamepadDeadzone = 0.2f; gamepadCurve = 1.8f;
            Save();
        }

        // ------------------------------------------------------------------ UI ownership / schemes

        /// <summary>A screen with buttons is up: the UI scheme takes over until the owner is released.</summary>
        public static void AcquireUi(object owner) { if (owner != null) _uiOwners.Add(owner); }
        public static void ReleaseUi(object owner) { if (owner != null) _uiOwners.Remove(owner); }
        /// <summary>Drops every owner (scene reset).</summary>
        public static void ClearUiOwners() => _uiOwners.Clear();

        // ------------------------------------------------------------------ per frame

        /// <summary>Called by the application after <see cref="Input.BeginFrame"/>: evaluates every action for the active scheme.</summary>
        public static void Update(float unscaledDt)
        {
            if (!_initialised) Initialize();
            bool ui = UiActive;
            Gameplay.Enabled = !ui && !Rebinding;
            UI.Enabled = ui && !Rebinding;

            SampleAxisEdges();
            if (Rebinding) CaptureRebind();

            Evaluate(Gameplay, unscaledDt);
            Evaluate(UI, unscaledDt);
            UpdateUiNavigation(unscaledDt);

            if (ManageCursor)
            {
                bool free = ui || !LockCursorInGameplay;
                var want = free ? CursorLockMode.None : CursorLockMode.Locked;
                if (Cursor.lockState != want) Cursor.lockState = want;
                // with a controller in the hand the pointer only clutters menus
                bool visible = free && !(Input.GamepadActive && Input.GamepadConnected);
                if (Cursor.visible != visible) Cursor.visible = visible;
            }
        }

        private static void Evaluate(InputMap map, float dt)
        {
            foreach (var a in map.actions)
            {
                if (!map.Enabled) { a.Clear(); _repeatTimers.Remove(map.name + "/" + a.name); continue; }
                bool heldK = Held(a.keyboard, out float vK), heldP = Held(a.gamepad, out float vP);
                bool pressedNow = PressedNow(a.keyboard) || PressedNow(a.gamepad);
                bool releasedNow = ReleasedNow(a.keyboard) || ReleasedNow(a.gamepad);
                a.Held = heldK || heldP;
                a.Value = MathF.Max(vK, vP);
                a.Released = releasedNow && !a.Held;
                a.Pressed = pressedNow;
                bool nav = map == UI && (a.name == "up" || a.name == "down" || a.name == "left" || a.name == "right");
                if (nav && a.Held && !pressedNow) a.Pressed |= Repeat(map.name + "/" + a.name, dt);
                else if (!a.Held) _repeatTimers.Remove(map.name + "/" + a.name);
                else if (pressedNow) _repeatTimers[map.name + "/" + a.name] = repeatDelay;
            }
        }

        /// <summary>Menu navigation = the D-pad / arrow actions plus the left stick as a digital pad (with auto-repeat while held).</summary>
        private static void UpdateUiNavigation(float dt)
        {
            UiUp = UiDown = UiLeft = UiRight = false;
            if (!UI.Enabled) { _repeatTimers.Remove("stick/up"); _repeatTimers.Remove("stick/down"); _repeatTimers.Remove("stick/left"); _repeatTimers.Remove("stick/right"); return; }
            UiUp = UI["up"].Pressed; UiDown = UI["down"].Pressed; UiLeft = UI["left"].Pressed; UiRight = UI["right"].Pressed;
            if (!Input.GamepadConnected) return;
            int ly = Glfw.GAMEPAD_AXIS_LEFT_Y, lx = Glfw.GAMEPAD_AXIS_LEFT_X, n = Input.GamepadAxisCount;
            UiUp |= StickNav("stick/up", _axisNow[n + ly], _axisPrev[n + ly], dt);
            UiDown |= StickNav("stick/down", _axisNow[ly], _axisPrev[ly], dt);
            UiLeft |= StickNav("stick/left", _axisNow[n + lx], _axisPrev[n + lx], dt);
            UiRight |= StickNav("stick/right", _axisNow[lx], _axisPrev[lx], dt);
        }

        private static bool StickNav(string key, bool now, bool prev, float dt)
        {
            if (!now) { _repeatTimers.Remove(key); return false; }
            if (!prev) { _repeatTimers[key] = repeatDelay; return true; }
            return Repeat(key, dt);
        }

        /// <summary>Auto-repeat for held UI navigation inputs.</summary>
        private static bool Repeat(string key, float dt)
        {
            if (!_repeatTimers.TryGetValue(key, out float t)) { _repeatTimers[key] = repeatDelay; return false; }
            t -= dt;
            if (t <= 0f) { _repeatTimers[key] = repeatRate; return true; }
            _repeatTimers[key] = t;
            return false;
        }

        private static bool Held(in InputBinding b, out float value)
        {
            value = 0f;
            switch (b.source)
            {
                case InputSource.Key:
                {
                    var k = (KeyCode)b.code;
                    bool h = k >= KeyCode.Mouse0 ? Input.GetMouseButton(b.code - (int)KeyCode.Mouse0) : Input.GetKey(k);
                    value = h ? 1f : 0f; return h;
                }
                case InputSource.PadButton: { bool h = Input.GetPadButton(b.code); value = h ? 1f : 0f; return h; }
                case InputSource.PadAxisPositive: { float v = Input.GetPadAxis(b.code); value = Deadzoned(v); return v > axisPressThreshold; }
                case InputSource.PadAxisNegative: { float v = -Input.GetPadAxis(b.code); value = Deadzoned(v); return v > axisPressThreshold; }
                case InputSource.MouseScrollUp: { bool h = Input.mouseScrollDelta.y > 0f; value = h ? 1f : 0f; return h; }
                case InputSource.MouseScrollDown: { bool h = Input.mouseScrollDelta.y < 0f; value = h ? 1f : 0f; return h; }
                default: return false;
            }
        }

        /// <summary>Analog value of an axis direction after the deadzone and response curve (0..1).</summary>
        private static float Deadzoned(float v)
        {
            if (v <= gamepadDeadzone) return 0f;
            float t = Mathf.Clamp01((v - gamepadDeadzone) / (1f - gamepadDeadzone));
            return MathF.Pow(t, MathF.Max(0.1f, gamepadCurve));
        }

        // axis "pressed" / "released" edges need last-frame state: [axis] = positive direction, [6 + axis] = negative
        private static readonly bool[] _axisNow = new bool[Input.GamepadAxisCount * 2], _axisPrev = new bool[Input.GamepadAxisCount * 2];

        private static void SampleAxisEdges()
        {
            Array.Copy(_axisNow, _axisPrev, _axisNow.Length);
            for (int a = 0; a < Input.GamepadAxisCount; a++)
            {
                float v = Input.GetPadAxis(a);
                _axisNow[a] = v > axisPressThreshold;
                _axisNow[Input.GamepadAxisCount + a] = -v > axisPressThreshold;
            }
        }

        private static int AxisSlot(in InputBinding b) => b.source == InputSource.PadAxisPositive ? b.code : Input.GamepadAxisCount + b.code;

        private static bool PressedNow(in InputBinding b)
        {
            switch (b.source)
            {
                case InputSource.Key:
                {
                    var k = (KeyCode)b.code;
                    return k >= KeyCode.Mouse0 ? Input.GetMouseButtonDown(b.code - (int)KeyCode.Mouse0) : Input.GetKeyDown(k);
                }
                case InputSource.PadButton: return Input.GetPadButtonDown(b.code);
                case InputSource.PadAxisPositive:
                case InputSource.PadAxisNegative:
                {
                    int i = AxisSlot(b);
                    return (uint)i < _axisNow.Length && _axisNow[i] && !_axisPrev[i];
                }
                case InputSource.MouseScrollUp: return Input.mouseScrollDelta.y > 0f;
                case InputSource.MouseScrollDown: return Input.mouseScrollDelta.y < 0f;
                default: return false;
            }
        }

        private static bool ReleasedNow(in InputBinding b)
        {
            switch (b.source)
            {
                case InputSource.Key:
                {
                    var k = (KeyCode)b.code;
                    return k >= KeyCode.Mouse0 ? Input.GetMouseButtonUp(b.code - (int)KeyCode.Mouse0) : Input.GetKeyUp(k);
                }
                case InputSource.PadButton: return Input.GetPadButtonUp(b.code);
                case InputSource.PadAxisPositive:
                case InputSource.PadAxisNegative:
                {
                    int i = AxisSlot(b);
                    return (uint)i < _axisNow.Length && !_axisNow[i] && _axisPrev[i];
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------ analog helpers for gameplay

        /// <summary>Left stick after deadzone and response curve (x right, y forward).</summary>
        public static Vector2 LeftStick() => Stick(Glfw.GAMEPAD_AXIS_LEFT_X, Glfw.GAMEPAD_AXIS_LEFT_Y);
        /// <summary>Right stick after deadzone and response curve (x right, y up).</summary>
        public static Vector2 RightStick() => Stick(Glfw.GAMEPAD_AXIS_RIGHT_X, Glfw.GAMEPAD_AXIS_RIGHT_Y);

        private static Vector2 Stick(int ax, int ay)
        {
            if (!Input.GamepadConnected || !Gameplay.Enabled) return Vector2.zero;
            var v = new Vector2(Input.GetPadAxis(ax), -Input.GetPadAxis(ay)); // GLFW: +Y is down
            float m = v.magnitude;
            if (m <= gamepadDeadzone) return Vector2.zero;
            float t = Mathf.Clamp01((m - gamepadDeadzone) / (1f - gamepadDeadzone));
            t = MathF.Pow(t, MathF.Max(0.1f, gamepadCurve));
            return v / m * t;
        }

        // ------------------------------------------------------------------ rebinding

        /// <summary>
        /// Starts listening for the next control: a key / mouse button / wheel fills the action's keyboard slot, a gamepad
        /// button / trigger / stick direction fills its gamepad slot. Escape or B cancels; Backspace / Delete clears the
        /// keyboard slot, the gamepad BACK button clears the gamepad slot.
        /// </summary>
        public static void StartRebind(InputAction action, Action<InputBinding> done = null)
        {
            if (action == null || action.locked) return;
            _rebindAction = action; _rebindDone = done;
            _rebindArmed = false;
        }

        public static void CancelRebind() { _rebindAction = null; _rebindDone = null; }
        private static bool _rebindArmed;

        private static void CaptureRebind()
        {
            var a = _rebindAction;
            // the press that started the rebind (Enter / A / click) is still down this frame: wait for everything to be released once
            if (!_rebindArmed)
            {
                bool anyHeld = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0) || Input.GetPadButton(Glfw.GAMEPAD_BUTTON_A);
                if (anyHeld) return;
                _rebindArmed = true;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetPadButtonDown(Glfw.GAMEPAD_BUTTON_B)) { CancelRebind(); return; }
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) { Assign(a, InputBinding.None, gamepadSlot: false); return; }
            if (Input.GetPadButtonDown(Glfw.GAMEPAD_BUTTON_BACK)) { Assign(a, InputBinding.None, gamepadSlot: true); return; }
            foreach (var k in Input.KeysDown())
            {
                if (k == KeyCode.Escape || k == KeyCode.Backspace || k == KeyCode.Delete) continue;
                Assign(a, InputBinding.Key(k), gamepadSlot: false); return;
            }
            if (Input.mouseScrollDelta.y > 0f) { Assign(a, new InputBinding(InputSource.MouseScrollUp, 0), false); return; }
            if (Input.mouseScrollDelta.y < 0f) { Assign(a, new InputBinding(InputSource.MouseScrollDown, 0), false); return; }
            for (int b = 0; b < Input.GamepadButtonCount; b++) if (Input.GetPadButtonDown(b)) { Assign(a, InputBinding.Pad(b), true); return; }
            for (int ax = 0; ax < Input.GamepadAxisCount; ax++)
            {
                float v = Input.GetPadAxis(ax);
                if (ax >= Glfw.GAMEPAD_AXIS_LEFT_TRIGGER) { if (v > 0.6f) { Assign(a, InputBinding.Axis(ax, true), true); return; } }
                else if (v > 0.75f) { Assign(a, InputBinding.Axis(ax, true), true); return; }
                else if (v < -0.75f) { Assign(a, InputBinding.Axis(ax, false), true); return; }
            }
        }

        private static void Assign(InputAction a, InputBinding b, bool gamepadSlot)
        {
            // the same physical control may drive only one action per scheme: steal it from the previous owner
            if (!b.IsNone)
                foreach (var other in Gameplay.actions)
                {
                    if (other == a) continue;
                    if (other.keyboard.Equals(b)) other.keyboard = InputBinding.None;
                    if (other.gamepad.Equals(b)) other.gamepad = InputBinding.None;
                }
            if (gamepadSlot) a.gamepad = b; else a.keyboard = b;
            var done = _rebindDone;
            CancelRebind();
            Save();
            done?.Invoke(b);
        }

        // ------------------------------------------------------------------ persistence

        private sealed class BindingDto { public string source; public int code; }
        private sealed class ActionDto { public BindingDto keyboard; public BindingDto gamepad; }
        private sealed class FileDto
        {
            public int version = 1;
            public Dictionary<string, ActionDto> gameplay = new Dictionary<string, ActionDto>();
            public float mouseSensitivity = 1f; public bool invertMouseY; public float gamepadLookSpeed = 200f; public bool invertGamepadY; public float gamepadDeadzone = 0.2f; public float gamepadCurve = 1.8f;
        }
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        private static BindingDto ToDto(InputBinding b) => new BindingDto { source = b.source.ToString(), code = b.code };
        private static InputBinding FromDto(BindingDto d) => d == null || !Enum.TryParse<InputSource>(d.source, out var s) ? InputBinding.None : new InputBinding(s, d.code);

        public static void Save()
        {
            try
            {
                var f = new FileDto { mouseSensitivity = mouseSensitivity, invertMouseY = invertMouseY, gamepadLookSpeed = gamepadLookSpeed, invertGamepadY = invertGamepadY, gamepadDeadzone = gamepadDeadzone, gamepadCurve = gamepadCurve };
                foreach (var a in Gameplay.actions) f.gameplay[a.name] = new ActionDto { keyboard = ToDto(a.keyboard), gamepad = ToDto(a.gamepad) };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(f, Json));
            }
            catch (Exception e) { Debug.LogWarning("controls.json: could not save: " + e.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var f = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), Json);
                if (f == null) return;
                mouseSensitivity = Mathf.Clamp(f.mouseSensitivity, 0.05f, 10f); invertMouseY = f.invertMouseY;
                gamepadLookSpeed = Mathf.Clamp(f.gamepadLookSpeed, 20f, 720f); invertGamepadY = f.invertGamepadY;
                gamepadDeadzone = Mathf.Clamp(f.gamepadDeadzone, 0f, 0.9f); gamepadCurve = Mathf.Clamp(f.gamepadCurve, 0.5f, 4f);
                if (f.gameplay != null)
                    foreach (var kv in f.gameplay)
                    {
                        var a = Gameplay.Get(kv.Key);
                        if (a == null || kv.Value == null) continue;
                        a.keyboard = FromDto(kv.Value.keyboard);
                        a.gamepad = FromDto(kv.Value.gamepad);
                    }
            }
            catch (Exception e) { Debug.LogWarning("controls.json: " + e.Message); }
        }
    }
}
