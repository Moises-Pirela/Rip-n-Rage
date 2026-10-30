using System;
using System.Runtime.InteropServices;

namespace RipNRage.Engine.Platform
{
    /// <summary>Minimal GLFW 3.3 binding (only what the engine needs).</summary>
    public static class Glfw
    {
        private const string Lib = NativeLoader.GlfwLib;

        public const int CONTEXT_VERSION_MAJOR = 0x00022002;
        public const int CONTEXT_VERSION_MINOR = 0x00022003;
        public const int OPENGL_PROFILE = 0x00022008;
        public const int OPENGL_CORE_PROFILE = 0x00032001;
        public const int OPENGL_FORWARD_COMPAT = 0x00022006;
        public const int RESIZABLE = 0x00020003;
        public const int VISIBLE = 0x00020004;
        public const int SAMPLES = 0x0002100D;
        public const int DOUBLEBUFFER = 0x00021010;
        public const int SRGB_CAPABLE = 0x0002100E;
        public const int DEPTH_BITS = 0x00021005;

        public const int CURSOR = 0x00033001;
        public const int CURSOR_NORMAL = 0x00034001;
        public const int CURSOR_HIDDEN = 0x00034002;
        public const int CURSOR_DISABLED = 0x00034003;
        public const int RAW_MOUSE_MOTION = 0x00033005;
        public const int STICKY_KEYS = 0x00033002;

        public const int PRESS = 1;
        public const int RELEASE = 0;
        public const int REPEAT = 2;

