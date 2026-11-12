using RipNRage.Engine.UI;
// Ported from Assets/_Game/Scripts/UI/GlockCrosshair.cs.
// Image -> UiImage; DOTween DOSizeDelta -> engine TweenRunner (same durations/easing).
public class GlockCrosshair : MonoBehaviour, ICrosshair
{
    public UiImage CrosshairImage { get; set; } = new UiImage("Crosshair");
    public RipNRage.Engine.Ecs.Animation.AnimatedSprite CrosshairAnimator { get; set; }

    [SerializeField] public UiImage innerCrosshairImage = new UiImage("InnerCrosshair");

    [SerializeField] public Vector2 onHitSize = new Vector2(20, 20);
    [SerializeField] public Vector2 regularSize = new Vector2(10, 10);

    private Coroutine _resetCrosshairStateCoroutine;
    
    protected override void Awake()
    {
        // the crosshair never had a sprite animator attached; the property stays for the ICrosshair surface
        CrosshairAnimator = null;

        CrosshairImage.rectTransform.sizeDelta = regularSize;
        innerCrosshairImage.rectTransform.sizeDelta = new Vector2(5, 5);
    }

    public void OnShoot()
    {
        TweenRunner.CompleteAll(CrosshairImage.rectTransform);
        CrosshairImage.rectTransform.sizeDelta = regularSize;
        TweenRunner.To(() => CrosshairImage.rectTransform.sizeDelta, v => CrosshairImage.rectTransform.sizeDelta = v, onHitSize, 0.25f, CrosshairImage.rectTransform)
            .SetEase(Ease.OutSine).onComplete += OnReset;
    }

    public void OnHit()
    {
        TweenRunner.CompleteAll(innerCrosshairImage.rectTransform);
        innerCrosshairImage.rectTransform.sizeDelta = new Vector2(5,5);
        TweenRunner.To(() => innerCrosshairImage.rectTransform.sizeDelta, v => innerCrosshairImage.rectTransform.sizeDelta = v, new Vector2(10,10), 0.25f, innerCrosshairImage.rectTransform)
            .SetEase(Ease.OutSine).onComplete += OnResetHitmarker;
       
    }

    public void OnReset()
    {
        TweenRunner.To(() => CrosshairImage.rectTransform.sizeDelta, v => CrosshairImage.rectTransform.sizeDelta = v, regularSize, 0.25f, CrosshairImage.rectTransform);
    }

    public void OnResetHitmarker()
    {
        TweenRunner.To(() => innerCrosshairImage.rectTransform.sizeDelta, v => innerCrosshairImage.rectTransform.sizeDelta = v, new Vector2(5,5), 0.25f, innerCrosshairImage.rectTransform);
    }

    /// <summary>Standalone addition: the equipped weapon's current spread (direction offset); drawn as four ticks that open with bloom.</summary>
    public float spread;
    public Color spreadColor = new Color(1f, 1f, 1f, 0.85f);

    private System.Action<UiBatch, Rect, float> _drawTicks;

    /// <summary>Declares the rings and the spread ticks (a custom element at the screen centre) in the HUD's Clay layout.</summary>
    public void Build(ClayUi c, short z)
    {
        CrosshairImage.Build(c, z);
        innerCrosshairImage.Build(c, (short)(z + 1));
        if (spread <= 0f) return;
        c.Leaf(new El
        {
            width = Size.Fixed(0f), height = Size.Fixed(0f),
            floating = new Float { enabled = true, attachTo = RipNRage.Engine.UI.Clay.Clay_FloatingAttachToElement.Root, zIndex = (short)(z + 2), passthrough = true,
                element = RipNRage.Engine.UI.Clay.Clay_FloatingAttachPointType.CenterCenter, parent = RipNRage.Engine.UI.Clay.Clay_FloatingAttachPointType.CenterCenter },
            custom = _drawTicks ??= (ui, r, a) =>
            {
                // 0.05 spread (~2.9°) ≈ 60 reference px from the centre at 77° fov on a 1080p canvas
                float gap = (8f + spread * 1200f) * ui.Scale;
                float len = 9f * ui.Scale, thick = Mathf.Max(1f, 2f * ui.Scale);
                float cx = r.center.x, cy = r.center.y;
                var col = new Color(spreadColor.r, spreadColor.g, spreadColor.b, spreadColor.a * a);
                ui.FillRect(new Rect(cx - gap - len, cy - thick * 0.5f, len, thick), col);
                ui.FillRect(new Rect(cx + gap, cy - thick * 0.5f, len, thick), col);
                ui.FillRect(new Rect(cx - thick * 0.5f, cy - gap - len, thick, len), col);
                ui.FillRect(new Rect(cx - thick * 0.5f, cy + gap, thick, len), col);
            },
        });
    }
}
