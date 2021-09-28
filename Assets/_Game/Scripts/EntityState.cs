using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityState : MonoBehaviour
{
    [Header("Status")]
    public HealthComponent healthComponent;

    public virtual void Awake()
    {
        Init();
    }

    public virtual void Init()
    {
        healthComponent.InitHealth();
    }
}
