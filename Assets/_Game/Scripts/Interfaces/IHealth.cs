using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IHealth
{
    public float Health { get; set; }

    public void TakeDamage(float value);

    public void Heal(float value);

    public EntityScriptableData Data { get; }
}
