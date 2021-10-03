using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IArmor
{
    public float Armor { get; set; }

    public void TakeArmorDamage(float value);

    public void ApplyArmor(float value);
}
