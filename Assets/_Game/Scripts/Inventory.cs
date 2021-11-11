using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Inventory
{
    public List<Weapon> allWeapons = new List<Weapon>();
    private List<Weapon> availableWeapons = new List<Weapon>();

    public Weapon equippedWeapon;
    private int _currentWeaponIndex;

    private InventoryData m_InventoryData;

    public void Init()
    {
        m_InventoryData = SaveData.current.InventoryData;
    }

    public void EquipWeapon(Weapon weapon)
    {
        if (equippedWeapon == null  || equippedWeapon == weapon) return;
        
        equippedWeapon = weapon;
    }

    public void SwapWeapon(int value)
    {
        _currentWeaponIndex += value;

        if (_currentWeaponIndex < 0)
            _currentWeaponIndex = availableWeapons.Count - 1;
        else if (_currentWeaponIndex > availableWeapons.Count - 1)
            _currentWeaponIndex = 0;
        
        EquipWeapon(availableWeapons[_currentWeaponIndex]);
    }
}