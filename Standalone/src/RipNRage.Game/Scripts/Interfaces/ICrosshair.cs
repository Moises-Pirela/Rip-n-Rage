// UnityEngine.UI.Image -> UiImage (engine HUD element). Animator stays the engine Animator.
public interface ICrosshair
{
    public UiImage CrosshairImage { get; set; }
    public Animator CrosshairAnimator { get; set; }
    
    public void OnShoot();
    public void OnHit();
    public void OnReset();
}
