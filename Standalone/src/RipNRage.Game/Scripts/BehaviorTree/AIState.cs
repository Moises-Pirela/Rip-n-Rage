using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIState.cs.
public class AIState : EntityState
{
    [SerializeField] public EnemyData data;
    public Action OnDieCallback;

    public Transform grapplePoint;

    protected override void Awake()
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
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        OnDieCallback?.Invoke();
    }
}
