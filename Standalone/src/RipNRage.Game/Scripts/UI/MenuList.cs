using System;
using System.Collections.Generic;

/// <summary>One entry of a <see cref="MenuList"/>.</summary>
public sealed class MenuItem
{
    public string label;
    /// <summary>Optional smaller line drawn under the label (e.g. "FROM THE LAST CHECKPOINT").</summary>
    public string hint;
    public bool enabled = true;
    public Action onActivate;
    /// <summary>Settings rows: current value shown right-aligned (e.g. "ON", "2048").</summary>
    public Func<string> valueText;
    /// <summary>Settings rows: called with +1 / -1 (Enter / click / Right = +1, Left / right-click = -1).</summary>
    public Action<int> onAdjust;
    public bool IsSetting => onAdjust != null;

    public MenuItem(string label, Action onActivate, string hint = null, bool enabled = true)
    {
        this.label = label; this.onActivate = onActivate; this.hint = hint; this.enabled = enabled;
    }
}

/// <summary>
/// Immediate-mode vertical menu drawn with the engine's bitmap font through <see cref="UiBatch"/>.
/// Shared by the death screen, the pause menu and the main menu. Keyboard (arrows / W S, Enter / Space)
/// and mouse (hover selects, left click activates) both work; sizes are in reference pixels of the
/// 1080p canvas and scale with <see cref="UiBatch.Scale"/>. Nothing here touches gameplay state: the
/// caller reacts to the <see cref="MenuItem"/> returned by <see cref="HandleKeyboard"/> / <see cref="Draw"/>.
/// </summary>
public sealed class MenuList
{
    public readonly List<MenuItem> items = new List<MenuItem>();
    public int selected;
    public bool interactable = true;

    // layout (reference pixels)
    public float itemWidth = 460f;
    public float itemHeight = 64f;
    public float itemGap = 12f;
    public float labelScale = 4f;   // glyph scale at the reference resolution (7 px glyphs -> 28 px)
    public float hintScale = 2f;

    // colours
    public Color normalText = new Color(0.86f, 0.86f, 0.88f, 1f);
    public Color highlightText = new Color(1f, 0.85f, 0.35f, 1f);
    public Color disabledText = new Color(0.45f, 0.45f, 0.48f, 1f);
    public Color boxColor = new Color(0f, 0f, 0f, 0.5f);
    public Color boxHighlight = new Color(0.5f, 0.07f, 0.05f, 0.8f);
    public Color borderColor = new Color(1f, 1f, 1f, 0.16f);

    /// <summary>Raised when the highlighted entry changes (keyboard or mouse), for a UI tick sound.</summary>
    public Action<int> onSelectionChanged;
    /// <summary>Raised when a settings row was adjusted (for a tick sound / saving).</summary>
    public Action<MenuItem> onAdjusted;
    /// <summary>Rows shown at once (0 = all); longer lists scroll to keep the selection visible.</summary>
    public int visibleRows;

    private Vector2 _lastMouse;
    private bool _mouseActive;
    private int _scroll;

    public MenuItem Add(string label, Action onActivate, string hint = null, bool enabled = true)
    {
        var item = new MenuItem(label, onActivate, hint, enabled);
        items.Add(item);
        if (items.Count == 1 || (!items[selected].enabled && enabled)) selected = items.Count - 1;
        return item;
    }

    /// <summary>Adds a settings row: <paramref name="value"/> renders the current value, <paramref name="adjust"/> changes it by ±1.</summary>
    public MenuItem AddSetting(string label, Func<string> value, Action<int> adjust, string hint = null)
    {
        var item = Add(label, null, hint);
        item.valueText = value; item.onAdjust = adjust;
        return item;
    }

    /// <summary>Changes the selected settings row by <paramref name="dir"/>; returns true when something was adjusted.</summary>
    public bool Adjust(int dir)
    {
        var item = Selected;
        if (item == null || !item.enabled || item.onAdjust == null) return false;
        item.onAdjust(dir);
        onAdjusted?.Invoke(item);
        return true;
    }

    public MenuItem Selected => items.Count > 0 ? items[Mathf.Clamp(selected, 0, items.Count - 1)] : null;

