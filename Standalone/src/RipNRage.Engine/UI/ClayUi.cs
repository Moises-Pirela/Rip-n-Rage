using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.UI.Clay;

namespace RipNRage.Engine.UI
{
    /// <summary>How an element is sized along one axis (lengths in reference pixels of the 1080p canvas).</summary>
    public struct Size
    {
        public Clay_SizingType type;
        public float min, max;
        public static Size Fit(float min = 0f, float max = 0f) => new Size { type = Clay_SizingType.Fit, min = min, max = max };
        public static Size Grow(float min = 0f, float max = 0f) => new Size { type = Clay_SizingType.Grow, min = min, max = max };
        public static Size Fixed(float px) => new Size { type = Clay_SizingType.Fixed, min = px, max = px };
        /// <summary>0..1 of the parent's inner size.</summary>
        public static Size Percent(float p) => new Size { type = Clay_SizingType.Percent, min = p };
    }

    /// <summary>Padding in reference pixels.</summary>
    public struct Pad
    {
        public float left, right, top, bottom;
        public Pad(float all) { left = right = top = bottom = all; }
        public Pad(float x, float y) { left = right = x; top = bottom = y; }
        public Pad(float left, float right, float top, float bottom) { this.left = left; this.right = right; this.top = top; this.bottom = bottom; }
        public static implicit operator Pad(float all) => new Pad(all);
    }

    /// <summary>Floating (absolutely positioned) element settings.</summary>
    public struct Float
    {
        public bool enabled;
        public Clay_FloatingAttachToElement attachTo;
        /// <summary>Point of this element that attaches to <see cref="parent"/>'s point.</summary>
        public Clay_FloatingAttachPointType element, parent;
        /// <summary>Offset in reference pixels, +y down (Clay's convention).</summary>
        public Vector2 offset;
        public short zIndex;
        public bool passthrough;
        public static Float ToRoot(Clay_FloatingAttachPointType point, Vector2 offset = default, short z = 0) =>
            new Float { enabled = true, attachTo = Clay_FloatingAttachToElement.Root, element = point, parent = point, offset = offset, zIndex = z };
        public static Float ToParent(Clay_FloatingAttachPointType element, Clay_FloatingAttachPointType parent, Vector2 offset = default, short z = 0) =>
            new Float { enabled = true, attachTo = Clay_FloatingAttachToElement.Parent, element = element, parent = parent, offset = offset, zIndex = z };
    }

    /// <summary>
    /// One Clay element declaration in engine terms (engine colours, reference-pixel lengths, sprites, custom drawing).
    /// Converted to a <see cref="Clay_ElementDeclaration"/> by <see cref="ClayUi.Box(in El)"/>.
    /// </summary>
    public struct El
    {
        public string id;
        public int idIndex;
        public Size width, height;
        public Pad padding;
        public float gap;
        public bool vertical;
        public Clay_LayoutAlignmentX alignX;
        public Clay_LayoutAlignmentY alignY;
        public Color background;
        public Color borderColor;
        public float border;
        public bool clipX, clipY;
        /// <summary>Child offset in reference pixels for clipped containers (scrolling), +y down.</summary>
        public Vector2 childOffset;
        public Float floating;
        /// <summary>Draw this sprite stretched over the element, tinted by <see cref="background"/> (white when unset).</summary>
        public Sprite image;
        /// <summary>Called with the element's final rectangle (screen pixels, origin bottom-left) at render time.</summary>
        public Action<UiBatch, Rect, float> custom;
        public float aspectRatio;
    }

    /// <summary>Text style. <see cref="size"/> is the bitmap-font glyph scale at the 1080p reference (4 = 28 px glyphs).</summary>
    public struct TextStyle
    {
        public float size;
        public Color color;
        public Clay_TextAlignment align;
        public Clay_TextWrapMode wrap;
        public bool shadow;
        public TextStyle(float size, Color color, bool shadow = false, Clay_TextAlignment align = Clay_TextAlignment.Left, Clay_TextWrapMode wrap = Clay_TextWrapMode.Words)
        { this.size = size; this.color = color; this.shadow = shadow; this.align = align; this.wrap = wrap; }
    }

