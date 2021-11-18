using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PlayerStateData
{
    public Vector3 playerPosition;
    public Quaternion playerRotation;

    public float PlayerHealth;
    public float PlayerArmor;

    public PlayerStateData(Vector3 pos, Quaternion rot, float health, float armor)
    {
        playerPosition = pos;
        playerRotation = rot;
        PlayerHealth = health;
        PlayerArmor = armor;
    }
}