using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class WorldEntity : MonoBehaviour, IDamageable
{
    public float Health { get; set; }
    public float MaxHealth { get; set; }

    private void Start()
    {
        SetDefaultValues();
    }
    public void SetDefaultValues()
    {
        MaxHealth = 100f;
        Health = MaxHealth;
    }
    public void TakeDamage(float damage)
    {
        Health = Mathf.Clamp(Health - damage, 0, MaxHealth);

        if (Health <= 0)
            Die();
    }
    public void Heal(float amountToHeal)
    {
        Health = Mathf.Clamp(Health + amountToHeal, 0, MaxHealth);
    }

    public void Die()
    {
        Destroy(gameObject);
    }
}
