[System.Serializable]
public struct WeaponStats
{
    public int bulletsPerShot;
    public float fireRate;
    public float damagePerShot;
    /// <summary>Base spread (direction offset per unit forward; 0.05 ≈ 2.9°).</summary>
    public float weaponSpread;
    public bool isUnlocked;

    // Standalone additions — bloom: spread grows with every shot and recovers over time; movement widens it.
    /// <summary>Upper limit of weaponSpread + bloom.</summary>
    public float spreadMax;
    /// <summary>Bloom added per shot.</summary>
    public float bloomPerShot;
    /// <summary>Bloom lost per second while not firing.</summary>
    public float bloomRecovery;
    /// <summary>Extra spread at full run speed.</summary>
    public float moveSpread;
    /// <summary>Extra spread while airborne.</summary>
    public float airSpread;
    /// <summary>Multiplier for the spread applied to projectiles (rockets are steadier than bullets; 0 = no projectile spread).</summary>
    public float projectileSpreadScale;

    public WeaponStats(int bulletsPerShot, float fireRate, bool isUnlocked)
    {
        this.bulletsPerShot = 0;
        this.fireRate = 0;
        this.isUnlocked = false;
        damagePerShot = 0;
        weaponSpread = 0;
        spreadMax = 0; bloomPerShot = 0; bloomRecovery = 0; moveSpread = 0; airSpread = 0; projectileSpreadScale = 0;
    }
}
