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

    public void Draw(UiBatch ui)
    {
        CrosshairImage.Draw(ui);
        innerCrosshairImage.Draw(ui);
    }
}
