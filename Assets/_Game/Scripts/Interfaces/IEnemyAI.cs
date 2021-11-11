using UnityEngine;
using System;
using System.Collections;

public interface IEnemyAI
{
    public void Move(Vector3 targetLocation);
    public void Attack(Vector3 target);
    public void SpawnProjectile(Vector3 target);

    public IEnumerator AllowRefireCoroutine();
    public void Die();
    public bool CanFire { get; set; }
}