    /// <summary>
    /// Clay (https://github.com/nicbarker/clay) layout context drawing through <see cref="UiBatch"/>.
    /// Each UI owner (a menu, the HUD, an editor panel) keeps its own instance: Clay resolves hover state against the
    /// layout computed by the same context the frame before, so independent screens must not share one.
    /// <code>
    /// ui.Begin(batch);
    /// using (ui.Box(new El { width = Size.Grow(), height = Size.Grow(), alignX = Clay_LayoutAlignmentX.Center }))
    ///     ui.Text("PAUSED", new TextStyle(6f, Color.white));
    /// ui.End();
    /// </code>
    /// Lengths are reference pixels of a 1080p-high canvas (scaled by <see cref="UiBatch.Scale"/>), so layouts match
    /// the rest of the UI on every resolution and on Retina framebuffers.
    /// </summary>
    public sealed unsafe class ClayUi : IDisposable
    {
        private IntPtr _ctx;
        private byte* _arena;
        private UiBatch _ui;
        private float _scale = 1f;
        private int _height;
        private bool _building;

        // per-frame storage: strings handed to Clay must live until the render commands are consumed
        private readonly List<IntPtr> _blocks = new List<IntPtr>();
        private byte* _block; private int _blockUsed, _blockCap;
        private readonly List<Sprite> _images = new List<Sprite>();
        private readonly List<Action<UiBatch, Rect, float>> _customs = new List<Action<UiBatch, Rect, float>>();

        private static bool _verified;
        private static BitmapFont _measureFont;

        /// <summary>When false the pointer is parked off-screen: nothing reports hover (locked cursor, gamepad navigation).</summary>
        public bool PointerEnabled = true;
        /// <summary>Screen pixels per reference pixel this frame.</summary>
        public float Scale => _scale;
        public bool IsBuilding => _building;

        public ClayUi(int maxElements = 2048)
        {
            Platform.NativeLoader.Register();
            if (!_verified) { ClayNative.VerifyLayout(); _verified = true; }
            // Clay sizes a new context from the *default* limits when no context is current (and from the current
            // context's limits otherwise), so detach, set the defaults for this one, size it, then restore.
            IntPtr previous = ClayNative.Clay_GetCurrentContext();
            ClayNative.Clay_SetCurrentContext(IntPtr.Zero);
            ClayNative.Clay_SetMaxElementCount(maxElements); // also sets the word cache to 2x
            uint size = ClayNative.Clay_MinMemorySize();
            _arena = (byte*)NativeMemory.AllocZeroed(size);
            var arena = ClayNative.Clay_CreateArenaWithCapacityAndMemory(size, _arena);
            var handler = new Clay_ErrorHandler { errorHandlerFunction = &OnError };
            _ctx = ClayNative.Clay_Initialize(arena, new Clay_Dimensions(1920, 1080), handler);
            if (_ctx == IntPtr.Zero) throw new InvalidOperationException("Clay_Initialize failed");
            ClayNative.Clay_SetMeasureTextFunction(&MeasureText, null);
            NewBlock(16 * 1024);
            if (previous != IntPtr.Zero) ClayNative.Clay_SetCurrentContext(previous);
            MemoryBytes = size;
        }

        /// <summary>Size of this context's Clay arena.</summary>
        public uint MemoryBytes { get; }

        // ------------------------------------------------------------------ frame

        /// <summary>Starts a layout covering the whole framebuffer of <paramref name="ui"/>.</summary>
        public void Begin(UiBatch ui)
        {
            if (_building) throw new InvalidOperationException("ClayUi.Begin called twice without End");
            _ui = ui;
            _measureFont = ui.Font;
            _scale = ui.Scale;
            _height = ui.Height;
            ResetFrameStorage();
            ClayNative.Clay_SetCurrentContext(_ctx);
            // Clay evicts stale text-measure entries only when a lookup hits the same hash bucket, so text that
            // changes every frame (counters, profiler numbers) slowly fills the cache. Measuring the monospaced
            // bitmap font costs next to nothing: start every layout with an empty cache instead.
            ClayNative.Clay_ResetMeasureTextCache();
            ClayNative.Clay_SetLayoutDimensions(new Clay_Dimensions(ui.Width, ui.Height));
            Vector2 m = Input.mousePosition;
            bool pointer = PointerEnabled && !Input.cursorLocked;
            var p = pointer ? new Clay_Vector2(m.x, ui.Height - m.y) : new Clay_Vector2(-100000f, -100000f);
            ClayNative.Clay_SetPointerState(p, (byte)(pointer && Input.GetMouseButton(0) ? 1 : 0));
            ClayNative.Clay_UpdateScrollContainers(0, new Clay_Vector2(0f, pointer ? Input.mouseScrollDelta.y * 40f * _scale : 0f), Time.unscaledDeltaTime);
            ClayNative.Clay_BeginLayout();
            _building = true;
        }

