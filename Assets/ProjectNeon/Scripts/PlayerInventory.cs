using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [SerializeField] private Transform playerCam;
   [SerializeField] private Weapon[] allWeapons;
   [HideInInspector] public Weapon equippedWeapon;

   private void Start()
   {
       equippedWeapon = allWeapons[0];

       equippedWeapon.Equip(playerCam);
   }

   public void Fire()
   {
       equippedWeapon.Fire();
   }

   public void AltFire()
   {
       equippedWeapon.AltFire();
   }
}
