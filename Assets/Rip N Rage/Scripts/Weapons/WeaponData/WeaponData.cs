using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponData" , menuName = "WeaponData")]
public class WeaponData : ScriptableObject
{
    public float damagePerBullet;
    public float range;
    public string weaponName;

    [Header("Recoil")]
    public Vector3 recoilPattern = new Vector3(1f,-0.5f, 0);
    public float recoilDuration = 0.2f;
    public int recoilStrength = 10;
}
