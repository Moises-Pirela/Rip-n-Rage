[System.Serializable]
public struct WeaponStats
{
    public int bulletsPerShot;
    public float fireRate;
    public float damagePerShot;
    public float weaponSpread;
    public bool isUnlocked;

    public WeaponStats(int bulletsPerShot, float fireRate, bool isUnlocked)
    {
        this.bulletsPerShot = 0;
        this.fireRate = 0;
        this.isUnlocked = false;
        damagePerShot = 0;
        weaponSpread = 0;
    }
}