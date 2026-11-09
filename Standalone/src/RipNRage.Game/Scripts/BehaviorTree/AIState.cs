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
        // the capsule collider is gone (enemies become ECS characters in layer 6): judge by the 2.25 m sprite height
        float bottom = transform.position.y - 0.5f * 2.25f;
        return point.y > bottom + 2.25f * 0.76f;
    }

    public override void Die()
    {
        HealthState = EntityHealthState.DEAD;
        // ECS migration (layer 2): legacy steering is gone; reference for the ECS port (layer 6).
#if LEGACY_PHYSICS
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        var motor = GetComponent<CharacterBody>();
        if (motor != null) motor.enabled = false;
        var body = GetComponent<RigidBody>();
        if (body != null) body.enabled = false; // the ragdoll pieces are the corpse
#endif
        GameVfx.Death(data != null ? data.name : null, transform.position + Vector3.up * 0.4f);
        OnDieCallback?.Invoke();
    }
}
