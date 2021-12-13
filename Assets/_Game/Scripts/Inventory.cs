using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Inventory
{
    public List<GameObject> allWeaponPrefabs = new List<GameObject>();
    [HideInInspector] public List<Weapon> availableWeapons = new List<Weapon>();

    public Weapon equippedWeapon;
    public int[] ammoLimits = new int[5];
    [HideInInspector] public int[] ammo = new int[5];
    [HideInInspector] public int currentWeaponIndex;

    private InventoryData _inventoryData;

    public void Init()
    {
        _inventoryData = SaveData.Current.inventoryData;

        currentWeaponIndex = _inventoryData.currentWeaponIndex;
        ammo = _inventoryData.ammo;
    }

    public bool HasAmmoOfType(AmmoType ammoType)
    {
        int index = (int) ammoType;
        
        return ammo[index] > 0;
    }

    public bool AddToAmmo(AmmoType ammoType, int amountToAdd)
    {
        int index = (int) ammoType;

        if (ammo[index] >= ammoLimits[index]) return false;
        
        ammo[index] = Mathf.Clamp(ammo[index] + amountToAdd, 0, ammoLimits[index]);
        
        return true;

    }

    public void UseAmmo(int amountToUse)
    {
        int index = (int)equippedWeapon.weaponData.ammoType;
        int currentAmmoCount = ammo[index];
        currentAmmoCount = Mathf.Clamp(currentAmmoCount - amountToUse, 0, ammoLimits[index]);
        ammo[index] = currentAmmoCount;
    }

    public int SwapWeapon(int value)
    {
        currentWeaponIndex += value;

        if (currentWeaponIndex < 0)
            currentWeaponIndex = availableWeapons.Count - 1;
        else if (currentWeaponIndex > availableWeapons.Count - 1)
            currentWeaponIndex = 0;
        
        equippedWeapon = availableWeapons[currentWeaponIndex];

        return currentWeaponIndex;
    }
}