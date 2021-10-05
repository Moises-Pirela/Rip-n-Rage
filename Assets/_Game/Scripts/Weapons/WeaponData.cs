using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum  FireType
{
    MELEE,
    SINGLE,
    BURST,
    AUTO
}

public enum AmmoType
{
    NONE,
    LIGHT,
    ENERGY,
    HEAVY,
    EXPLOSIVE, 
    SHELL
}

[CreateAssetMenu(fileName = "WeaponData", menuName = "Rip-n-Rage/WeaponData", order = 0)]
public class WeaponData : ScriptableObject 
{
    public string WeaponName;
    public FireType FireType;
    public AmmoType AmmoType;
    public int roundsPerFire;
    public float fireRate;
    public float altRefireRate;
    public AnimatorOverrideController AnimatorOverrideController;
    public AudioClip fireSound;
    public AudioClip projectileImpactSound;
    public GameObject bulletProjectile;
    public GameObject bulletImpact;
    public GameObject muzzleFlash;

    [Space(20)] [Header("Recoil settings")]
    public Vector3 recoilRotation;
    public float recoilSpeed;
    public float recoilRecoverySpeed;


}
