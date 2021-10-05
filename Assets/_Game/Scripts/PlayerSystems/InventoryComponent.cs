using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryComponent
{
    public int currentWeaponIndex;
    public AWeapon currentWeapon;
    public AWeapon[] weapons;
    public Ammo currentAmmo;
    public GameObject Owner;

    public List<Ammo> InventoryAmmo = new List<Ammo>()
    {
        new Ammo(AmmoType.LIGHT, 30, 60),
        new Ammo(AmmoType.HEAVY, 30, 60),
        new Ammo(AmmoType.SHELL, 30, 60),
        new Ammo(AmmoType.EXPLOSIVE, 30, 60),
        new Ammo(AmmoType.ENERGY, 30, 60),
        new Ammo(AmmoType.NONE, 0, 0)
    };

    [HideInInspector] public List<AWeapon> availableWeapons;

    public int SwapWeapon(int index)
    {
        index = currentWeaponIndex += index;
        
        if (index >= weapons.Length) index = 0;

        if (index < 0) index = weapons.Length - 1;

        return index;
    }

    public void EquipWeapon(int index)
    {
        currentWeaponIndex = index;
        
        currentWeapon = availableWeapons[index];

        currentAmmo = InventoryAmmo.Find(type => type.ammoType == currentWeapon.weaponData.AmmoType);
    }
    public void SetupInventory(GameObject Owner)
    {
        if (SaveData.current.PlayerStateData.Ammo != null)
            InventoryAmmo = SaveData.current.PlayerStateData.Ammo;

        this.Owner = Owner;
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
