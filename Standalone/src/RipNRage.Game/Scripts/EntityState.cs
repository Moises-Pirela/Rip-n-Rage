using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/EntityState.cs — logic unchanged, only the base class is the
// engine's MonoBehaviour replacement.
public abstract class EntityState : MonoBehaviour, IHealth, IArmor
{
    public EntityHealthState HealthState;

    public float Health { get; set; }
    public float Armor { get; set; }

    private float MaxHealth;
    private float MaxArmor;

    public Action OnHealthChanged;
    public Action OnArmorChanged;

    public abstract void Init();
    public abstract void Die();

    public virtual void TakeDamage(float damage, float armorDamageResistance = 0.75f)
    {
        var totalDamage = damage;
        
        if (Armor > 0)
        {
            var armorAbsorption = damage * armorDamageResistance;

            if (armorAbsorption > Armor)
            {
                var armorDelta = armorAbsorption - Armor;
                armorAbsorption -= armorDelta;
            }

            totalDamage -= armorAbsorption;
            TakeArmorDamage(armorAbsorption);
        }
        
        TakeHealthDamage(totalDamage);
    }
    public void TakeHealthDamage(float value)
    {
        Health = Mathf.Clamp(Health-value,0,MaxHealth);
        OnHealthChanged?.Invoke();

        if (Health <= 0)
        {
            Die();
        }
    }

    public void Heal(float value)
    {
        Health = Mathf.Clamp(Health+value,0,MaxHealth);
        OnHealthChanged?.Invoke();
    }
    public void TakeArmorDamage(float value)
    {
        Armor = Mathf.Clamp(Armor-value,0,MaxArmor);

        if (OnArmorChanged != null)
            OnArmorChanged();
    }

    public void ApplyArmor(float value)
    {
        Armor = Mathf.Clamp(Armor+value,0,MaxArmor);
        
        if (OnArmorChanged != null)
            OnArmorChanged();
    }

    public void InitializeHealth(float maxHealth)
    {
        MaxHealth = maxHealth;
        Health = maxHealth;
    }

    public void InitializeArmor(float maxArmor)
    {
        MaxArmor = maxArmor;
        Armor = maxArmor;
    }
}
