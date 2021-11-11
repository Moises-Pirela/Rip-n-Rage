using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class WeaponAnimationEvents : MonoBehaviour
{
    [SerializeField] private GameObject muzzleFlashObject;
    [SerializeField] private AudioSource weaponAudio;

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
