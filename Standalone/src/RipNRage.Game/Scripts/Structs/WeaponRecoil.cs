[System.Serializable]
public struct WeaponRecoil
{
    public Vector3 recoil;
    public float recoilSpeed;
    public float recoilRecoverySpeed;
    /// <summary>Standalone addition: camera trauma added per shot (0 = none, 0.3 = shotgun thump, 0.5 = rocket).</summary>
    public float cameraShake;

    public WeaponRecoil(Vector3 recoil, float recoilSpeed, float recoilRecoverySpeed)
    {
        this.recoil = recoil;
        this.recoilSpeed = recoilSpeed;
        this.recoilRecoverySpeed = recoilRecoverySpeed;
        cameraShake = 0f;
    }

    public WeaponRecoil(Vector3 recoil, float recoilSpeed, float recoilRecoverySpeed, float cameraShake)
    {
        this.recoil = recoil;
        this.recoilSpeed = recoilSpeed;
        this.recoilRecoverySpeed = recoilRecoverySpeed;
        this.cameraShake = cameraShake;
    }
}
