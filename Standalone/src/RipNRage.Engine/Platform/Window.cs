using System;
using System.Runtime.InteropServices;

namespace RipNRage.Engine.Platform
{
    /// <summary>GLFW window + OpenGL 3.3 core context. Feeds the Input system with events.</summary>
    public sealed class Window : IDisposable
    {
        private IntPtr _handle;
        private int _fbWidth, _fbHeight;
        private bool _cursorDisabled;
        private bool _firstMouse = true;
        private double _lastX, _lastY;

        // Keep delegates alive (GLFW stores raw function pointers).
        private Glfw.ErrorCallback _errorCb;
        private Glfw.FramebufferSizeCallback _fbCb;
        private Glfw.KeyCallback _keyCb;
        private Glfw.MouseButtonCallback _mouseCb;
        private Glfw.CursorPosCallback _cursorCb;
        private Glfw.ScrollCallback _scrollCb;
        private Glfw.CharCallback _charCb;

        public IntPtr Handle => _handle;
        public int Width => _fbWidth;
        public int Height => _fbHeight;
        public float Aspect => _fbHeight > 0 ? (float)_fbWidth / _fbHeight : 1f;
        public string GlVersion { get; private set; }
        public string GlRenderer { get; private set; }

        public event Action<int, int> Resized;

        public Window(string title, int width, int height, bool vsync = true, bool visible = true)
        {
            NativeLoader.Register();

            _errorCb = (code, desc) => Debug.LogError($"GLFW error {code}: {Marshal.PtrToStringAnsi(desc)}");
            Glfw.SetErrorCallback(_errorCb);

            if (Glfw.Init() != Glfw.TRUE)
                throw new InvalidOperationException("glfwInit failed (is a display available?)");

            Glfw.WindowHint(Glfw.CONTEXT_VERSION_MAJOR, 3);
            Glfw.WindowHint(Glfw.CONTEXT_VERSION_MINOR, 3);
            Glfw.WindowHint(Glfw.OPENGL_PROFILE, Glfw.OPENGL_CORE_PROFILE);
            Glfw.WindowHint(Glfw.OPENGL_FORWARD_COMPAT, Glfw.TRUE); // required on macOS
            Glfw.WindowHint(Glfw.RESIZABLE, Glfw.TRUE);
            Glfw.WindowHint(Glfw.VISIBLE, visible ? Glfw.TRUE : Glfw.FALSE);
            Glfw.WindowHint(Glfw.DEPTH_BITS, 24);

            _handle = Glfw.CreateWindow(width, height, title, IntPtr.Zero, IntPtr.Zero);
            if (_handle == IntPtr.Zero)
            {
                Glfw.Terminate();
                throw new InvalidOperationException("glfwCreateWindow failed (OpenGL 3.3 core context unavailable?)");
            }

            Glfw.MakeContextCurrent(_handle);
            Glfw.SwapInterval(vsync ? 1 : 0);
            GL.LoadFunctions(Glfw.GetProcAddress);
            GlVersion = GL.GetString(GL.VERSION);
            GlRenderer = GL.GetString(GL.RENDERER);

            Glfw.GetFramebufferSize(_handle, out _fbWidth, out _fbHeight);

            _fbCb = (w, width2, height2) => { _fbWidth = width2; _fbHeight = height2; Resized?.Invoke(width2, height2); };
            _keyCb = (w, key, scancode, action, mods) =>
            {
                if (action == Glfw.PRESS) Input.OnKey(key, true);
                else if (action == Glfw.RELEASE) Input.OnKey(key, false);
            };
            _mouseCb = (w, button, action, mods) => Input.OnMouseButton(button, action == Glfw.PRESS);
            _cursorCb = (w, x, y) =>
            {
                if (_firstMouse) { _lastX = x; _lastY = y; _firstMouse = false; }
                // Unity's mouse delta is +Y up; GLFW's cursor Y grows downwards.
                Input.OnMouseMove((float)(x - _lastX), (float)(_lastY - y));
                _lastX = x; _lastY = y;
                Input.mousePosition = new Vector2((float)x, _fbHeight - (float)y);
            };
            _scrollCb = (w, dx, dy) => Input.OnScroll((float)dy);
            _charCb = (w, cp) => { if (cp < 0x10000) Input.OnChar((char)cp); };

            Glfw.SetFramebufferSizeCallback(_handle, _fbCb);
            Glfw.SetKeyCallback(_handle, _keyCb);
            Glfw.SetMouseButtonCallback(_handle, _mouseCb);
            Glfw.SetCursorPosCallback(_handle, _cursorCb);
            Glfw.SetScrollCallback(_handle, _scrollCb);
            Glfw.SetCharCallback(_handle, _charCb);
        }

        public bool ShouldClose => Glfw.WindowShouldClose(_handle) == Glfw.TRUE;
        public void Close() => Glfw.SetWindowShouldClose(_handle, Glfw.TRUE);
        public double Time => Glfw.GetTime();
        public void SetTitle(string title) => Glfw.SetWindowTitle(_handle, title);

        public void PollEvents()
        {
            Glfw.PollEvents();
            ApplyCursorState();
        }

        public void SwapBuffers() => Glfw.SwapBuffers(_handle);

        private void ApplyCursorState()
        {
            bool wantDisabled = Cursor.lockState == CursorLockMode.Locked;
            if (wantDisabled != _cursorDisabled)
            {
                _cursorDisabled = wantDisabled;
                Glfw.SetInputMode(_handle, Glfw.CURSOR, wantDisabled ? Glfw.CURSOR_DISABLED : (Cursor.visible ? Glfw.CURSOR_NORMAL : Glfw.CURSOR_HIDDEN));
                if (wantDisabled && Glfw.RawMouseMotionSupported() == Glfw.TRUE)
                    Glfw.SetInputMode(_handle, Glfw.RAW_MOUSE_MOTION, Glfw.TRUE);
                _firstMouse = true;
                Input.cursorLocked = wantDisabled;
            }
            else if (!wantDisabled)
            {
                int mode = Cursor.visible ? Glfw.CURSOR_NORMAL : Glfw.CURSOR_HIDDEN;
                if (Glfw.GetInputMode(_handle, Glfw.CURSOR) != mode) Glfw.SetInputMode(_handle, Glfw.CURSOR, mode);
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                Glfw.DestroyWindow(_handle);
                _handle = IntPtr.Zero;
            }
            Glfw.Terminate();
        }
    }
}
