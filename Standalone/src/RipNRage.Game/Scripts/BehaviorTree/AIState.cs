using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIState.cs (standalone addition: death blood effect).
// MIGRATION (layer 5): the enemy's hit points live in a Health component on an ECS entity this class creates, with
// the legacy capsule restored as its collider — so rays and projectiles hit enemies again (hit resolution had been
// stubbed since the legacy physics was deleted in 2.L). The entity is a static collider for now: the force-driven
// character body and the steering return in layer 6, replacing the pose sync below.
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content.Vfx;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

public class AIState : EntityState
{
    [SerializeField] public EnemyData data;
    public Action OnDieCallback;

    public Transform grapplePoint;

    /// <summary>The legacy capsule, from the pre-2.L prefab: radius 0.15 * 2.25, height 2.25 minus the hover gap,
    /// centred half the ride height below the body-centre transform.</summary>
    public const float SpriteHeight = 2.25f, CapsuleRadius = 0.15f * SpriteHeight;
    /// <summary>Hits above this fraction of the sprite height are headshots (the old IsHeadshot).</summary>
    public const float HeadFraction = 0.76f;

    private Vector3 _lastPosition;

    protected override void Awake()
    {
        float rideHeight = CharacterProfiles.Enemy.rideHeight;
        var w = Application.Current.World;
        // immediate creation: Awake runs in the scene phase, and Init on the next line needs the Health component live
        var e = w.CreateEntity();
        w.Add(e, new LocalTransform { Position = transform.position, Rotation = Quaternion.identity, Scale = Vector3.one });
        w.Add(e, new ColliderShape
        {
            Kind = ShapeKind.Capsule, Radius = CapsuleRadius, Height = SpriteHeight - rideHeight,
            Center = new Vector3(0f, -rideHeight * 0.5f, 0f),
        });
        w.Add(e, new CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
        w.Add(e, new Health()); // maxima set by Init below, as the legacy flow did
        w.Add(e, new Hitbox { Height = SpriteHeight, HeadFraction = HeadFraction });
        Bind(e);
        _lastPosition = transform.position;
        Init();
    }

    public override void Init()
    {
        InitializeHealth(data.maxHealth);
        // Armour as the data writes it. The legacy Init passed maxHealth here, which gave the rifleman 1000 phantom
        // armour on top of its 1000 hit points (RiflemanData says maxArmor = 0) and doubled what it takes to kill —
        // a typo, kept faithfully through 5.1 and corrected now that armour is its own component: no armour, none.
        InitializeArmor(data.maxArmor);
    }

    /// <summary>Standalone addition: true when a hit point lies in the top part of the capsule (head).</summary>
    public bool IsHeadshot(Vector3 point)
    {
        float bottom = transform.position.y - 0.5f * SpriteHeight;
        return point.y > bottom + SpriteHeight * HeadFraction;
    }

    public override void Die()
    {
        HealthState = EntityHealthState.DEAD;
        GameVfx.Death(data != null ? data.name : null, transform.position + Vector3.up * 0.4f);
        OnDieCallback?.Invoke();
    }

    protected override void LateUpdate()
    {
        // keep the hitbox under the body when something moves it (spawners, scripts; the AI itself is layer 6).
        // Only on an actual move: writing a static collider's LocalTransform re-bakes it.
        if (transform.position == _lastPosition) return;
        _lastPosition = transform.position;
        var w = Application.Current.World;
        if (w.IsAlive(entity) && w.Has<LocalTransform>(entity))
            w.Get<LocalTransform>(entity).Position = _lastPosition;
    }

    protected override void OnDestroy()
    {
        var w = Application.Current?.World;
        if (w != null && w.IsAlive(entity) && !w.IsIterating) w.DestroyEntity(entity);
        base.OnDestroy(); // unregisters the EcsObjects mapping
    }
}
