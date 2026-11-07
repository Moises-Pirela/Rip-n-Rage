using System;
using System.Collections.Generic;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using System.Runtime.InteropServices;
using RipNRage.Engine.Platform;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.ImGuiIntegration
{
    /// <summary>
    /// Owns a Dear ImGui context for an editor: feeds it the engine's keyboard / mouse state, runs one frame per
    /// rendered frame and draws the result with <see cref="ImGuiRenderer"/>.
    /// <code>
    /// _imgui = new ImGuiController();          // after the window and GL context exist
    /// ...
    /// _imgui.NewFrame(window);                 // once per frame, before building any windows
    /// ImGui.Begin("Parts"); ... ImGui.End();
    /// _imgui.Render(window);                   // draws, and releases the mouse / keyboard if ImGui did not want it
    /// </code>
    /// <para>
    /// HiDPI: ImGui is driven in window points and every size in editor code is a point, while the font atlas is
    /// rasterised at point size x pixel ratio and scaled back down, so text is crisp on a Retina display instead of
    /// being upscaled. The renderer covers the whole framebuffer and scales ImGui's clip rectangles to match.
    /// </para>
    /// </summary>
    public sealed class ImGuiController : IDisposable
    {
        private readonly IntPtr _context;
        private ImGuiRenderer _renderer;
        private float _pixelRatio;
        private bool _frameStarted;

        /// <summary>True when ImGui wants the mouse (cursor over a panel): the editor's own picking should stand down.</summary>
        public bool WantsMouse { get; private set; }
        /// <summary>True when ImGui wants the keyboard (a text field has focus): the editor's shortcuts should stand down.</summary>
        public bool WantsKeyboard { get; private set; }

        /// <summary>
        /// False the first time this editor runs for this user: no layout file existed, so the editor should build its
        /// default dock layout. Afterwards ImGui restores whatever layout the user left behind.
        /// </summary>
        public bool HasSavedLayout { get; }

        private IntPtr _iniPathUtf8;

        /// <param name="layoutName">
        /// Name of the window layout file, kept in the user's data folder (not the working tree) so each editor
        /// remembers its own panel arrangement. Null keeps ImGui's default imgui.ini next to the executable.
        /// </param>
        public unsafe ImGuiController(bool docking = true, string layoutName = null)
        {
            string iniPath = null;
            if (layoutName != null)
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "layouts");
                System.IO.Directory.CreateDirectory(dir);
                iniPath = System.IO.Path.Combine(dir, layoutName + ".ini");
            }
            HasSavedLayout = iniPath != null
                ? System.IO.File.Exists(iniPath)
                : System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "imgui.ini"));
            _context = ImGui.CreateContext();
            ImGui.SetCurrentContext(_context);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            if (docking) io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigWindowsMoveFromTitleBarOnly = true;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.HasMouseCursors;
            if (iniPath != null)
            {
                // ImGui keeps the pointer, so the UTF-8 path has to stay alive for the life of the context
                _iniPathUtf8 = Marshal.StringToCoTaskMemUTF8(iniPath);
                io.NativePtr->IniFilename = (byte*)_iniPathUtf8;
            }

            StyleEditorDark();
            _renderer = new ImGuiRenderer();
            SetPixelRatio(1f);
        }

        /// <summary>Dark theme close to the editors' existing palette (warm accent on a cool grey panel).</summary>
        private static void StyleEditorDark()
        {
            ImGui.StyleColorsDark();
            var s = ImGui.GetStyle();
            s.WindowRounding = 3f;
            s.FrameRounding = 2f;
            s.GrabRounding = 2f;
            s.ScrollbarRounding = 2f;
            s.WindowBorderSize = 1f;
            s.FrameBorderSize = 0f;
            s.WindowPadding = new Vec2(8f, 8f);
            s.FramePadding = new Vec2(6f, 3f);
            s.ItemSpacing = new Vec2(6f, 4f);

            void Set(ImGuiCol c, float r, float g, float b, float a) => s.Colors[(int)c] = new Vec4(r, g, b, a);
            Set(ImGuiCol.WindowBg, 0.08f, 0.09f, 0.11f, 0.94f);
            Set(ImGuiCol.Border, 0.25f, 0.27f, 0.32f, 1f);
            Set(ImGuiCol.TitleBgActive, 0.22f, 0.17f, 0.07f, 1f);
            Set(ImGuiCol.FrameBg, 0.18f, 0.20f, 0.25f, 1f);
            Set(ImGuiCol.FrameBgHovered, 0.28f, 0.31f, 0.38f, 1f);
            Set(ImGuiCol.Button, 0.18f, 0.20f, 0.25f, 1f);
            Set(ImGuiCol.ButtonHovered, 0.28f, 0.31f, 0.38f, 1f);
            Set(ImGuiCol.ButtonActive, 0.85f, 0.55f, 0.15f, 1f);
            Set(ImGuiCol.Header, 0.95f, 0.65f, 0.20f, 0.35f);
            Set(ImGuiCol.HeaderHovered, 0.95f, 0.65f, 0.20f, 0.50f);
            Set(ImGuiCol.CheckMark, 1f, 0.78f, 0.25f, 1f);
            Set(ImGuiCol.SliderGrab, 0.85f, 0.55f, 0.15f, 1f);
            Set(ImGuiCol.Tab, 0.14f, 0.16f, 0.20f, 1f);
            Set(ImGuiCol.TabSelected, 0.32f, 0.24f, 0.10f, 1f);
        }

        /// <summary>Base font size in points (the default ImGui font is 13).</summary>
        public const float FontSizePoints = 13f;

        /// <summary>
        /// Rebuilds the font atlas for a new pixel density: glyphs are rasterised at point size x ratio and drawn back
        /// at 1/ratio, which keeps them sharp on Retina while editor code keeps working in points.
        /// </summary>
        private unsafe void SetPixelRatio(float ratio)
        {
            if (Math.Abs(ratio - _pixelRatio) < 0.01f) return;
            _pixelRatio = ratio;
            var io = ImGui.GetIO();
            io.Fonts.Clear();
            var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
            cfg->SizePixels = FontSizePoints * ratio;
            io.Fonts.AddFontDefault(cfg);
            ImGuiNative.ImFontConfig_destroy(cfg);
            io.FontGlobalScale = 1f / ratio;
            _renderer.RebuildFontAtlas();
        }

        /// <summary>Starts an ImGui frame for this window. Call once per frame before any ImGui call.</summary>
        public void NewFrame(Window window)
        {
            ImGui.SetCurrentContext(_context);
            var io = ImGui.GetIO();

            float ratio = Math.Max(1f, window.PixelRatio);
            SetPixelRatio(ratio);
            // ImGui works in window points; the renderer covers the denser framebuffer
            io.DisplaySize = new Vec2(window.Width / ratio, window.Height / ratio);
            io.DisplayFramebufferScale = new Vec2(ratio, ratio);
            io.DeltaTime = Math.Max(1e-4f, Time.unscaledDeltaTime);

            FeedInput(io, window, ratio);
            ImGui.NewFrame();
            _frameStarted = true;
        }

        private void FeedInput(ImGuiIOPtr io, Window window, float ratio)
        {
            // Input.mousePosition is in framebuffer pixels, origin bottom-left; ImGui wants points, origin top-left
            Vec2 mouse = new Vec2(Input.mousePosition.x / ratio, (window.Height - Input.mousePosition.y) / ratio);
            io.AddMousePosEvent(mouse.X, mouse.Y);
            for (int b = 0; b < 3; b++) io.AddMouseButtonEvent(b, Input.GetMouseButton(b));
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f) io.AddMouseWheelEvent(0f, wheel);

            io.AddKeyEvent(ImGuiKey.ModCtrl, Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            io.AddKeyEvent(ImGuiKey.ModShift, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            io.AddKeyEvent(ImGuiKey.ModAlt, Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModSuper, Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand));

            foreach (var pair in KeyMap)
            {
                bool down = Input.GetKey(pair.Key);
                if (down != _keyWasDown.TryGetValue(pair.Key, out bool was) || down != was)
                {
                    io.AddKeyEvent(pair.Value, down);
                    _keyWasDown[pair.Key] = down;
                }
            }

            foreach (char c in Input.inputString) io.AddInputCharacter(c);
        }

        private readonly Dictionary<KeyCode, bool> _keyWasDown = new Dictionary<KeyCode, bool>();

        /// <summary>Ends the frame and draws it. Also refreshes <see cref="WantsMouse"/> / <see cref="WantsKeyboard"/>.</summary>
        public void Render(Window window)
        {
            if (!_frameStarted) return;
            _frameStarted = false;
            ImGui.SetCurrentContext(_context);
            ImGui.Render();
            _renderer.Render(ImGui.GetDrawData(), window.Width, window.Height);
            var io = ImGui.GetIO();
            WantsMouse = io.WantCaptureMouse;
            WantsKeyboard = io.WantCaptureKeyboard;
        }

        /// <summary>
        /// Full-window dock host: a borderless window over the viewport's work area (below the main menu bar) with a
        /// dock space inside, optionally reserving <paramref name="bottomReserve"/> points for a status bar. Returns
        /// the dock space id. Pair with <see cref="EndDockHost"/>.
        /// </summary>
        public uint BeginDockHost(string id, float bottomReserve = 0f)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(new Vec2(viewport.WorkSize.X, Math.Max(1f, viewport.WorkSize.Y - bottomReserve)));
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vec2.Zero);
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize
                      | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus
                      | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground;
            ImGui.Begin(id, flags);
            ImGui.PopStyleVar(3);
            uint dockspace = ImGui.GetID(id + "##dockspace");
            ImGui.DockSpace(dockspace, Vec2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);
            return dockspace;
        }

        public void EndDockHost() => ImGui.End();

        /// <summary>A status strip across the bottom of the window, <paramref name="height"/> points tall.</summary>
        public bool BeginStatusBar(string id, float height)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(new Vec2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height));
            ImGui.SetNextWindowSize(new Vec2(viewport.WorkSize.X, height));
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking
                      | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = ImGui.Begin(id, flags);
            ImGui.PopStyleVar();
            return open;
        }

        /// <summary>Draws an engine texture, flipping V because engine textures are stored bottom-up.</summary>
        public static void Image(Texture2D texture, Vec2 size)
        {
            if (texture == null) return;
            ImGui.Image(ImGuiRenderer.TextureId(texture), size, new Vec2(0f, 1f), new Vec2(1f, 0f));
        }

        /// <summary>Draws a sprite's sub-rectangle of its atlas, V-flipped like <see cref="Image"/>.</summary>
        public static void Image(Sprite sprite, Vec2 size)
        {
            if (sprite?.texture == null) return;
            var t = sprite.texture;
            var r = sprite.rect;
            var uv0 = new Vec2(r.x / t.width, (r.y + r.height) / t.height);
            var uv1 = new Vec2((r.x + r.width) / t.width, r.y / t.height);
            ImGui.Image(ImGuiRenderer.TextureId(t), size, uv0, uv1);
        }

        /// <summary>Points per framebuffer pixel in use (2 on a Retina display).</summary>
        public float PixelRatio => _pixelRatio;

        /// <summary>
        /// Converts an ImGui rectangle (points, origin top-left) to an engine <see cref="Rect"/> in framebuffer
        /// pixels with the origin bottom-left, so <see cref="UiBatch"/> can draw inside an ImGui window.
        /// </summary>
        public Rect ToEngineRect(Vec2 min, Vec2 max, Window window)
        {
            float r = _pixelRatio;
            float x = min.X * r, w = (max.X - min.X) * r, h = (max.Y - min.Y) * r;
            float y = window.Height - max.Y * r;
            return new Rect(x, y, w, h);
        }

        /// <summary>Content rectangle of the ImGui window being built, in engine pixels.</summary>
        public Rect CurrentWindowRect(Window window)
        {
            Vec2 min = ImGui.GetCursorScreenPos();          // top-left of the remaining content area
            Vec2 avail = ImGui.GetContentRegionAvail();
            return ToEngineRect(min, min + avail, window);
        }

        public void Dispose()
        {
            _renderer?.Dispose();
            _renderer = null;
            if (_context != IntPtr.Zero) ImGui.DestroyContext(_context);
            if (_iniPathUtf8 != IntPtr.Zero) { Marshal.FreeCoTaskMem(_iniPathUtf8); _iniPathUtf8 = IntPtr.Zero; }
        }

        /// <summary>Engine key codes ImGui needs (text editing, navigation and the shortcut modifiers).</summary>
        private static readonly Dictionary<KeyCode, ImGuiKey> KeyMap = new Dictionary<KeyCode, ImGuiKey>
        {
            [KeyCode.Tab] = ImGuiKey.Tab,
            [KeyCode.LeftArrow] = ImGuiKey.LeftArrow,
            [KeyCode.RightArrow] = ImGuiKey.RightArrow,
            [KeyCode.UpArrow] = ImGuiKey.UpArrow,
            [KeyCode.DownArrow] = ImGuiKey.DownArrow,
            [KeyCode.PageUp] = ImGuiKey.PageUp,
            [KeyCode.PageDown] = ImGuiKey.PageDown,
            [KeyCode.Home] = ImGuiKey.Home,
            [KeyCode.End] = ImGuiKey.End,
            [KeyCode.Insert] = ImGuiKey.Insert,
            [KeyCode.Delete] = ImGuiKey.Delete,
            [KeyCode.Backspace] = ImGuiKey.Backspace,
            [KeyCode.Space] = ImGuiKey.Space,
            [KeyCode.Return] = ImGuiKey.Enter,
            [KeyCode.Escape] = ImGuiKey.Escape,
            [KeyCode.KeypadEnter] = ImGuiKey.KeypadEnter,
            [KeyCode.A] = ImGuiKey.A,
            [KeyCode.C] = ImGuiKey.C,
            [KeyCode.V] = ImGuiKey.V,
            [KeyCode.X] = ImGuiKey.X,
            [KeyCode.Y] = ImGuiKey.Y,
            [KeyCode.Z] = ImGuiKey.Z,
        };
    }
}
