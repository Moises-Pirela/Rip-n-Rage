using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ENRAGED/Weapon", fileName = "Weapon")]
public class WeaponData : ScriptableObject
{
    public AmmoType ammoType;
    public WeaponType weaponType;
    
    [Header("Weapon stats")]
    public WeaponRecoil weaponRecoil;
    public WeaponStats weaponStats;
    
    [Space(10)]
    [Header("Weapon FX")]
    public AnimatorOverrideController animatorOverrideController;
    public AudioClip fireSound;
    public GameObject spawnableVFX;
    public Vector3 weaponPosition;
}
