using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>Key codes (values are GLFW key codes; names follow UnityEngine.KeyCode).</summary>
    public enum KeyCode
    {
        None = -1,
        Space = 32, Apostrophe = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47,
        Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57,
        Semicolon = 59, Equals = 61,
        A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75, L = 76, M = 77,
        N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
        LeftBracket = 91, Backslash = 92, RightBracket = 93, BackQuote = 96,
        Escape = 256, Return = 257, Tab = 258, Backspace = 259, Insert = 260, Delete = 261,
        RightArrow = 262, LeftArrow = 263, DownArrow = 264, UpArrow = 265,
        PageUp = 266, PageDown = 267, Home = 268, End = 269,
        CapsLock = 280, ScrollLock = 281, NumLock = 282, PrintScreen = 283, Pause = 284,
        F1 = 290, F2 = 291, F3 = 292, F4 = 293, F5 = 294, F6 = 295, F7 = 296, F8 = 297, F9 = 298, F10 = 299, F11 = 300, F12 = 301,
        Keypad0 = 320, Keypad1 = 321, Keypad2 = 322, Keypad3 = 323, Keypad4 = 324, Keypad5 = 325, Keypad6 = 326, Keypad7 = 327, Keypad8 = 328, Keypad9 = 329,
        KeypadPeriod = 330, KeypadDivide = 331, KeypadMultiply = 332, KeypadMinus = 333, KeypadPlus = 334, KeypadEnter = 335,
        LeftShift = 340, LeftControl = 341, LeftAlt = 342, LeftCommand = 343,
        RightShift = 344, RightControl = 345, RightAlt = 346, RightCommand = 347, Menu = 348,
        // Mouse buttons are mapped into the high range so one enum can describe both.
        Mouse0 = 1000, Mouse1 = 1001, Mouse2 = 1002, Mouse3 = 1003, Mouse4 = 1004,
    }

    /// <summary>
    /// Polled input state (UnityEngine.Input style). The window layer feeds it with raw
    /// events; the engine snapshots it once per frame so GetKeyDown / GetKeyUp are frame-exact.
    /// </summary>
    public static class Input
    {
        private const int MaxKeys = 1024;
        private const int MouseBase = 1000;

        private static readonly bool[] _held = new bool[MaxKeys + 16];
        private static readonly bool[] _down = new bool[MaxKeys + 16];
        private static readonly bool[] _up = new bool[MaxKeys + 16];
        private static readonly bool[] _pendingDown = new bool[MaxKeys + 16];
        private static readonly bool[] _pendingUp = new bool[MaxKeys + 16];

        private static Vector2 _mouseDeltaAccum;
        private static float _scrollAccum;
        private static readonly List<char> _chars = new List<char>();

        public static Vector2 mousePosition { get; internal set; }
        public static Vector2 mouseDelta { get; private set; }
        public static Vector2 mouseScrollDelta { get; private set; }
        public static bool anyKeyDown { get; private set; }
        public static bool cursorLocked { get; internal set; }

        /// <summary>Mouse sensitivity scale applied to raw GLFW deltas so they resemble Unity's Mouse/delta values.</summary>
        public static float mouseDeltaScale = 1f;

        public static bool GetKey(KeyCode k) => k != KeyCode.None && _held[(int)k];
        public static bool GetKeyDown(KeyCode k) => k != KeyCode.None && _down[(int)k];
        public static bool GetKeyUp(KeyCode k) => k != KeyCode.None && _up[(int)k];
        public static bool GetMouseButton(int button) => _held[MouseBase + button];
        public static bool GetMouseButtonDown(int button) => _down[MouseBase + button];
        public static bool GetMouseButtonUp(int button) => _up[MouseBase + button];

        public static float GetAxisRaw(string axis)
        {
            switch (axis)
            {
                case "Horizontal": return (GetKey(KeyCode.D) || GetKey(KeyCode.RightArrow) ? 1f : 0f) - (GetKey(KeyCode.A) || GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                case "Vertical": return (GetKey(KeyCode.W) || GetKey(KeyCode.UpArrow) ? 1f : 0f) - (GetKey(KeyCode.S) || GetKey(KeyCode.DownArrow) ? 1f : 0f);
                case "Mouse X": return mouseDelta.x;
                case "Mouse Y": return mouseDelta.y;
                case "Mouse ScrollWheel": return mouseScrollDelta.y;
                default: return 0f;
            }
        }

        public static float GetAxis(string axis) => GetAxisRaw(axis);

        public static string inputString
        {
            get { return new string(_chars.ToArray()); }
        }

        // ---- fed by the window layer ----

        internal static void OnKey(int key, bool pressed)
        {
            if (key < 0 || key >= MaxKeys) return;
            if (pressed) { if (!_held[key]) _pendingDown[key] = true; _held[key] = true; }
            else { _pendingUp[key] = true; _held[key] = false; }
        }

        internal static void OnMouseButton(int button, bool pressed)
        {
            int k = MouseBase + button;
            if (k >= _held.Length) return;
            if (pressed) { if (!_held[k]) _pendingDown[k] = true; _held[k] = true; }
            else { _pendingUp[k] = true; _held[k] = false; }
        }

        internal static void OnMouseMove(float dx, float dy) => _mouseDeltaAccum += new Vector2(dx, dy);
        internal static void OnScroll(float dy) => _scrollAccum += dy;
        internal static void OnChar(char c) => _pendingChars.Add(c);
        private static readonly List<char> _pendingChars = new List<char>();

        /// <summary>Called once per frame after polling OS events.</summary>
        internal static void BeginFrame()
        {
            anyKeyDown = false;
            for (int i = 0; i < _down.Length; i++)
            {
                _down[i] = _pendingDown[i];
                _up[i] = _pendingUp[i];
                _pendingDown[i] = false;
                _pendingUp[i] = false;
                if (_down[i]) anyKeyDown = true;
            }
            _chars.Clear(); _chars.AddRange(_pendingChars); _pendingChars.Clear();
            mouseDelta = _mouseDeltaAccum * mouseDeltaScale;
            _mouseDeltaAccum = Vector2.zero;
            mouseScrollDelta = new Vector2(0f, _scrollAccum);
            _scrollAccum = 0f;
        }

        internal static void EndFrame()
        {
        }

        // ---- automation hooks (tests / scripted sessions): feed the same paths as the window callbacks ----
        public static void SimulateKey(KeyCode key, bool pressed) => OnKey((int)key, pressed);
        public static void SimulateMouseButton(int button, bool pressed) => OnMouseButton(button, pressed);
        /// <summary>Moves the virtual cursor (pixels, origin bottom-left) and reports the delta like a real mouse.</summary>
        public static void SimulateMousePosition(Vector2 position)
        {
            OnMouseMove(position.x - mousePosition.x, position.y - mousePosition.y);
            mousePosition = position;
        }
        public static void SimulateMouseMove(float dx, float dy) => OnMouseMove(dx, dy);
        public static void SimulateChar(char c) => OnChar(c);

        /// <summary>Clears all held state (e.g. when the window loses focus).</summary>
        internal static void ResetAll()
        {
            Array.Clear(_held, 0, _held.Length);
            Array.Clear(_pendingDown, 0, _pendingDown.Length);
            Array.Clear(_pendingUp, 0, _pendingUp.Length);
            _mouseDeltaAccum = Vector2.zero;
            _scrollAccum = 0f;
        }
    }

    public enum CursorLockMode { None, Locked, Confined }

    /// <summary>UnityEngine.Cursor replacement; the window applies the state each frame.</summary>
    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; } = CursorLockMode.None;
        public static bool visible { get; set; } = true;
    }
}
