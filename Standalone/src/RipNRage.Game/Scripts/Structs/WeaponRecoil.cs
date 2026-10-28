[System.Serializable]
public struct WeaponRecoil
{
    public Vector3 recoil;
    public float recoilSpeed;
    public float recoilRecoverySpeed;

    public WeaponRecoil(Vector3 recoil, float recoilSpeed, float recoilRecoverySpeed)
    {
        this.recoil = recoil;
        this.recoilSpeed = recoilSpeed;
        this.recoilRecoverySpeed = recoilRecoverySpeed;
    }
}