using UnityEngine;


[System.Serializable]
public struct WeaponData
{
    public int bulletsPerShot;
    public float fireRate;
    public bool isUnlocked;
    public AnimatorOverrideController animatorOverrideController;

    public WeaponData(int bulletsPerShot, float fireRate, bool isUnlocked)
    {
        this.bulletsPerShot = 0;
        this.fireRate = 0;
        this.isUnlocked = false;
        animatorOverrideController = null;
    }
}