        /// <summary>Finishes the layout and draws it. <paramref name="alpha"/> fades everything (menu fade-ins).</summary>
        public void End(float alpha = 1f)
        {
            if (!_building) return;
            _building = false;
            ClayNative.Clay_SetCurrentContext(_ctx);
            var cmds = ClayNative.Clay_EndLayout();
            Render(cmds, alpha);
        }

        // ------------------------------------------------------------------ declaration

        /// <summary>Closes the element when disposed: <c>using (ui.Box(...)) { children }</c>.</summary>
        public readonly ref struct Scope
        {
            public void Dispose() => ClayNative.Clay__CloseElement();
        }

        public Scope Box(in El e)
        {
            var d = Convert(e);
            ClayNative.Clay__OpenElement();
            ClayNative.Clay__ConfigureOpenElementPtr(&d);
            return default;
        }

        /// <summary>A leaf element (no children).</summary>
        public void Leaf(in El e) { using (Box(e)) { } }

        /// <summary>True while declaring the children of an element the pointer was over last frame.</summary>
        public bool Hovered => ClayNative.Clay_Hovered() != 0;
        /// <summary>Hovered and the left button went down this frame.</summary>
        public bool Clicked => Hovered && Input.GetMouseButtonDown(0);

        public void Text(string text, in TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var cfg = new Clay_TextElementConfig
            {
                textColor = ToClay(style.color),
                fontSize = FontSize(style.size),
                wrapMode = style.wrap,
                textAlignment = style.align,
                userData = style.shadow ? (void*)1 : null,
            };
            ClayNative.Clay__OpenTextElement(Str(text), ClayNative.Clay__StoreTextElementConfig(cfg));
        }

        /// <summary>Element id for <paramref name="name"/> (same hash as CLAY_IDI).</summary>
        public Clay_ElementId Id(string name, int index = 0) { ClayNative.Clay_SetCurrentContext(_ctx); return ClayNative.Clay__HashString(Str(name), (uint)index, 0); }

        /// <summary>The pointer was over the named element in the last computed layout.</summary>
        public bool PointerOver(string name, int index = 0) => ClayNative.Clay_PointerOver(Id(name, index)) != 0;

        /// <summary>Final rectangle of a named element from the last layout (screen pixels, origin bottom-left).</summary>
        public bool TryGetBounds(string name, out Rect r, int index = 0)
        {
            var data = ClayNative.Clay_GetElementData(Id(name, index));
            r = ToScreen(data.boundingBox);
            return data.found != 0;
        }

        /// <summary>Bitmap-font text size for the measure / render callbacks: Clay's fontSize is the line height in pixels.</summary>
        private ushort FontSize(float glyphScale) => (ushort)Math.Clamp(MathF.Round(glyphScale * _scale * BitmapFont.Cell), 1, ushort.MaxValue);

        /// <summary>Line height in screen pixels of a text style (for spacing decisions outside Clay).</summary>
        public float LineHeight(float glyphScale) => FontSize(glyphScale);

        private ushort Px(float referencePx) => (ushort)Math.Clamp(MathF.Round(referencePx * _scale), 0, ushort.MaxValue);

