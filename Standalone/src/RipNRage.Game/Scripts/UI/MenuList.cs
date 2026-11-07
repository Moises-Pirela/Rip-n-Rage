using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
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
/// Immediate-mode vertical menu laid out with Clay (<see cref="ClayUi"/>) and drawn with the engine's bitmap font.
/// Shared by the death screen, the pause menu and the main menu. Keyboard (arrows / W S, Enter / Space)
/// and mouse (hover selects, left click activates) both work; sizes are in reference pixels of the
/// 1080p canvas and scale with <see cref="UiBatch.Scale"/>. Nothing here touches gameplay state: the
/// caller reacts to the <see cref="MenuItem"/> returned by <see cref="HandleKeyboard"/> / <see cref="Build"/>.
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
    /// Declares the list inside the current Clay layout (a centred column of fixed-width rows) and handles mouse hover
    /// / click. <paramref name="alpha"/> fades the colours (the whole layout is also faded by <see cref="ClayUi.End"/>,
    /// so callers normally pass 1). Returns the item clicked this frame, or null.
    /// </summary>
    public MenuItem Build(ClayUi c, float alpha = 1f)
    {
        if (items.Count == 0 || alpha <= 0.001f) return null;

        Vector2 mouse = Input.mousePosition;
        if ((mouse - _lastMouse).sqrMagnitude > 1f) { _mouseActive = true; _lastMouse = mouse; }
        bool mouseUsable = interactable && _mouseActive && !Input.cursorLocked;

        // scrolling window of rows
        int first = 0, last = items.Count;
        bool scrolling = visibleRows > 0 && items.Count > visibleRows;
        if (scrolling)
        {
            if (selected < _scroll) _scroll = selected;
            if (selected >= _scroll + visibleRows) _scroll = selected - visibleRows + 1;
            _scroll = Mathf.Clamp(_scroll, 0, items.Count - visibleRows);
            first = _scroll; last = _scroll + visibleRows;
        }
        var dim = new TextStyle(hintScale, new Color(normalText.r, normalText.g, normalText.b, 0.6f * alpha));

        MenuItem clicked = null;
        using (c.Box(new El { id = "menu", vertical = true, gap = itemGap, alignX = Clay_LayoutAlignmentX.Center, width = Size.Fit(), height = Size.Fit() }))
        {
            if (scrolling) MoreMarker(c, first > 0 ? "^ MORE ^" : null, dim);
            for (int i = first; i < last; i++)
            {
                var item = items[i];
                // hover from the previous layout (the row's box doesn't move between frames)
                bool hover = mouseUsable && item.enabled && c.PointerOver("menu-row", i);
                if (hover && selected != i) { selected = i; onSelectionChanged?.Invoke(i); }
                bool sel = i == selected && item.enabled;

                Color box = sel ? boxHighlight : boxColor; box.a *= alpha;
                Color border = sel ? highlightText : borderColor; border.a *= alpha;
                Color col = !item.enabled ? disabledText : sel ? highlightText : normalText; col.a *= alpha;
                var label = new TextStyle(labelScale, col, wrap: Clay_TextWrapMode.None);
                var hint = new TextStyle(hintScale, new Color(col.r, col.g, col.b, col.a * 0.7f), wrap: Clay_TextWrapMode.None);

                using (c.Box(new El
                {
                    id = "menu-row", idIndex = i, width = Size.Fixed(itemWidth), height = Size.Fixed(itemHeight),
                    padding = new Pad(16f, 0f), alignY = Clay_LayoutAlignmentY.Center, background = box, borderColor = border, border = 2f,
                }))
                {
                    if (item.IsSetting)
                    {
                        // label left, "< VALUE >" right, hint floating under the selected row
                        using (c.Box(new El { width = Size.Grow(), padding = new Pad(2f, 0f) })) c.Text(item.label, label);
                        string v = item.valueText != null ? item.valueText() : "";
                        c.Text(sel ? "< " + v + " >" : v, sel ? label : new TextStyle(labelScale, new Color(1f, 1f, 1f, col.a), wrap: Clay_TextWrapMode.None));
                        if (sel && !string.IsNullOrEmpty(item.hint))
                            using (c.Box(new El { floating = Float.ToParent(Clay_FloatingAttachPointType.CenterTop, Clay_FloatingAttachPointType.CenterBottom, new Vector2(0f, 2f), 5) }))
                                c.Text(item.hint, hint);
                        if (hover && Input.GetMouseButtonDown(0) && c.TryGetBounds("menu-row", out Rect r, i)) Adjust(mouse.x > r.center.x ? 1 : -1);
                        else if (hover && Input.GetMouseButtonDown(1)) Adjust(-1);
                        continue;
                    }

                    // small diamonds on both sides mark the highlighted entry
                    Color mark = sel && interactable ? col : default;
                    c.Leaf(new El { width = Size.Fixed(7f), height = Size.Fixed(7f), background = mark });
                    using (c.Box(new El { width = Size.Grow(), vertical = true, gap = 4f, alignX = Clay_LayoutAlignmentX.Center }))
                    {
                        c.Text(item.label, label);
                        if (!string.IsNullOrEmpty(item.hint)) c.Text(item.hint, hint);
                    }
                    c.Leaf(new El { width = Size.Fixed(7f), height = Size.Fixed(7f), background = mark });
                }
                if (hover && Input.GetMouseButtonDown(0)) clicked = item;
            }
            if (scrolling) MoreMarker(c, last < items.Count ? "v MORE v" : null, dim);
        }
        return clicked;
    }

    /// <summary>Fixed-height slot above / below a scrolling list (keeps the rows from jumping when it appears).</summary>
    private void MoreMarker(ClayUi c, string text, TextStyle style)
    {
        using (c.Box(new El { height = Size.Fixed(hintScale * 8f), alignX = Clay_LayoutAlignmentX.Center }))
            if (text != null) c.Text(text, style);
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

/// <summary>
/// Small Clay layout helpers shared by the menu screens. Lengths are reference pixels of the 1080-high canvas, so a
/// screen position given as a fraction of the height is simply <c>fraction * 1080</c>.
/// </summary>
public static class ClayScreen
{
    public const float RefHeight = 1080f;

    /// <summary>Full-screen centred column whose content starts <paramref name="topRef"/> below the top edge.</summary>
    public static El Column(float topRef, float gap = 0f) => new El
    {
        id = "screen", width = Size.Grow(), height = Size.Grow(), vertical = true, gap = gap,
        alignX = Clay_LayoutAlignmentX.Center, padding = new Pad(0f, 0f, topRef, 0f),
    };

    /// <summary>A centred column placed absolutely with its top edge <paramref name="topRef"/> below the screen top.</summary>
    public static El FloatingColumn(float topRef, string id = null, float gap = 0f) => new El
    {
        id = id, vertical = true, gap = gap, alignX = Clay_LayoutAlignmentX.Center,
        floating = Float.ToRoot(Clay_FloatingAttachPointType.CenterTop, new Vector2(0f, topRef)),
    };

    public static void Gap(ClayUi c, float px) => c.Leaf(new El { height = Size.Fixed(px) });

    /// <summary>One line of text centred at the bottom; <paramref name="topFromBottomRef"/> is where its top edge sits (old DrawCentered convention).</summary>
    public static void Footer(ClayUi c, string text, float topFromBottomRef, TextStyle style)
    {
        float bottom = topFromBottomRef - style.size * BitmapFont.Cell;
        using (c.Box(new El { floating = Float.ToRoot(Clay_FloatingAttachPointType.CenterBottom, new Vector2(0f, -bottom)) }))
            c.Text(text, style);
    }

    public static TextStyle Line(float size, Color color, bool shadow = false) => new TextStyle(size, color, shadow, wrap: Clay_TextWrapMode.None);
}
