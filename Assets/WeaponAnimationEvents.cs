using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponAnimationEvents : MonoBehaviour
{
    [SerializeField] private GameObject muzzleFlashObject;
    [SerializeField] private PlayerInventory playerInventory;

   void OnFire()
   {
       muzzleFlashObject.SetActive(true);
   }

   void OnEndFire()
   {
       muzzleFlashObject.SetActive(false);
       playerInventory.CanFire = true;
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
