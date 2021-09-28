using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryComponent
{
    public AWeapon currentWeapon;
    public AWeapon[] weapons;
    public Ammo currentAmmo;
    public Ammo[] ammo;

    [HideInInspector] public List<AWeapon> availableWeapons;


    public void EquipWeapon(int index)
    {
        currentWeapon = availableWeapons[index];
        
        foreach (var ammo1 in ammo)
        {
            if (ammo1.ammoType != currentWeapon.weaponData.AmmoType) continue;

            currentAmmo = ammo1;
        }
    }

    public void SetupInventory()
    {
        foreach (var ammo1 in ammo)
        {
            ammo1.Amount = ammo1.MaxAmmo;
        }
    }

    public int GetCurrentWeaponAmmo()
    {
        return currentAmmo.Amount;
    }

    public void UseAmmo(int ammoToUse)
    {
        currentAmmo.Amount -= ammoToUse;
    }

    public void AddToAmmo(int ammoToAdd)
    {
        currentAmmo.Amount += ammoToAdd;
    }

    private void IterateAmmo(System.Action<Ammo> action)
    {
        for (int i = 0; i < ammo.Length; i++)
        {
            if (ammo[i].ammoType != currentWeapon.weaponData.AmmoType) continue;

            action(ammo[i]);
        }
    }
}

[System.Serializable]
public class Ammo
{
    public AmmoType ammoType;
    public int MaxAmmo;
    private int amount;
    public int Amount
    {
        get => amount;
        set => amount = Mathf.Clamp(value, 0, MaxAmmo);
    }

    public Ammo(AmmoType ammoType, int startingAmount, int maxAmmo)
    {
        this.ammoType = ammoType;
        amount = startingAmount;
        MaxAmmo = maxAmmo;
    }
}
