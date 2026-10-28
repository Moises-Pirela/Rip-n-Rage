using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponAnimationEvents.cs.
// The methods are invoked by name from animation-clip events (engine Animator -> SendMessage),
// exactly like Unity's AnimationEvent dispatch.
public class WeaponAnimationEvents : MonoBehaviour
{
    [SerializeField] public GameObject muzzleFlashObject;
    [SerializeField] public AudioSource weaponAudio;

   void OnFire()
   {
       //muzzleFlashObject.SetActive(true);
   }

   void OnEndFire()
   {
       //muzzleFlashObject.SetActive(false);
   }

   void OnGlockAltFire()
   {
       muzzleFlashObject.SetActive(true);
   }

   void OnGlockBeginAltFire()
   {
       muzzleFlashObject.SetActive(false);
   }
}