        private Clay_ElementDeclaration Convert(in El e)
        {
            var d = new Clay_ElementDeclaration();
            if (e.id != null) d.id = Id(e.id, e.idIndex);
            d.layout.sizing.width = Axis(e.width);
            d.layout.sizing.height = Axis(e.height);
            d.layout.padding = new Clay_Padding { left = Px(e.padding.left), right = Px(e.padding.right), top = Px(e.padding.top), bottom = Px(e.padding.bottom) };
            d.layout.childGap = Px(e.gap);
            d.layout.childAlignment = new Clay_ChildAlignment { x = e.alignX, y = e.alignY };
            d.layout.layoutDirection = e.vertical ? Clay_LayoutDirection.TopToBottom : Clay_LayoutDirection.LeftToRight;
            d.backgroundColor = ToClay(e.background);
            if (e.border > 0f && e.borderColor.a > 0f)
            {
                ushort b = Math.Max((ushort)1, Px(e.border));
                d.border = new Clay_BorderElementConfig { color = ToClay(e.borderColor), width = new Clay_BorderWidth { left = b, right = b, top = b, bottom = b } };
            }
            if (e.clipX || e.clipY)
                d.clip = new Clay_ClipElementConfig { horizontal = (byte)(e.clipX ? 1 : 0), vertical = (byte)(e.clipY ? 1 : 0), childOffset = new Clay_Vector2(e.childOffset.x * _scale, e.childOffset.y * _scale) };
            if (e.floating.enabled)
            {
                var f = e.floating;
                d.floating = new Clay_FloatingElementConfig
                {
                    attachTo = f.attachTo,
                    attachPoints = new Clay_FloatingAttachPoints { element = f.element, parent = f.parent },
                    offset = new Clay_Vector2(f.offset.x * _scale, f.offset.y * _scale),
                    zIndex = f.zIndex,
                    pointerCaptureMode = f.passthrough ? Clay_PointerCaptureMode.Passthrough : Clay_PointerCaptureMode.Capture,
                };
            }
            if (e.image != null)
            {
                _images.Add(e.image);
                d.image.imageData = (void*)_images.Count; // 1-based index
            }
            if (e.custom != null)
            {
                _customs.Add(e.custom);
                d.custom.customData = (void*)_customs.Count;
            }
            if (e.aspectRatio > 0f) d.aspectRatio.aspectRatio = e.aspectRatio;
            return d;
        }

        private Clay_SizingAxis Axis(Size s) => s.type == Clay_SizingType.Percent
            ? new Clay_SizingAxis { type = s.type, min = s.min }
            : new Clay_SizingAxis { type = s.type, min = s.min * _scale, max = s.max * _scale };

        private static Clay_Color ToClay(Color c) => new Clay_Color { r = c.r, g = c.g, b = c.b, a = c.a };

        // ------------------------------------------------------------------ strings

        private Clay_String Str(string s)
        {
            int max = Encoding.UTF8.GetMaxByteCount(s.Length);
            if (_blockUsed + max > _blockCap) NewBlock(Math.Max(16 * 1024, max));
            byte* dst = _block + _blockUsed;
            int n = Encoding.UTF8.GetBytes(s, new Span<byte>(dst, max));
            _blockUsed += n;
            return new Clay_String { isStaticallyAllocated = 0, length = n, chars = dst };
        }

        private void NewBlock(int size)
        {
            _block = (byte*)NativeMemory.Alloc((nuint)size);
            _blocks.Add((IntPtr)_block);
            _blockUsed = 0; _blockCap = size;
        }

        private void ResetFrameStorage()
        {
            // keep the largest block, free the rest
            if (_blocks.Count > 1)
            {
                for (int i = 0; i < _blocks.Count - 1; i++) NativeMemory.Free((void*)_blocks[i]);
                var last = _blocks[_blocks.Count - 1];
                _blocks.Clear(); _blocks.Add(last);
            }
            _blockUsed = 0;
            _images.Clear(); _customs.Clear();
        }

        // ------------------------------------------------------------------ rendering

        private Rect ToScreen(Clay_BoundingBox b) => new Rect(b.x, _height - b.y - b.height, b.width, b.height);