    /// <summary>Total height in screen pixels for the current canvas scale.</summary>
    public float Height(UiBatch ui)
    {
        int n = visibleRows > 0 ? Mathf.Min(visibleRows, items.Count) : items.Count;
        return (n * itemHeight + Mathf.Max(0, n - 1) * itemGap) * ui.Scale;
    }

    /// <summary>
    /// Forget the mouse position seen so far: hovering only counts after the mouse has moved again.
    /// Call when the menu opens, so a cursor that was locked (or parked over a button) cannot select or
    /// click anything by accident.
    /// </summary>
    public void ResetMouse()
    {
        _mouseActive = false;
        _lastMouse = Input.mousePosition;
    }

    /// <summary>
    /// Keyboard / gamepad navigation through the UI scheme (arrows, W S A D, D-pad and left stick with auto-repeat;
    /// Enter / Space / A confirms); call once per frame from Update. Returns the activated item or null.
    /// </summary>
    public MenuItem HandleKeyboard()
    {
        if (!interactable || items.Count == 0 || InputSystem.Rebinding) return null;
        var UI = InputSystem.UI;
        int dir = 0;
        if (InputSystem.UiDown || Input.GetKeyDown(KeyCode.S)) dir = 1;
        else if (InputSystem.UiUp || Input.GetKeyDown(KeyCode.W)) dir = -1;
        if (dir != 0) Move(dir);

        if (InputSystem.UiRight || Input.GetKeyDown(KeyCode.D)) { if (Adjust(1)) return null; }
        else if (InputSystem.UiLeft || Input.GetKeyDown(KeyCode.A)) { if (Adjust(-1)) return null; }

        if (UI["submit"].Pressed || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            var item = Selected;
            if (item != null && item.enabled)
            {
                if (item.IsSetting) { Adjust(1); return null; }
                return item;
            }
        }
        return null;
    }

    private void Move(int dir)
    {
        if (items.Count == 0) return;
        int start = selected;
        for (int n = 0; n < items.Count; n++)
        {
            selected = (selected + dir + items.Count) % items.Count;
            if (items[selected].enabled) break;
        }
        if (selected != start) onSelectionChanged?.Invoke(selected);
    }

