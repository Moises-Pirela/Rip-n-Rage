using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class WeaponAnimationEvents : MonoBehaviour
{
    [SerializeField] private FPSCombat combat;
    [SerializeField] private GameObject muzzleFlashObject;

   void OnFire()
   {
       muzzleFlashObject.SetActive(true);
   }

   void OnEndFire()
   {
       muzzleFlashObject.SetActive(false);
       combat.CanFire = true;
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
