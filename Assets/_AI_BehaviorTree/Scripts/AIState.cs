using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AIState : EntityState
{
    [SerializeField] protected EnemyData data;
    public Action OnDieCallback;

    private void Awake()
    {
        Init();
    }

    public override void Init()
    {
        InitializeHealth(data.maxHealth);
        InitializeArmor(data.maxHealth);
    }

    public override void Die()
    {
        HealthState = EntityHealthState.DEAD;
        GetComponent<CapsuleCollider>().enabled = false;
        GetComponent<NavMeshAgent>().enabled = false;
        OnDieCallback();
    }
}