    /// <summary>
    /// Draws the list horizontally centred on <paramref name="centerX"/> with its top edge at <paramref name="topY"/>
    /// (pixels, origin bottom-left) and handles mouse hover / click. <paramref name="alpha"/> fades the whole list.
    /// Returns the item clicked this frame, or null.
    /// </summary>
    public MenuItem Draw(UiBatch ui, float centerX, float topY, float alpha = 1f)
    {
        if (items.Count == 0 || alpha <= 0.001f) return null;
        float s = ui.Scale;
        float w = itemWidth * s, h = itemHeight * s, gap = itemGap * s;

        Vector2 mouse = Input.mousePosition;
        if ((mouse - _lastMouse).sqrMagnitude > 1f) { _mouseActive = true; _lastMouse = mouse; }
        bool mouseUsable = interactable && _mouseActive && !Input.cursorLocked;

        // scrolling window of rows
        int first = 0, last = items.Count;
        if (visibleRows > 0 && items.Count > visibleRows)
        {
            if (selected < _scroll) _scroll = selected;
            if (selected >= _scroll + visibleRows) _scroll = selected - visibleRows + 1;
            _scroll = Mathf.Clamp(_scroll, 0, items.Count - visibleRows);
            first = _scroll; last = _scroll + visibleRows;
            var dim = new Color(normalText.r, normalText.g, normalText.b, 0.6f * alpha);
            if (first > 0) DrawCentered(ui, "^ MORE ^", centerX, topY + 14f * s, hintScale * s, dim);
            if (last < items.Count) DrawCentered(ui, "v MORE v", centerX, topY - visibleRows * (h + gap) + 2f * s, hintScale * s, dim);
        }

        MenuItem clicked = null;
        for (int i = first; i < last; i++)
        {
            var item = items[i];
            var r = new Rect(centerX - w * 0.5f, topY - h - (i - first) * (h + gap), w, h);
            bool hover = mouseUsable && item.enabled && r.Contains(mouse);
            if (hover && selected != i) { selected = i; onSelectionChanged?.Invoke(i); }
            bool sel = i == selected && item.enabled;

            Color box = sel ? boxHighlight : boxColor; box.a *= alpha;
            ui.FillRect(r, box);
            Color border = sel ? highlightText : borderColor; border.a *= alpha;
            DrawBorder(ui, r, border, Mathf.Max(1f, 2f * s));

            Color c = !item.enabled ? disabledText : sel ? highlightText : normalText;
            c.a *= alpha;
            float ls = labelScale * s;
            Vector2 size = ui.MeasureText(item.label, ls);
            if (item.IsSetting)
            {
                // label left, "< VALUE >" right
                float top = r.y + (r.height + size.y) * 0.5f;
                ui.DrawText(item.label, new Vector2(r.x + 18f * s, top), ls, c);
                string v = item.valueText != null ? item.valueText() : "";
                string shown = sel ? "< " + v + " >" : v;
                Vector2 vs = ui.MeasureText(shown, ls);
                var vc = new Color(1f, 1f, 1f, c.a);
                ui.DrawText(shown, new Vector2(r.xMax - 18f * s - vs.x, top), ls, sel ? c : vc);
                if (!string.IsNullOrEmpty(item.hint) && sel)
                {
                    float hs = hintScale * s;
                    Vector2 hsize = ui.MeasureText(item.hint, hs);
                    ui.DrawText(item.hint, new Vector2(centerX - hsize.x * 0.5f, r.y - 2f * s), hs, new Color(c.r, c.g, c.b, c.a * 0.7f));
                }
                if (hover && Input.GetMouseButtonDown(0)) { bool right = mouse.x > r.center.x; Adjust(right ? 1 : -1); }
                else if (hover && Input.GetMouseButtonDown(1)) Adjust(-1);
                continue;
            }
            if (!string.IsNullOrEmpty(item.hint))
            {
                float hs = hintScale * s;
                Vector2 hsize = ui.MeasureText(item.hint, hs);
                float total = size.y + hsize.y + 4f * s;
                float top = r.y + (r.height + total) * 0.5f;
                ui.DrawText(item.label, new Vector2(centerX - size.x * 0.5f, top), ls, c);
                var hc = new Color(c.r, c.g, c.b, c.a * 0.7f);
                ui.DrawText(item.hint, new Vector2(centerX - hsize.x * 0.5f, top - size.y - 4f * s), hs, hc);
            }
            else
            {
                float top = r.y + (r.height + size.y) * 0.5f;
                ui.DrawText(item.label, new Vector2(centerX - size.x * 0.5f, top), ls, c);
            }

            if (sel && interactable)
            {
                // small diamonds on both sides mark the highlighted entry
                float m = 7f * s;
                ui.FillRect(new Rect(r.x + 16f * s, r.center.y - m * 0.5f, m, m), c);
                ui.FillRect(new Rect(r.xMax - 16f * s - m, r.center.y - m * 0.5f, m, m), c);
            }

            if (hover && Input.GetMouseButtonDown(0)) clicked = item;
        }
        return clicked;
    }

    public static void DrawBorder(UiBatch ui, Rect r, Color c, float t)
    {
        ui.FillRect(new Rect(r.x, r.y, r.width, t), c);
        ui.FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        ui.FillRect(new Rect(r.x, r.y, t, r.height), c);
        ui.FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>Draws a line of text centred on <paramref name="centerX"/> with its top at <paramref name="topY"/>; returns the text height.</summary>
    public static float DrawCentered(UiBatch ui, string text, float centerX, float topY, float scale, Color color, bool shadow = false)
    {
        Vector2 size = ui.MeasureText(text, scale);
        float x = centerX - size.x * 0.5f;
        if (shadow)
        {
            float o = Mathf.Max(1f, scale * 0.5f);
            ui.DrawText(text, new Vector2(x + o, topY - o), scale, new Color(0f, 0f, 0f, color.a * 0.85f));
        }
        ui.DrawText(text, new Vector2(x, topY), scale, color);
        return size.y;
    }
}
