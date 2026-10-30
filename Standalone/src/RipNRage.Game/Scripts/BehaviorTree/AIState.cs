using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIState.cs (standalone addition: death blood effect).
using RipNRage.Game.Content.Vfx;

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

    /// <summary>Standalone addition: true when a hit point lies in the top part of the capsule (head).</summary>
    public bool IsHeadshot(Vector3 point)
    {
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) return false;
        var b = capsule.bounds;
        return point.y > b.min.y + (b.max.y - b.min.y) * 0.76f;
    }

    public override void Die()
    {
        HealthState = EntityHealthState.DEAD;
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        GameVfx.Death(data != null ? data.name : null, transform.position + Vector3.up * 0.4f);
        OnDieCallback?.Invoke();
    }
}
