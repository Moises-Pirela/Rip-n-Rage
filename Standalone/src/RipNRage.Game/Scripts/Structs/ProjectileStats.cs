[System.Serializable]
public struct ProjectileStats
{
    public float speed;
    public float damage;
    public float range;
    public Prefab hitVFX;
    /// <summary>Standalone addition: key into vfx/bindings.json "trails" for a following effect (e.g. "rocket"); empty = none.</summary>
    public string trailEffect;
    /// <summary>Standalone addition: key into vfx/bindings.json "explosions" used on impact (rockets); empty = surface impact.</summary>
    public string explosionEffect;
    /// <summary>Standalone addition: key into vfx/bindings.json "bullets" (calibre) for impact / blood scale; default "enemy".</summary>
    public string bulletKey;
}
