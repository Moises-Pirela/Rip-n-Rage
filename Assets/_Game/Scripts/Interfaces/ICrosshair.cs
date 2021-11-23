using UnityEngine;
using UnityEngine.UI;

public interface ICrosshair
{
    public Image CrosshairImage { get; set; }
    public Animator CrosshairAnimator { get; set; }
    
    public void OnShoot();
    public void OnHit();
    public void OnReset();
}