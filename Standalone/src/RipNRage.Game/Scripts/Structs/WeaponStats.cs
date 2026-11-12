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
    /// <summary>Standalone addition: impulse (N·s) a hit gives the thing it kills (throws ragdoll parts); 0 = the weapon class default.</summary>
    public float hitImpulse;

}
