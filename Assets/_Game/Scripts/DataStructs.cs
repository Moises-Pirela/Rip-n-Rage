using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public struct HealthComponent
{
    public EntityHealthState HealthState;
    public float MaxHealth;
    public float Health
    {
        get { return _health;}
        set
        {
            _health = value;
            
            Mathf.Clamp(_health, 0, MaxHealth);
            OnHealthChanged.Invoke();
            
            if (_health <= 0)
                HealthState = EntityHealthState.DEAD;
        }
    }
    private float _health;
    
    public UnityEvent OnHealthChanged;

    public void InitHealth()
    {
        Health = MaxHealth;
    }
}
