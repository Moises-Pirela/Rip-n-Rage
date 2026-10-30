// Ported from Assets/_Game/Scripts/UI/GlockCrosshair.cs.
// Image -> UiImage; DOTween DOSizeDelta -> engine TweenRunner (same durations/easing).
public class GlockCrosshair : MonoBehaviour, ICrosshair
{
    public UiImage CrosshairImage { get; set; } = new UiImage("Crosshair");
    public Animator CrosshairAnimator { get; set; }

    [SerializeField] public UiImage innerCrosshairImage = new UiImage("InnerCrosshair");

    [SerializeField] public Vector2 onHitSize = new Vector2(20, 20);
    [SerializeField] public Vector2 regularSize = new Vector2(10, 10);

    private Coroutine _resetCrosshairStateCoroutine;
    
    protected override void Awake()
    {
        CrosshairAnimator = GetComponent<Animator>();

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

    public void Draw(UiBatch ui)
    {
        CrosshairImage.Draw(ui);
        innerCrosshairImage.Draw(ui);
        if (spread > 0f)
        {
            // 0.05 spread (~2.9°) ≈ 60 reference px from the centre at 77° fov on a 1080p canvas
            float gap = (8f + spread * 1200f) * ui.Scale;
            float len = 9f * ui.Scale, thick = Mathf.Max(1f, 2f * ui.Scale);
            float cx = ui.Width * 0.5f, cy = ui.Height * 0.5f;
            ui.FillRect(new Rect(cx - gap - len, cy - thick * 0.5f, len, thick), spreadColor);
            ui.FillRect(new Rect(cx + gap, cy - thick * 0.5f, len, thick), spreadColor);
            ui.FillRect(new Rect(cx - thick * 0.5f, cy - gap - len, thick, len), spreadColor);
            ui.FillRect(new Rect(cx - thick * 0.5f, cy + gap, thick, len), spreadColor);
        }
    }
}
