
[System.Serializable]
public struct InventoryData
{
    public int[] ammo;
    public int currentWeaponIndex;
    
    public InventoryData(int[] ammo, int weaponIndex)
    {
        this.ammo = ammo;
        currentWeaponIndex = weaponIndex;
    }
}