using System.Collections;
using System.Collections.Generic;

public interface IHealth
{
    public float Health { get; set; }

    public void TakeHealthDamage(float value);

    public void Heal(float value);

    public void InitializeHealth(float maxHealth);
}