        public const int TRUE = 1;
        public const int FALSE = 0;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ErrorCallback(int error, IntPtr description);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FramebufferSizeCallback(IntPtr window, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void KeyCallback(IntPtr window, int key, int scancode, int action, int mods);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MouseButtonCallback(IntPtr window, int button, int action, int mods);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CursorPosCallback(IntPtr window, double x, double y);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ScrollCallback(IntPtr window, double xoffset, double yoffset);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CharCallback(IntPtr window, uint codepoint);

        [DllImport(Lib, EntryPoint = "glfwInit", CallingConvention = CallingConvention.Cdecl)] public static extern int Init();
        [DllImport(Lib, EntryPoint = "glfwTerminate", CallingConvention = CallingConvention.Cdecl)] public static extern void Terminate();
        [DllImport(Lib, EntryPoint = "glfwGetVersionString", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GetVersionStringPtr();
        public static string GetVersionString() => Marshal.PtrToStringAnsi(GetVersionStringPtr());
        [DllImport(Lib, EntryPoint = "glfwSetErrorCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetErrorCallback(ErrorCallback cb);
        [DllImport(Lib, EntryPoint = "glfwWindowHint", CallingConvention = CallingConvention.Cdecl)] public static extern void WindowHint(int hint, int value);
        [DllImport(Lib, EntryPoint = "glfwCreateWindow", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr CreateWindow(int width, int height, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, IntPtr monitor, IntPtr share);
        [DllImport(Lib, EntryPoint = "glfwDestroyWindow", CallingConvention = CallingConvention.Cdecl)] public static extern void DestroyWindow(IntPtr window);
        [DllImport(Lib, EntryPoint = "glfwMakeContextCurrent", CallingConvention = CallingConvention.Cdecl)] public static extern void MakeContextCurrent(IntPtr window);
        [DllImport(Lib, EntryPoint = "glfwSwapBuffers", CallingConvention = CallingConvention.Cdecl)] public static extern void SwapBuffers(IntPtr window);
        [DllImport(Lib, EntryPoint = "glfwSwapInterval", CallingConvention = CallingConvention.Cdecl)] public static extern void SwapInterval(int interval);
        [DllImport(Lib, EntryPoint = "glfwPollEvents", CallingConvention = CallingConvention.Cdecl)] public static extern void PollEvents();
        [DllImport(Lib, EntryPoint = "glfwWindowShouldClose", CallingConvention = CallingConvention.Cdecl)] public static extern int WindowShouldClose(IntPtr window);
        [DllImport(Lib, EntryPoint = "glfwSetWindowShouldClose", CallingConvention = CallingConvention.Cdecl)] public static extern void SetWindowShouldClose(IntPtr window, int value);
        [DllImport(Lib, EntryPoint = "glfwGetProcAddress", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string procname);
        [DllImport(Lib, EntryPoint = "glfwGetFramebufferSize", CallingConvention = CallingConvention.Cdecl)] public static extern void GetFramebufferSize(IntPtr window, out int width, out int height);
        [DllImport(Lib, EntryPoint = "glfwGetWindowSize", CallingConvention = CallingConvention.Cdecl)] public static extern void GetWindowSize(IntPtr window, out int width, out int height);
        [DllImport(Lib, EntryPoint = "glfwSetFramebufferSizeCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetFramebufferSizeCallback(IntPtr window, FramebufferSizeCallback cb);
        [DllImport(Lib, EntryPoint = "glfwSetKeyCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetKeyCallback(IntPtr window, KeyCallback cb);
        [DllImport(Lib, EntryPoint = "glfwSetMouseButtonCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetMouseButtonCallback(IntPtr window, MouseButtonCallback cb);
        [DllImport(Lib, EntryPoint = "glfwSetCursorPosCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetCursorPosCallback(IntPtr window, CursorPosCallback cb);
        [DllImport(Lib, EntryPoint = "glfwSetScrollCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetScrollCallback(IntPtr window, ScrollCallback cb);
        [DllImport(Lib, EntryPoint = "glfwSetCharCallback", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SetCharCallback(IntPtr window, CharCallback cb);
        [DllImport(Lib, EntryPoint = "glfwGetKey", CallingConvention = CallingConvention.Cdecl)] public static extern int GetKey(IntPtr window, int key);
        [DllImport(Lib, EntryPoint = "glfwGetMouseButton", CallingConvention = CallingConvention.Cdecl)] public static extern int GetMouseButton(IntPtr window, int button);
        [DllImport(Lib, EntryPoint = "glfwGetCursorPos", CallingConvention = CallingConvention.Cdecl)] public static extern void GetCursorPos(IntPtr window, out double x, out double y);
        [DllImport(Lib, EntryPoint = "glfwSetCursorPos", CallingConvention = CallingConvention.Cdecl)] public static extern void SetCursorPos(IntPtr window, double x, double y);
        [DllImport(Lib, EntryPoint = "glfwSetInputMode", CallingConvention = CallingConvention.Cdecl)] public static extern void SetInputMode(IntPtr window, int mode, int value);
        [DllImport(Lib, EntryPoint = "glfwGetInputMode", CallingConvention = CallingConvention.Cdecl)] public static extern int GetInputMode(IntPtr window, int mode);
        [DllImport(Lib, EntryPoint = "glfwRawMouseMotionSupported", CallingConvention = CallingConvention.Cdecl)] public static extern int RawMouseMotionSupported();
        [DllImport(Lib, EntryPoint = "glfwGetTime", CallingConvention = CallingConvention.Cdecl)] public static extern double GetTime();
        [DllImport(Lib, EntryPoint = "glfwSetWindowTitle", CallingConvention = CallingConvention.Cdecl)] public static extern void SetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
        [DllImport(Lib, EntryPoint = "glfwGetPrimaryMonitor", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr GetPrimaryMonitor();
        [DllImport(Lib, EntryPoint = "glfwFocusWindow", CallingConvention = CallingConvention.Cdecl)] public static extern void FocusWindow(IntPtr window);
        [DllImport(Lib, EntryPoint = "glfwGetWindowAttrib", CallingConvention = CallingConvention.Cdecl)] public static extern int GetWindowAttrib(IntPtr window, int attrib);

        // ---- joysticks / gamepads (GLFW 3.3+: SDL_GameController-style mapping) ----
        public const int JOYSTICK_1 = 0, JOYSTICK_LAST = 15;
        public const int GAMEPAD_BUTTON_A = 0, GAMEPAD_BUTTON_B = 1, GAMEPAD_BUTTON_X = 2, GAMEPAD_BUTTON_Y = 3,
            GAMEPAD_BUTTON_LEFT_BUMPER = 4, GAMEPAD_BUTTON_RIGHT_BUMPER = 5, GAMEPAD_BUTTON_BACK = 6, GAMEPAD_BUTTON_START = 7,
            GAMEPAD_BUTTON_GUIDE = 8, GAMEPAD_BUTTON_LEFT_THUMB = 9, GAMEPAD_BUTTON_RIGHT_THUMB = 10,
            GAMEPAD_BUTTON_DPAD_UP = 11, GAMEPAD_BUTTON_DPAD_RIGHT = 12, GAMEPAD_BUTTON_DPAD_DOWN = 13, GAMEPAD_BUTTON_DPAD_LEFT = 14;
        public const int GAMEPAD_AXIS_LEFT_X = 0, GAMEPAD_AXIS_LEFT_Y = 1, GAMEPAD_AXIS_RIGHT_X = 2, GAMEPAD_AXIS_RIGHT_Y = 3,
            GAMEPAD_AXIS_LEFT_TRIGGER = 4, GAMEPAD_AXIS_RIGHT_TRIGGER = 5;

        /// <summary>GLFWgamepadstate: 15 buttons (GLFW_PRESS / GLFW_RELEASE) then 6 axes in -1..1.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct GamepadState
        {
            public fixed byte buttons[15];
            public fixed float axes[6];
        }

        [DllImport(Lib, EntryPoint = "glfwJoystickPresent", CallingConvention = CallingConvention.Cdecl)] public static extern int JoystickPresent(int jid);
        [DllImport(Lib, EntryPoint = "glfwJoystickIsGamepad", CallingConvention = CallingConvention.Cdecl)] public static extern int JoystickIsGamepad(int jid);
        [DllImport(Lib, EntryPoint = "glfwGetGamepadState", CallingConvention = CallingConvention.Cdecl)] public static extern unsafe int GetGamepadState(int jid, GamepadState* state);
        [DllImport(Lib, EntryPoint = "glfwGetGamepadName", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GetGamepadNamePtr(int jid);
        public static string GetGamepadName(int jid) { var p = GetGamepadNamePtr(jid); return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p); }
        [DllImport(Lib, EntryPoint = "glfwGetJoystickName", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GetJoystickNamePtr(int jid);
        public static string GetJoystickName(int jid) { var p = GetJoystickNamePtr(jid); return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p); }
        [DllImport(Lib, EntryPoint = "glfwUpdateGamepadMappings", CallingConvention = CallingConvention.Cdecl)] public static extern int UpdateGamepadMappings([MarshalAs(UnmanagedType.LPStr)] string mappings);
    }
}
