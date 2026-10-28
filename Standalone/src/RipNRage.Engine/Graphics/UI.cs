namespace RipNRage.Engine.Graphics
{
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    /// <summary>
    /// Anchored rectangle in a reference-resolution canvas (UnityEngine.RectTransform subset).
    /// <c>anchor</c> is the normalised screen point (0,0 = bottom-left) the rect is placed relative to;
    /// <c>anchoredPosition</c> is the pivot's offset from that anchor in reference pixels.
    /// </summary>
    public sealed class RectTransform
    {
        public Vector2 anchor = new Vector2(0.5f, 0.5f);
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta = new Vector2(100, 100);
        public Vector2 pivot = new Vector2(0.5f, 0.5f);

        /// <summary>Resolves to a pixel rect for the given screen size and canvas scale.</summary>
        public Rect Resolve(int screenW, int screenH, float scale)
        {
            float ax = anchor.x * screenW, ay = anchor.y * screenH;
            float w = sizeDelta.x * scale, h = sizeDelta.y * scale;
            float x = ax + anchoredPosition.x * scale - pivot.x * w;
            float y = ay + anchoredPosition.y * scale - pivot.y * h;
            return new Rect(x, y, w, h);
        }
    }

    public enum FillMethod { Horizontal, Vertical, Radial360 }

    /// <summary>UnityEngine.UI.Image stand-in drawn by the HUD.</summary>
    public sealed class UiImage
    {
        public RectTransform rectTransform = new RectTransform();
        public Sprite sprite;
        public Color color = Color.white;
        public float fillAmount = 1f;
        public bool filled;
        public FillMethod fillMethod = FillMethod.Horizontal;
        public bool fillClockwise = true;
        public bool enabled = true;
        public string name;

        public UiImage(string name = "Image") { this.name = name; }

        public void Draw(UiBatch ui)
        {
            if (!enabled || sprite == null) return;
            Rect r = rectTransform.Resolve(ui.Width, ui.Height, ui.Scale);
            if (!filled) ui.Draw(sprite, r, color);
            else if (fillMethod == FillMethod.Radial360) ui.DrawRadialFilled(sprite, r, fillAmount, fillClockwise, color);
            else ui.DrawFilled(sprite, r, fillAmount, color, fillMethod == FillMethod.Vertical);
        }
    }

    /// <summary>TextMeshProUGUI stand-in rendered with the built-in bitmap font.</summary>
    public sealed class UiText
    {
        public RectTransform rectTransform = new RectTransform();
        public string text = "";
        public float fontSize = 32f;
        public Color color = Color.white;
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public bool enabled = true;

        public void Draw(UiBatch ui)
        {
            if (!enabled || string.IsNullOrEmpty(text)) return;
            Rect r = rectTransform.Resolve(ui.Width, ui.Height, ui.Scale);
            float glyphScale = fontSize * ui.Scale / BitmapFont.GlyphH;
            Vector2 size = ui.MeasureText(text, glyphScale);
            float x = r.x, y = r.y + r.height;
            int a = (int)alignment;
            int col = a % 3, row = a / 3;
            if (col == 1) x = r.x + (r.width - size.x) * 0.5f; else if (col == 2) x = r.x + r.width - size.x;
            if (row == 1) y = r.y + (r.height + size.y) * 0.5f; else if (row == 2) y = r.y + size.y;
            ui.DrawText(text, new Vector2(x, y), glyphScale, color);
        }
    }
}
