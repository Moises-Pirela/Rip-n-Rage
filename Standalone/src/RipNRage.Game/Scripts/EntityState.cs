using System;
using RipNRage.Engine.Ecs;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;
// the Health *component* needs an alias inside this class: the legacy `Health` float property shadows the type name
using HealthComponent = RipNRage.Game.Ecs.Combat.Health;

// MIGRATION FACADE (layer 5; gone in layer 9). Health and armour live in the Health component on the thing's ECS
// entity, damage is a DamageEvent consumed by HealthSystem at the end of the frame, and death is the Dead tag — this
// class only keeps the surface the not-yet-ported scripts talk to (the HUD's bars, the AI's "was I hit" probe, the
// save system, the weapons until 5.3). Subclasses bind their entity with Bind(); CombatBridgeSystem calls NotifyHit
// when a blow lands, which is where the legacy change callbacks fire. Death has no facade half since 8.3.
//
// Semantics that changed with the port, on purpose:
//  - damage is applied at the end of the frame, not inside TakeDamage — a reader in the same scene update still sees
//    the old value (the weapons' synchronous "did this kill" checks are rewritten on top of HitEvent in 5.3);
//  - death latches (the Dead tag): a second lethal blow does nothing.
public abstract class EntityState : MonoBehaviour
{
    /// <summary>The entity carrying this thing's <see cref="Health"/> (bound by the subclass).</summary>
    public Entity entity { get; private set; }

    private static World W => Application.Current.World;
    private bool Bound => W != null && W.IsAlive(entity) && W.Has<HealthComponent>(entity);

    /// <summary>Ties the facade to its health-carrying entity and registers the GameObject for the event bridge.</summary>
    protected void Bind(Entity e)
    {
        entity = e;
        EcsObjects.Register(e, gameObject);
    }

    public EntityHealthState HealthState
    {
        get => Bound && (W.Has<Dead>(entity) || W.Get<HealthComponent>(entity).Current <= 0f) ? EntityHealthState.DEAD : EntityHealthState.ALIVE;
        set
        {
            if (!Bound || W.IsIterating) return; // the systems never set this; scene-side writers only
            if (value == EntityHealthState.DEAD) { if (!W.Has<Dead>(entity)) W.Add(entity, new Dead()); }
            else if (W.Has<Dead>(entity)) W.Remove<Dead>(entity);
        }
    }

    public float Health
    {
        get => Bound ? W.Get<HealthComponent>(entity).Current : 0f;
        set { if (Bound) { ref var h = ref W.Get<HealthComponent>(entity); h.Current = Mathf.Clamp(value, 0f, h.Max); } }
    }

    /// <summary>Armour, or 0 when this thing wears none (no <see cref="Armor"/> component).</summary>
    public float Armor
    {
        get => Bound && W.Has<Armor>(entity) ? W.Get<Armor>(entity).Current : 0f;
        set { if (Bound && W.Has<Armor>(entity)) { ref var a = ref W.Get<Armor>(entity); a.Current = Mathf.Clamp(value, 0f, a.Max); } }
    }

    public Action OnHealthChanged;
    public Action OnArmorChanged;

    public abstract void Init();

    /// <summary>Posts the blow as a <see cref="DamageEvent"/> (applied by HealthSystem at the end of the frame),
    /// with no circumstances: scripted damage from nowhere in particular (the weapons post their own events).</summary>
    public void TakeDamage(float damage)
    {
        if (!Bound) return;
        W.Post(new DamageEvent
        {
            Target = entity,
            Amount = damage,
            Point = transform.position,
            Direction = Vector3.forward,
        });
    }


    public void Heal(float value)
    {
        if (!Bound) return;
        ref var h = ref W.Get<HealthComponent>(entity);
        h.Current = Mathf.Clamp(h.Current + value, 0f, h.Max);
        OnHealthChanged?.Invoke();
    }



    public void InitializeHealth(float maxHealth)
    {
        if (!Bound) return;
        ref var h = ref W.Get<HealthComponent>(entity);
        h.Max = maxHealth; h.Current = maxHealth;
    }

    /// <summary>Gives this thing armour (or re-fills it); <paramref name="maxArmor"/> 0 means it wears none.</summary>
    public void InitializeArmor(float maxArmor, float resistance = 0.75f)
    {
        if (!Bound || W.IsIterating) return;
        if (maxArmor <= 0f) { if (W.Has<Armor>(entity)) W.Remove<Armor>(entity); return; }
        W.Add(entity, new Armor { Current = maxArmor, Max = maxArmor, Resistance = resistance });
    }

    // ---------------------------------------------------------------- event bridge (CombatBridgeSystem)

    /// <summary>A blow landed on this entity this frame (HealthSystem already applied it).</summary>
    internal virtual void NotifyHit(in HitEvent e)
    {
        OnHealthChanged?.Invoke();
        OnArmorChanged?.Invoke();
    }

    protected override void OnDestroy() => EcsObjects.Unregister(entity);
}
