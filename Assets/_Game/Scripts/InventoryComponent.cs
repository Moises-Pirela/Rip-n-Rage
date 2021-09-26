using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryComponent
{
    public AWeapon weapon;
    public AWeapon[] weapons;
    public Ammo[] ammo;
}

[System.Serializable]
public struct Ammo
{
    public AmmoType ammoType;
    public int amount;

    public Ammo(AmmoType ammoType, int startingAmount)
    {
        this.ammoType = ammoType;
        amount = startingAmount;
    }
}
