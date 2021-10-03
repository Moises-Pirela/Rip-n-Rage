using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EntityState : MonoBehaviour, IHealth, IArmor
{
    
    public float Health { get; set; }
    public float Armor { get; set; }
    
    public EntityScriptableData data;
    public EntityScriptableData Data => data;

    public UnityEvent OnHealthChanged;
    public UnityEvent OnArmorChanged;

    public virtual void Awake()
    {
        Init();
    }

    public virtual void Init()
    {
        // Health = SaveData.current != null ? SaveData.current.PlayerStateData.PlayerHealth : Data.MaxHealth;
        // Armor = SaveData.current != null ? SaveData.current.PlayerStateData.PlayerArmor : Data.MaxArmor;
        
        Armor = Data.MaxArmor;
        Health = Data.MaxHealth;
    }

   
    public void TakeDamage(float value)
    {
        Health = Mathf.Clamp(Health-value,0,Data.MaxHealth);
        OnHealthChanged.Invoke();
    }

    public void Heal(float value)
    {
        Health = Mathf.Clamp(Health+value,0,Data.MaxHealth);
        OnHealthChanged.Invoke();
    }
    
    public void TakeArmorDamage(float value)
    {
        Armor = Mathf.Clamp(Armor-value,0,Data.MaxArmor);
        OnArmorChanged.Invoke();
    }

    public void ApplyArmor(float value)
    {
        Armor = Mathf.Clamp(Armor+value,0,Data.MaxArmor);
        OnArmorChanged.Invoke();
    }
}
