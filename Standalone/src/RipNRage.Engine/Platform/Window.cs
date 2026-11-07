using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RipNRage.Engine.Platform
{
    /// <summary>How the window occupies the screen.</summary>
    public enum DisplayMode
    {
        /// <summary>A normal, resizable, decorated window.</summary>
        Windowed,
        /// <summary>Covers one monitor at its desktop resolution (no mode switch; fast alt-tab / Cmd-Tab).</summary>
        Borderless,
        /// <summary>Exclusive fullscreen at a chosen resolution / refresh rate (switches the display mode).</summary>
        Fullscreen,
    }

    /// <summary>A connected monitor and the video modes it supports.</summary>
    public sealed class MonitorInfo
    {
        public IntPtr Handle;
        public string Name;
        public int Index;
        public bool IsPrimary;
        /// <summary>Desktop video mode (what Borderless uses).</summary>
        public Glfw.VideoMode Desktop;
        /// <summary>Supported modes, largest / fastest first, duplicates removed.</summary>
        public Glfw.VideoMode[] Modes;
        /// <summary>OS content scale (2 on a Retina display, 1.5 at 150% on Windows, ...).</summary>
        public float ContentScale;
        public override string ToString() => $"{Index}: {Name} {Desktop.width}x{Desktop.height}@{Desktop.refreshRate} scale {ContentScale:0.##}";
    }

    /// <summary>Window creation settings.</summary>
    public sealed class WindowConfig
    {
        public string Title = "Rip-n-Rage";
        /// <summary>Windowed size in logical units (points on macOS, scaled pixels on Windows / X11 HiDPI).</summary>
        public int Width = 1280, Height = 720;
        public bool VSync = true;
        public bool Visible = true;
        public DisplayMode Mode = DisplayMode.Windowed;
        /// <summary>Monitor index for Borderless / Fullscreen (-1 = primary).</summary>
        public int Monitor = -1;
        /// <summary>Exclusive fullscreen mode (0 = the monitor's desktop mode).</summary>
        public int FullscreenWidth, FullscreenHeight, RefreshRate;
    }

    /// <summary>
    /// GLFW window + OpenGL 3.3 core context. Feeds the Input system with events.
    /// <para>
    /// HiDPI: <see cref="Width"/> / <see cref="Height"/> are always the <b>framebuffer</b> size in physical pixels (the
    /// renderer and the UI work in those), while the OS reports the cursor in window units — points on macOS, which
    /// are half a pixel on Retina. Cursor positions are converted to framebuffer pixels here, so UI hit-testing is
    /// right on every display; mouse-look deltas stay in window units so the look sensitivity doesn't double on Retina.
    /// </para>
    /// </summary>
    public sealed class Window : IDisposable
    {
        private IntPtr _handle;
        private int _fbWidth, _fbHeight;
        private int _winWidth, _winHeight;
        private float _contentScale = 1f;
        private bool _cursorDisabled;
        private bool _firstMouse = true;
        private double _lastX, _lastY;

        // windowed placement remembered while fullscreen
        private int _savedX, _savedY, _savedW, _savedH;
        private bool _hasSaved;

        // Keep delegates alive (GLFW stores raw function pointers).
        private Glfw.ErrorCallback _errorCb;
        private Glfw.FramebufferSizeCallback _fbCb;
        private Glfw.WindowSizeCallback _sizeCb;
        private Glfw.WindowContentScaleCallback _scaleCb;
        private Glfw.WindowFocusCallback _focusCb;
        private Glfw.KeyCallback _keyCb;
        private Glfw.MouseButtonCallback _mouseCb;
        private Glfw.CursorPosCallback _cursorCb;
        private Glfw.ScrollCallback _scrollCb;
        private Glfw.CharCallback _charCb;
        private static Glfw.MonitorCallback _monitorCb;

        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public IntPtr Handle => _handle;
        /// <summary>Framebuffer width in physical pixels.</summary>
        public int Width => _fbWidth;
        /// <summary>Framebuffer height in physical pixels.</summary>
        public int Height => _fbHeight;
        /// <summary>Window size in OS window units (points on macOS).</summary>
        public int WindowWidth => _winWidth;
        public int WindowHeight => _winHeight;
        /// <summary>Framebuffer pixels per window unit (2 on Retina, 1 elsewhere unless the OS scales the window).</summary>
        public float PixelRatio => _winWidth > 0 ? (float)_fbWidth / _winWidth : 1f;
        /// <summary>The OS content scale of the monitor the window is on (2 on Retina; 1.25 / 1.5 on scaled Windows desktops).</summary>
        public float ContentScale => _contentScale;
        public float Aspect => _fbHeight > 0 ? (float)_fbWidth / _fbHeight : 1f;
        public string GlVersion { get; private set; }
        public string GlRenderer { get; private set; }
        public bool Focused { get; private set; } = true;

        public DisplayMode Mode { get; private set; } = DisplayMode.Windowed;
        /// <summary>Monitor index used for the current fullscreen mode, or the one the window is mostly on when windowed.</summary>
        public int MonitorIndex => Mode == DisplayMode.Windowed ? CurrentMonitorIndex() : _fsMonitor;
        private int _fsMonitor;

        /// <summary>Framebuffer size changed (physical pixels).</summary>
        public event Action<int, int> Resized;
        /// <summary>The window moved to a monitor with a different content scale (e.g. Retina ↔ external display).</summary>
        public event Action<float> ContentScaleChanged;
        /// <summary>A monitor was connected or disconnected.</summary>
        public static event Action MonitorsChanged;

        public Window(string title, int width, int height, bool vsync = true, bool visible = true)
            : this(new WindowConfig { Title = title, Width = width, Height = height, VSync = vsync, Visible = visible }) { }

        public Window(WindowConfig cfg)
        {
            NativeLoader.Register();

            _errorCb = (code, desc) => Debug.LogError($"GLFW error {code}: {Marshal.PtrToStringAnsi(desc)}");
            Glfw.SetErrorCallback(_errorCb);

            if (IsMacOS)
            {
                // keep the working directory (GLFW would chdir into Contents/Resources inside an .app bundle; the asset
                // locator handles bundles itself) and keep the standard app menu (Cmd-Q, Cmd-H, Cmd-M).
                Glfw.InitHint(Glfw.COCOA_CHDIR_RESOURCES, Glfw.FALSE);
                Glfw.InitHint(Glfw.COCOA_MENUBAR, Glfw.TRUE);
            }

            if (Glfw.Init() != Glfw.TRUE)
                throw new InvalidOperationException("glfwInit failed (is a display available?)");

            _monitorCb ??= (m, e) => MonitorsChanged?.Invoke();
            Glfw.SetMonitorCallback(_monitorCb);

            Glfw.WindowHint(Glfw.CONTEXT_VERSION_MAJOR, 3);
            Glfw.WindowHint(Glfw.CONTEXT_VERSION_MINOR, 3);
            Glfw.WindowHint(Glfw.OPENGL_PROFILE, Glfw.OPENGL_CORE_PROFILE);
            Glfw.WindowHint(Glfw.OPENGL_FORWARD_COMPAT, Glfw.TRUE); // required on macOS
            Glfw.WindowHint(Glfw.RESIZABLE, Glfw.TRUE);
            // start hidden: the window is positioned / switched to fullscreen first, so it doesn't flash windowed
            Glfw.WindowHint(Glfw.VISIBLE, Glfw.FALSE);
            Glfw.WindowHint(Glfw.DEPTH_BITS, 24);
            // macOS: native-resolution framebuffer on Retina (the UI is laid out in pixels and stays crisp; the 3D
            // scene's resolution is controlled separately with PostProcessStack.PixelScale / render scale).
            Glfw.WindowHint(Glfw.COCOA_RETINA_FRAMEBUFFER, Glfw.TRUE);
            // dual-GPU MacBooks: let macOS choose; without this the discrete GPU is forced on for the whole session
            Glfw.WindowHint(Glfw.COCOA_GRAPHICS_SWITCHING, Glfw.TRUE);
            // Windows / X11: the requested size is in logical units, so 1280x720 isn't tiny on a 150-200% desktop
            Glfw.WindowHint(Glfw.SCALE_TO_MONITOR, Glfw.TRUE);

            // keep the requested windowed size inside the primary monitor's work area
            IntPtr primary = Glfw.GetPrimaryMonitor();
            if (primary != IntPtr.Zero)
            {
                Glfw.GetMonitorWorkarea(primary, out _, out _, out int ww, out int wh);
                Glfw.GetMonitorContentScale(primary, out float psx, out _);
                // on Windows / X11 the work area is in pixels but the size hint is logical (SCALE_TO_MONITOR)
                float div = IsMacOS || psx <= 0f ? 1f : psx;
                int maxW = (int)(ww / div * 0.95f), maxH = (int)(wh / div * 0.9f);
                if (maxW > 320 && maxH > 200 && (cfg.Width > maxW || cfg.Height > maxH))
                {
                    float k = Math.Min((float)maxW / cfg.Width, (float)maxH / cfg.Height);
                    cfg.Width = (int)(cfg.Width * k); cfg.Height = (int)(cfg.Height * k);
                }
            }

            _handle = Glfw.CreateWindow(cfg.Width, cfg.Height, cfg.Title, IntPtr.Zero, IntPtr.Zero);
            if (_handle == IntPtr.Zero)
            {
                Glfw.Terminate();
                throw new InvalidOperationException("glfwCreateWindow failed (OpenGL 3.3 core context unavailable?)");
            }
            Glfw.SetWindowSizeLimits(_handle, 320, 180, Glfw.DONT_CARE, Glfw.DONT_CARE);

            Glfw.MakeContextCurrent(_handle);
            VSync = cfg.VSync;
            Glfw.SwapInterval(cfg.VSync ? 1 : 0);
            GL.LoadFunctions(Glfw.GetProcAddress);
            GlVersion = GL.GetString(GL.VERSION);
            GlRenderer = GL.GetString(GL.RENDERER);

            RefreshSizes();
            Glfw.GetWindowContentScale(_handle, out _contentScale, out _);
            if (_contentScale <= 0f) _contentScale = 1f;

            _fbCb = (w, width2, height2) => { _fbWidth = width2; _fbHeight = height2; Resized?.Invoke(width2, height2); };
            _sizeCb = (w, width2, height2) => { _winWidth = width2; _winHeight = height2; };
            _scaleCb = (w, sx, sy) =>
            {
                if (sx <= 0f || Math.Abs(sx - _contentScale) < 0.001f) return;
                _contentScale = sx;
                ContentScaleChanged?.Invoke(sx);
            };
            _focusCb = (w, focused) =>
            {
                Focused = focused == Glfw.TRUE;
                // Cmd-Tab / Alt-Tab swallows the key-up events of whatever was held: don't leave keys stuck down
                if (!Focused) Input.ResetAll();
                _firstMouse = true;
            };
            _keyCb = (w, key, scancode, action, mods) =>
            {
                if (action == Glfw.PRESS) { Input.OnKey(key, true); HandleWindowShortcut(key, mods); }
                else if (action == Glfw.RELEASE) Input.OnKey(key, false);
            };
            _mouseCb = (w, button, action, mods) => Input.OnMouseButton(button, action == Glfw.PRESS);
            _cursorCb = (w, x, y) =>
            {
                if (_firstMouse) { _lastX = x; _lastY = y; _firstMouse = false; }
                // Unity's mouse delta is +Y up; GLFW's cursor Y grows downwards. Deltas stay in window units.
                Input.OnMouseMove((float)(x - _lastX), (float)(_lastY - y));
                _lastX = x; _lastY = y;
                // position in framebuffer pixels, origin bottom-left (what the UI and the editors hit-test against)
                float sx = _winWidth > 0 ? (float)_fbWidth / _winWidth : 1f;
                float sy = _winHeight > 0 ? (float)_fbHeight / _winHeight : 1f;
                Input.mousePosition = new Vector2((float)x * sx, _fbHeight - (float)y * sy);
            };
            _scrollCb = (w, dx, dy) => Input.OnScroll((float)dy);
            _charCb = (w, cp) => { if (cp < 0x10000) Input.OnChar((char)cp); };

            Glfw.SetFramebufferSizeCallback(_handle, _fbCb);
            Glfw.SetWindowSizeCallback(_handle, _sizeCb);
            Glfw.SetWindowContentScaleCallback(_handle, _scaleCb);
            Glfw.SetWindowFocusCallback(_handle, _focusCb);
            Glfw.SetKeyCallback(_handle, _keyCb);
            Glfw.SetMouseButtonCallback(_handle, _mouseCb);
            Glfw.SetCursorPosCallback(_handle, _cursorCb);
            Glfw.SetScrollCallback(_handle, _scrollCb);
            Glfw.SetCharCallback(_handle, _charCb);

            if (cfg.Mode != DisplayMode.Windowed)
                SetDisplayMode(cfg.Mode, cfg.Monitor, cfg.FullscreenWidth, cfg.FullscreenHeight, cfg.RefreshRate);
            if (cfg.Visible) Glfw.ShowWindow(_handle);
            RefreshSizes();
        }

        private void RefreshSizes()
        {
            Glfw.GetFramebufferSize(_handle, out _fbWidth, out _fbHeight);
            Glfw.GetWindowSize(_handle, out _winWidth, out _winHeight);
        }

        /// <summary>When false, the built-in fullscreen shortcuts (Alt+Enter / F11, Cmd+Ctrl+F on macOS) are ignored.</summary>
        public bool FullscreenShortcuts { get; set; } = true;
        /// <summary>Raised after the display mode changes (from code, a shortcut or the OS).</summary>
        public event Action<DisplayMode> DisplayModeChanged;

        private void HandleWindowShortcut(int key, int mods)
        {
            if (!FullscreenShortcuts) return;
            const int MOD_CONTROL = 0x0002, MOD_ALT = 0x0004, MOD_SUPER = 0x0008;
            bool toggle = IsMacOS
                ? key == (int)KeyCode.F && (mods & MOD_SUPER) != 0 && (mods & MOD_CONTROL) != 0
                : (key == (int)KeyCode.Return && (mods & MOD_ALT) != 0) || key == (int)KeyCode.F11;
            if (toggle) ToggleFullscreen();
        }

        // ------------------------------------------------------------------ monitors / display modes

        /// <summary>Connected monitors (primary first is not guaranteed; see <see cref="MonitorInfo.IsPrimary"/>).</summary>
        public static MonitorInfo[] GetMonitors()
        {
            var handles = Glfw.GetMonitors();
            IntPtr primary = Glfw.GetPrimaryMonitor();
            var list = new MonitorInfo[handles.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                var m = handles[i];
                Glfw.GetMonitorContentScale(m, out float sx, out _);
                var modes = new List<Glfw.VideoMode>(Glfw.GetVideoModes(m));
                // largest first, highest refresh first; drop duplicates that only differ in colour depth
                modes.Sort((a, b) => a.width != b.width ? b.width.CompareTo(a.width) : a.height != b.height ? b.height.CompareTo(a.height) : b.refreshRate.CompareTo(a.refreshRate));
                var unique = new List<Glfw.VideoMode>();
                foreach (var v in modes)
                    if (unique.Count == 0 || !SameMode(unique[unique.Count - 1], v)) unique.Add(v);
                list[i] = new MonitorInfo
                {
                    Handle = m, Index = i, Name = Glfw.GetMonitorName(m), IsPrimary = m == primary,
                    Desktop = Glfw.GetVideoMode(m), Modes = unique.ToArray(), ContentScale = sx > 0f ? sx : 1f,
                };
            }
            return list;
        }

        private static bool SameMode(Glfw.VideoMode a, Glfw.VideoMode b) => a.width == b.width && a.height == b.height && a.refreshRate == b.refreshRate;

        private static IntPtr MonitorHandle(int index)
        {
            var all = Glfw.GetMonitors();
            if (index >= 0 && index < all.Length) return all[index];
            return Glfw.GetPrimaryMonitor();
        }

        private static int IndexOf(IntPtr monitor)
        {
            var all = Glfw.GetMonitors();
            for (int i = 0; i < all.Length; i++) if (all[i] == monitor) return i;
            return 0;
        }

        /// <summary>Monitor holding the centre of the window (or the fullscreen monitor).</summary>
        public int CurrentMonitorIndex()
        {
            IntPtr fs = Glfw.GetWindowMonitor(_handle);
            if (fs != IntPtr.Zero) return IndexOf(fs);
            Glfw.GetWindowPos(_handle, out int x, out int y);
            int cx = x + _winWidth / 2, cy = y + _winHeight / 2;
            var all = Glfw.GetMonitors();
            int best = 0; long bestD = long.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                // the work area is in the same screen coordinates as the window position on every platform
                Glfw.GetMonitorWorkarea(all[i], out int mx, out int my, out int mw, out int mh);
                if (cx >= mx && cx < mx + mw && cy >= my && cy < my + mh) return i;
                long dx = cx - (mx + mw / 2), dy = cy - (my + mh / 2);
                if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Switches between windowed, borderless and exclusive fullscreen. <paramref name="monitor"/> = -1 keeps the
        /// monitor the window is on; width / height / refresh = 0 use the monitor's desktop mode.
        /// </summary>
        public void SetDisplayMode(DisplayMode mode, int monitor = -1, int width = 0, int height = 0, int refreshRate = 0)
        {
            if (monitor < 0) monitor = Mode == DisplayMode.Windowed ? CurrentMonitorIndex() : _fsMonitor;
            IntPtr mon = MonitorHandle(monitor);
            monitor = IndexOf(mon);

            if (Mode == DisplayMode.Windowed && mode != DisplayMode.Windowed) SaveWindowedPlacement();

            switch (mode)
            {
                case DisplayMode.Windowed:
                {
                    Glfw.SetWindowAttrib(_handle, Glfw.DECORATED, Glfw.TRUE);
                    Glfw.SetWindowAttrib(_handle, Glfw.FLOATING, Glfw.FALSE);
                    int x = _savedX, y = _savedY, w = _savedW, h = _savedH;
                    if (!_hasSaved)
                    {
                        Glfw.GetMonitorWorkarea(mon, out int ax, out int ay, out int aw, out int ah);
                        w = Math.Min(1280, aw * 3 / 4); h = Math.Min(720, ah * 3 / 4);
                        x = ax + (aw - w) / 2; y = ay + (ah - h) / 2;
                    }
                    Glfw.SetWindowMonitor(_handle, IntPtr.Zero, x, y, Math.Max(320, w), Math.Max(180, h), Glfw.DONT_CARE);
                    break;
                }
                case DisplayMode.Borderless:
                {
                    var vm = Glfw.GetVideoMode(mon);
                    Glfw.SetWindowAttrib(_handle, Glfw.AUTO_ICONIFY, Glfw.FALSE);
                    if (IsMacOS)
                    {
                        // macOS: a fullscreen window at the desktop mode is GLFW's "windowed fullscreen": it hides the
                        // menu bar and Dock and never switches the display mode (the desktop mode is passed back as-is).
                        Glfw.SetWindowMonitor(_handle, mon, 0, 0, vm.width, vm.height, vm.refreshRate);
                    }
                    else
                    {
                        // Windows / X11: an undecorated window covering the monitor (no exclusive mode, instant alt-tab)
                        if (Glfw.GetWindowMonitor(_handle) != IntPtr.Zero)
                            Glfw.SetWindowMonitor(_handle, IntPtr.Zero, 0, 0, vm.width, vm.height, Glfw.DONT_CARE);
                        Glfw.SetWindowAttrib(_handle, Glfw.DECORATED, Glfw.FALSE);
                        Glfw.GetMonitorPos(mon, out int mx, out int my);
                        Glfw.SetWindowPos(_handle, mx, my);
                        Glfw.SetWindowSize(_handle, vm.width, vm.height);
                    }
                    break;
                }
                case DisplayMode.Fullscreen:
                {
                    var vm = Glfw.GetVideoMode(mon);
                    int w = width > 0 ? width : vm.width, h = height > 0 ? height : vm.height;
                    int hz = refreshRate > 0 ? refreshRate : (width > 0 ? Glfw.DONT_CARE : vm.refreshRate);
                    Glfw.SetWindowAttrib(_handle, Glfw.DECORATED, Glfw.TRUE);
                    Glfw.SetWindowAttrib(_handle, Glfw.AUTO_ICONIFY, Glfw.TRUE);
                    Glfw.SetWindowMonitor(_handle, mon, 0, 0, w, h, hz);
                    break;
                }
            }

            Mode = mode;
            _fsMonitor = monitor;
            RefreshSizes();
            Glfw.GetWindowContentScale(_handle, out float cs, out _);
            if (cs > 0f && Math.Abs(cs - _contentScale) > 0.001f) { _contentScale = cs; ContentScaleChanged?.Invoke(cs); }
            Glfw.SwapInterval(VSync ? 1 : 0); // some drivers reset the swap interval on a mode change
            _firstMouse = true;
            Resized?.Invoke(_fbWidth, _fbHeight);
            DisplayModeChanged?.Invoke(mode);
        }

        /// <summary>Windowed ↔ borderless (or back to the last fullscreen mode used).</summary>
        public void ToggleFullscreen()
        {
            if (Mode == DisplayMode.Windowed) SetDisplayMode(_lastFullscreen);
            else { _lastFullscreen = Mode; SetDisplayMode(DisplayMode.Windowed); }
        }
        private DisplayMode _lastFullscreen = DisplayMode.Borderless;

        private void SaveWindowedPlacement()
        {
            Glfw.GetWindowPos(_handle, out _savedX, out _savedY);
            Glfw.GetWindowSize(_handle, out _savedW, out _savedH);
            _hasSaved = _savedW > 0 && _savedH > 0;
        }

        // ------------------------------------------------------------------ misc

        public bool VSync { get; private set; }
        /// <summary>Changes the swap interval at runtime (graphics settings).</summary>
        public void SetVSync(bool on) { VSync = on; Glfw.SwapInterval(on ? 1 : 0); }

        public bool ShouldClose => Glfw.WindowShouldClose(_handle) == Glfw.TRUE;
        public void Close() => Glfw.SetWindowShouldClose(_handle, Glfw.TRUE);
        public double Time => Glfw.GetTime();
        public void SetTitle(string title) => Glfw.SetWindowTitle(_handle, title);
        public bool Minimized => Glfw.GetWindowAttrib(_handle, Glfw.ICONIFIED) == Glfw.TRUE;

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
            Glfw.SetMonitorCallback(null);
            Glfw.Terminate();
        }
    }
}
