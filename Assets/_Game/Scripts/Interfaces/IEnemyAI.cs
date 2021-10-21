using UnityEngine;
using System;

public interface IEnemyAI
{
    public void Move(Vector3 targetLocation);
    public void Attack(Vector3 target);
    public void Die();
}