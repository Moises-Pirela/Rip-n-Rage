using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
   [SerializeField] private Transform playerCam;
   [SerializeField] private Weapon[] allWeapons;
   [SerializeField] private Animator animator;
   [HideInInspector] public Weapon equippedWeapon;

   public bool CanFire {get;set;}

   private void Start()
   {
       EquipWeapon(allWeapons[0]);
   }

   private void EquipWeapon(Weapon weapon)
   {
       equippedWeapon = weapon;

       animator.runtimeAnimatorController = weapon.animator;

       equippedWeapon.Equip(playerCam);

       CanFire = true;
   }

   public void Fire()
   {
       if (!CanFire) return;

       equippedWeapon.Fire();

       animator.SetTrigger("Fire");

       CanFire = false;
   }

   public void AltFire()
   {
       if (!CanFire) return;

       equippedWeapon.AltFire();

       animator.SetTrigger("AltFire");

       CanFire = false;
   }
}
