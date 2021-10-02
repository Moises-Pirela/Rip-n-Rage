using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PlayerStateData
{
    public Vector3 playerPosition;
    public Quaternion playerRotation;

    public float PlayerHealth;

    public List<Ammo> Ammo;
}

[Serializable]
public struct AmmoData
{
    public AmmoType AmmoType;
    public float AmmoCount;
}