        private static Color Tint(Clay_Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        private void Render(Clay_RenderCommandArray cmds, float alpha)
        {
            var ui = _ui;
            int clips = 0;
            Span<char> chars = stackalloc char[512];
            for (int i = 0; i < cmds.length; i++)
            {
                ref Clay_RenderCommand c = ref cmds.internalArray[i];
                Rect r = ToScreen(c.boundingBox);
                switch (c.commandType)
                {
                    case Clay_RenderCommandType.Rectangle:
                        if (c.renderData.rectangle.backgroundColor.a > 0f) ui.FillRect(r, Tint(c.renderData.rectangle.backgroundColor, alpha));
                        break;
                    case Clay_RenderCommandType.Border:
                    {
                        var b = c.renderData.border; Color col = Tint(b.color, alpha);
                        if (b.width.top > 0) ui.FillRect(new Rect(r.x, r.yMax - b.width.top, r.width, b.width.top), col);
                        if (b.width.bottom > 0) ui.FillRect(new Rect(r.x, r.y, r.width, b.width.bottom), col);
                        if (b.width.left > 0) ui.FillRect(new Rect(r.x, r.y + b.width.bottom, b.width.left, r.height - b.width.top - b.width.bottom), col);
                        if (b.width.right > 0) ui.FillRect(new Rect(r.xMax - b.width.right, r.y + b.width.bottom, b.width.right, r.height - b.width.top - b.width.bottom), col);
                        break;
                    }
                    case Clay_RenderCommandType.Text:
                    {
                        var t = c.renderData.text;
                        int n = Math.Min(t.stringContents.length, chars.Length);
                        for (int k = 0; k < n; k++) { byte ch = t.stringContents.chars[k]; chars[k] = ch < 128 ? (char)ch : '?'; }
                        float scale = t.fontSize / (float)BitmapFont.Cell;
                        var pos = new Vector2(r.x, r.yMax);
                        var span = chars.Slice(0, n);
                        if (c.userData != null)
                        {
                            float o = MathF.Max(1f, scale * 0.5f);
                            ui.Font.Draw(ui, span, new Vector2(pos.x + o, pos.y - o), scale, new Color(0f, 0f, 0f, t.textColor.a * 0.85f * alpha));
                        }
                        ui.Font.Draw(ui, span, pos, scale, Tint(t.textColor, alpha));
                        break;
                    }
                    case Clay_RenderCommandType.Image:
                    {
                        int idx = (int)(nint)c.renderData.image.imageData - 1;
                        if (idx < 0 || idx >= _images.Count) break;
                        var bg = c.renderData.image.backgroundColor;
                        Color tint = bg.a > 0f || bg.r > 0f || bg.g > 0f || bg.b > 0f ? Tint(bg, alpha) : new Color(1f, 1f, 1f, alpha);
                        ui.Draw(_images[idx], r, tint);
                        break;
                    }
                    case Clay_RenderCommandType.Custom:
                    {
                        int idx = (int)(nint)c.renderData.custom.customData - 1;
                        if (idx >= 0 && idx < _customs.Count) _customs[idx](ui, r, alpha);
                        break;
                    }
                    case Clay_RenderCommandType.ScissorStart: ui.PushClip(r); clips++; break;
                    case Clay_RenderCommandType.ScissorEnd: if (clips > 0) { ui.PopClip(); clips--; } break;
                }
            }
            while (clips-- > 0) ui.PopClip();
        }

        // ------------------------------------------------------------------ callbacks

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static Clay_Dimensions MeasureText(Clay_StringSlice text, Clay_TextElementConfig* config, void* userData)
        {
            // the built-in font is monospaced: 6 px advance and 8 px line per glyph pixel of scale (fontSize = line height)
            float scale = config->fontSize / (float)BitmapFont.Cell;
            int cols = 0, maxCols = 0, lines = 1;
            for (int i = 0; i < text.length; i++)
            {
                byte b = text.chars[i];
                if (b == (byte)'\n') { lines++; if (cols > maxCols) maxCols = cols; cols = 0; continue; }
                if ((b & 0xC0) != 0x80) cols++; // count UTF-8 lead bytes only
            }
            if (cols > maxCols) maxCols = cols;
            return new Clay_Dimensions(maxCols * (BitmapFont.GlyphW + 1) * scale, lines * config->fontSize);
        }

        private static readonly HashSet<Clay_ErrorType> _reported = new HashSet<Clay_ErrorType>();

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnError(Clay_ErrorData e)
        {
            if (!_reported.Add(e.errorType)) return; // once per kind: these fire every frame
            string msg = e.errorText.chars != null ? Encoding.UTF8.GetString(e.errorText.chars, e.errorText.length) : "";
            Debug.LogError($"Clay {e.errorType}: {msg}");
        }

        public void Dispose()
        {
            foreach (var b in _blocks) NativeMemory.Free((void*)b);
            _blocks.Clear();
            if (_arena != null)
            {
                if (ClayNative.Clay_GetCurrentContext() == _ctx) ClayNative.Clay_SetCurrentContext(IntPtr.Zero);
                NativeMemory.Free(_arena);
                _arena = null; _ctx = IntPtr.Zero;
            }
        }
    }
}
