using System;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Tiny immediate-mode widget layer over <see cref="UiBatch"/> (pixel coordinates, origin bottom-left).
    /// Input is read straight from <see cref="Input"/>; a widget "consumes" the mouse click so overlapping
    /// regions drawn later (the 3D viewport) do not react to it. Shared by the animation editor, the VFX editor
    /// and any in-game tool UI.
    /// </summary>
    public sealed class ImUi
    {
        public UiBatch Batch;
        public float FontScale = 1.5f;
        public float LineHeight => (BitmapFont.GlyphH + 1) * FontScale + 6f;
        public Vector2 Mouse => Input.mousePosition;
        public bool MouseDown => Input.GetMouseButtonDown(0);
        public bool MouseHeld => Input.GetMouseButton(0);
        public bool RightDown => Input.GetMouseButtonDown(1);
        public float Scroll => Input.mouseScrollDelta.y;
        /// <summary>Set when a widget handled this frame's click (checked by later, lower-priority regions).</summary>
        public bool ClickConsumed;
        public bool Modal;

        public static readonly Color PanelBg = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        public static readonly Color PanelBorder = new Color(0.25f, 0.27f, 0.32f, 1f);
        public static readonly Color Text = new Color(0.88f, 0.9f, 0.92f, 1f);
        public static readonly Color TextDim = new Color(0.55f, 0.58f, 0.63f, 1f);
        public static readonly Color Accent = new Color(1f, 0.78f, 0.25f, 1f);
        public static readonly Color ButtonBg = new Color(0.18f, 0.2f, 0.25f, 1f);
        public static readonly Color ButtonHover = new Color(0.28f, 0.31f, 0.38f, 1f);
        public static readonly Color ButtonOn = new Color(0.85f, 0.55f, 0.15f, 1f);
        public static readonly Color Selected = new Color(0.95f, 0.65f, 0.2f, 0.35f);
        public static readonly Color Danger = new Color(0.8f, 0.25f, 0.2f, 1f);
        public static readonly Color Good = new Color(0.3f, 0.75f, 0.35f, 1f);

        public void BeginFrame(UiBatch batch) { Batch = batch; ClickConsumed = false; }

        public bool Hover(Rect r) => r.Contains(Mouse) && Batch.ClipContains(Mouse);

        public void Panel(Rect r, string title = null)
        {
            Batch.FillRect(r, PanelBg);
            Border(r, PanelBorder);
            if (title != null) Label(title, new Vector2(r.x + 6f, r.yMax - 4f), Accent);
        }

        public void Border(Rect r, Color c, float t = 1f)
        {
            Batch.FillRect(new Rect(r.x, r.y, r.width, t), c);
            Batch.FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            Batch.FillRect(new Rect(r.x, r.y, t, r.height), c);
            Batch.FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>Draws text with its top-left at <paramref name="topLeft"/>.</summary>
        public void Label(string text, Vector2 topLeft, Color? color = null, float scale = 0f)
        {
            Batch.DrawText(text, topLeft, scale > 0f ? scale : FontScale, color ?? Text);
        }

        public float TextWidth(string text, float scale = 0f) => Batch.MeasureText(text, scale > 0f ? scale : FontScale).x;

        /// <summary>Shortens a string with ".." so it fits <paramref name="width"/> pixels.</summary>
        public string Fit(string text, float width, float scale = 0f)
        {
            if (text == null) return "";
            float s = scale > 0f ? scale : FontScale;
            float cw = (BitmapFont.GlyphW + 1) * s;
            int max = Math.Max(1, (int)(width / cw));
            if (text.Length <= max) return text;
            return max <= 2 ? text.Substring(0, max) : text.Substring(0, max - 2) + "..";
        }

        /// <summary>Button: returns true on click. <paramref name="on"/> draws it highlighted (toggle look).</summary>
        public bool Button(Rect r, string label, bool on = false, Color? tint = null, bool enabled = true)
        {
            bool hover = !Modal && enabled && Hover(r);
            Color c = on ? ButtonOn : hover ? ButtonHover : ButtonBg;
            if (tint.HasValue && !on) c = hover ? tint.Value * 1.25f : tint.Value;
            if (!enabled) c = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f);
            Batch.FillRect(r, c);
            Border(r, hover ? Accent : PanelBorder);
            string t = Fit(label, r.width - 6f);
            Vector2 size = Batch.MeasureText(t, FontScale);
            Label(t, new Vector2(r.x + (r.width - size.x) * 0.5f, r.y + (r.height + size.y) * 0.5f), enabled ? Text : TextDim);
            if (hover && MouseDown && !ClickConsumed) { ClickConsumed = true; return true; }
            return false;
        }

        /// <summary>Clickable list row. Returns 1 on left click, 2 on right click, 0 otherwise.</summary>
        public int Row(Rect r, string label, bool selected, Color? labelColor = null, string right = null)
        {
            bool hover = !Modal && Hover(r);
            if (selected) Batch.FillRect(r, Selected);
            else if (hover) Batch.FillRect(r, new Color(1f, 1f, 1f, 0.06f));
            float rw = right != null ? TextWidth(right) + 8f : 0f;
            Label(Fit(label, r.width - 8f - rw), new Vector2(r.x + 4f, r.y + (r.height + BitmapFont.GlyphH * FontScale) * 0.5f), labelColor ?? (selected ? Accent : Text));
            if (right != null) Label(right, new Vector2(r.xMax - rw + 4f, r.y + (r.height + BitmapFont.GlyphH * FontScale) * 0.5f), TextDim);
            if (hover && !ClickConsumed)
            {
                if (MouseDown) { ClickConsumed = true; return 1; }
                if (RightDown) { ClickConsumed = true; return 2; }
            }
            return 0;
        }

        /// <summary>A small value cell: left click = next / +, right click = previous / -. Returns +1, -1 or 0.</summary>
        public int Cell(Rect r, string text, Color? bg = null, Color? fg = null)
        {
            bool hover = !Modal && Hover(r);
            Batch.FillRect(r, hover ? ButtonHover : (bg ?? ButtonBg));
            Border(r, hover ? Accent : PanelBorder);
            Label(Fit(text, r.width - 4f), new Vector2(r.x + 3f, r.y + (r.height + BitmapFont.GlyphH * FontScale) * 0.5f), fg ?? Text);
            if (hover && !ClickConsumed)
            {
                if (MouseDown) { ClickConsumed = true; return 1; }
                if (RightDown) { ClickConsumed = true; return -1; }
                if (Scroll != 0f) return Scroll > 0f ? 1 : -1;
            }
            return 0;
        }

        // ---- drag value -----------------------------------------------------------------------

        private int _dragId = -1;
        private float _dragStartX, _dragStartValue;
        private int _nextDragId;
        private bool _dragAny;

        /// <summary>Call at the start of every frame (after <see cref="BeginFrame"/>) so widget ids are stable.</summary>
        public void BeginWidgets() { _nextDragId = 0; _dragAny = false; }

        /// <summary>
        /// Numeric cell: drag horizontally to change the value by <paramref name="step"/> per 4 pixels (Shift ×10, Ctrl ÷10),
        /// mouse wheel ±step, right click resets to <paramref name="reset"/>. Returns the new value (clamped to min..max).
        /// Ids are assigned in call order, so call the same DragValue set every frame.
        /// </summary>
        public float DragValue(Rect r, string label, float value, float step, float min, float max, float reset, string format = null, Color? bg = null)
        {
            int id = _nextDragId++;
            bool hover = !Modal && Hover(r);
            bool active = _dragId == id;
            float original = value;
            if (active)
            {
                if (!MouseHeld) _dragId = -1;
                else
                {
                    float mult = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 10f : Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? 0.1f : 1f;
                    float delta = (Mouse.x - _dragStartX) / 4f;
                    value = _dragStartValue + delta * step * mult;
                    if (MathF.Abs(step) >= 1f) value = MathF.Round(value / step) * step;
                    _dragAny = true;
                }
            }
            else if (hover && !ClickConsumed)
            {
                if (MouseDown) { _dragId = id; _dragStartX = Mouse.x; _dragStartValue = value; ClickConsumed = true; }
                else if (RightDown) { value = reset; ClickConsumed = true; }
                else if (Scroll != 0f) value += (Scroll > 0f ? 1f : -1f) * step * (Input.GetKey(KeyCode.LeftShift) ? 10f : 1f);
            }
            value = Mathf.Clamp(value, min, max);
            if (MathF.Abs(value) < 1E-06f) value = 0f;

            Batch.FillRect(r, active ? ButtonOn : hover ? ButtonHover : (bg ?? ButtonBg));
            Border(r, hover || active ? Accent : PanelBorder);
            string text = value.ToString(format ?? (MathF.Abs(step) >= 1f ? "0" : step >= 0.1f ? "0.0" : "0.00"), System.Globalization.CultureInfo.InvariantCulture);
            float ty = r.y + (r.height + BitmapFont.GlyphH * FontScale) * 0.5f;
            if (!string.IsNullOrEmpty(label)) Label(Fit(label, r.width * 0.5f - 6f), new Vector2(r.x + 4f, ty), TextDim);
            float tw = TextWidth(text);
            Label(text, new Vector2(r.xMax - tw - 4f, ty), value != original || active ? Accent : Text);
            return value;
        }

        /// <summary>True while any DragValue is being dragged this frame (callers use it to mark documents dirty).</summary>
        public bool Dragging => _dragId >= 0 || _dragAny;

        /// <summary>Two-state button showing ON / OFF; returns the (possibly toggled) value.</summary>
        public bool Toggle(Rect r, string label, bool value)
        {
            if (Button(r, label + ": " + (value ? "ON" : "OFF"), value)) return !value;
            return value;
        }

        /// <summary>Small colour preview square.</summary>
        public void Swatch(Rect r, Color c)
        {
            // checkerboard behind so alpha is visible
            Batch.FillRect(r, new Color(0.35f, 0.35f, 0.35f, 1f));
            Batch.FillRect(new Rect(r.x, r.y, r.width * 0.5f, r.height * 0.5f), new Color(0.6f, 0.6f, 0.6f, 1f));
            Batch.FillRect(new Rect(r.x + r.width * 0.5f, r.y + r.height * 0.5f, r.width * 0.5f, r.height * 0.5f), new Color(0.6f, 0.6f, 0.6f, 1f));
            Batch.FillRect(r, new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a)));
            Border(r, PanelBorder);
        }

        // ---- vertical scrolling helper -------------------------------------------------------

        /// <summary>Applies the mouse wheel to a scroll offset when the mouse is over <paramref name="area"/>.</summary>
        public float ScrollArea(Rect area, float offset, float contentHeight, float step = 40f)
        {
            if (!Modal && Hover(area) && Scroll != 0f) offset -= Scroll * step;
            float max = MathF.Max(0f, contentHeight - area.height);
            return Mathf.Clamp(offset, 0f, max);
        }

        public void ScrollBar(Rect area, float offset, float contentHeight)
        {
            if (contentHeight <= area.height) return;
            float track = area.height;
            float size = MathF.Max(12f, track * area.height / contentHeight);
            float pos = (track - size) * (offset / (contentHeight - area.height));
            Batch.FillRect(new Rect(area.xMax - 4f, area.y, 3f, track), new Color(1, 1, 1, 0.08f));
            Batch.FillRect(new Rect(area.xMax - 4f, area.yMax - pos - size, 3f, size), new Color(1, 1, 1, 0.35f));
        }
    }

    /// <summary>Single-line modal text prompt (name entry) fed by the window's character callback.</summary>
    public sealed class TextPrompt
    {
        public bool Active { get; private set; }
        public string Title { get; private set; }
        public string Text { get; private set; } = "";
        private Action<string> _accept;

        public void Open(string title, string initial, Action<string> accept)
        {
            Active = true; Title = title; Text = initial ?? ""; _accept = accept;
        }

        /// <summary>Call every frame while active; handles typing / Enter / Escape.</summary>
        public void Update()
        {
            if (!Active) return;
            foreach (char c in Input.inputString)
                if (c >= ' ' && c < 127) Text += c;
            if (Input.GetKeyDown(KeyCode.Backspace) && Text.Length > 0) Text = Text.Substring(0, Text.Length - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Active = false;
                var a = _accept; _accept = null;
                a?.Invoke(Text.Trim());
            }
            else if (Input.GetKeyDown(KeyCode.Escape)) { Active = false; _accept = null; }
        }

        public void Draw(ImUi ui, Rect r)
        {
            if (!Active) return;
            ui.Batch.FillRect(r, new Color(0.05f, 0.06f, 0.08f, 0.97f));
            ui.Border(r, ImUi.Accent);
            float y = r.yMax - 8f;
            ui.Label(Title, new Vector2(r.x + 10f, y), ImUi.Accent);
            y -= ui.LineHeight;
            bool blink = ((int)(Time.unscaledTime * 2f) & 1) == 0;
            ui.Label(Text + (blink ? "_" : " "), new Vector2(r.x + 10f, y), ImUi.Text);
            y -= ui.LineHeight;
            ui.Label("ENTER = OK   ESC = CANCEL", new Vector2(r.x + 10f, y), ImUi.TextDim);
        }
    }
}
