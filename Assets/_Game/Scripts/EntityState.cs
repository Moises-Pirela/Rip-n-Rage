using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityState : MonoBehaviour
{
    [Header("Status")]
    public HealthComponent healthComponent;

    public virtual void Start()
    {
        Init();
    }

    public virtual void Init()
    {
        if (SaveData.current != null)
        {
            healthComponent.Health = SaveData.current.PlayerStateData.PlayerHealth;
        }
        else
        {
            healthComponent.InitHealth();   
        }        
    }
}
