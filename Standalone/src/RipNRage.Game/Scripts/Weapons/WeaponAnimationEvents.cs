using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponAnimationEvents.cs.
// The methods are invoked by name from animation-clip events — the weapon sprite entity's AnimationEvents arrive
// through AnimatedSprite.OnEvent as a SendMessage on this GameObject (wired in PlayerPrefab), exactly like Unity's
// AnimationEvent dispatch did.
public class WeaponAnimationEvents : MonoBehaviour
{
    /// <summary>The muzzle flash sprite entity (a child of the weapon sprite).</summary>
    public RipNRage.Engine.Ecs.Animation.AnimatedSprite muzzleFlash;

   void OnFire()
   {
       //muzzleFlash.SetActive(true);
   }

   void OnEndFire()
   {
       //muzzleFlash.SetActive(false);
   }

   void OnGlockAltFire()
   {
       muzzleFlash.SetActive(true);
   }

   void OnGlockBeginAltFire()
   {
       muzzleFlash.SetActive(false);
   }
}
