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
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content.Vfx;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.AI;
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

    protected override void Awake()
    {
        float rideHeight = CharacterProfiles.Enemy.rideHeight;
        var w = Application.Current.World;
        // immediate creation: Awake runs in the scene phase, and Init on the next line needs the Health component live
        var e = w.CreateEntity();
        w.Add(e, new LocalTransform { Position = transform.position, Rotation = transform.rotation, Scale = Vector3.one });
        w.Add(e, new ColliderShape
        {
            Kind = ShapeKind.Capsule, Radius = CapsuleRadius, Height = SpriteHeight - rideHeight,
            Center = new Vector3(0f, -rideHeight * 0.5f, 0f),
        });
        w.Add(e, new CollisionFilter { Layer = Layers.Enemies, Mask = ~0, UseLayerMatrix = true });
        // the body: the force-driven capsule on its spring (6.1; it stood as a static hitbox through layer 5)
        w.Add(e, new BodyConfig { mass = CharacterProfiles.Enemy.mass, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
        w.Add(e, new BodyVelocity());
        w.Add(e, new CharacterMotorConfig { profile = CharacterProfileKind.Enemy, rideHeight = rideHeight }.Bake());
        w.Add(e, new Health()); // maxima set by Init below, as the legacy flow did
        w.Add(e, new Hitbox { Height = SpriteHeight, HeadFraction = HeadFraction });

        // the look: an 8-sided sheet on a sprite child (feet on the ground: the old SpriteHolder offset and scale)
        var sprite = w.CreateEntity();
        w.Add(sprite, new LocalTransform
        {
            Position = new Vector3(0f, -rideHeight, 0f), Rotation = Quaternion.identity,
            Scale = Vector3.one * (SpriteHeight / 5.12f), // 512 px tiles @100 ppu, ~2.25 units tall
        });
        w.Add(sprite, new Parent { Value = e });
        w.Add(sprite, new SpriteRender { Color = Color.white, Billboard = true, Lit = true, Layer = RenderLayer.World, CastsShadow = true });
        w.Add(sprite, new SpriteFx());
        w.Add(e, new EnemyVisual { Sprite = sprite, DeadTime = -1f });

        // the mind (6.2) and the senses (6.3): sight and hearing write Perception, the state tree reads it and writes
        // the intent, AIAgent acts on the intent
        w.Add(e, new Enemy { Kind = Enemies.KindOf(data) });
        w.Add(e, new Heading { Yaw = transform.rotation.eulerAngles.y }); // the body never turns (6.4): facing is data
        w.Add(e, new Perception());
        w.Add(e, new AiBrain { Rng = (uint)(e.Index + 1) * 2654435761u }); // seeded by the entity: the same run every time
        w.Add(e, new AiIntent());

        Bind(e);
        Init();
    }

    /// <summary>
    /// The entity is the enemy now; this GameObject follows it so the unported scripts (the tree, the save system,
    /// the spawn/grapple child transforms) keep reading the right pose. The first frame hands the GameObject's pose
    /// over instead — spawners and the save system place the GameObject after Awake built the entity.
    /// </summary>
    private bool _adopted;

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
        var w = Application.Current.World;
        if (!w.IsAlive(entity) || !w.Has<LocalTransform>(entity)) return;
        if (!_adopted)
        {
            _adopted = true;
            ref var lt = ref w.Get<LocalTransform>(entity);
            lt.Position = transform.position; // a LocalTransform write is a teleport to the physics sync
            w.Get<Heading>(entity).Yaw = transform.rotation.eulerAngles.y;
            return;
        }
        ref readonly var pose = ref w.Get<LocalTransform>(entity);
        transform.position = pose.Position;
        transform.rotation = Quaternion.Euler(0f, w.Get<Heading>(entity).Yaw, 0f);
    }

    protected override void OnDestroy()
    {
        var w = Application.Current?.World;
        if (w != null && w.IsAlive(entity) && !w.IsIterating) w.DestroyEntity(entity);
        base.OnDestroy(); // unregisters the EcsObjects mapping
    }
}
