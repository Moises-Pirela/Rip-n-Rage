using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/PlayerState.cs.
// Standalone additions: Die() is implemented (death sequence + death screen, see PlayerDeathSequence),
// damage is ignored once dead, Init() keeps the maximum health/armour fixed at 100 (the original used
// the saved value as the maximum, so healing after a checkpoint load capped at the saved health),
// Current returns null once the player object was destroyed (level restart), and the per-frame
// Debug.Log calls in Update were removed (they built two strings every frame).
[System.Serializable]
public class PlayerState : EntityState
{
    public const float MaxHealthValue = 100f;
    public const float MaxArmorValue = 100f;

    /// <summary>Raised once, when health reaches zero.</summary>
    public Action OnDied;
    
    private static PlayerState _current;
    public static PlayerState Current {
        get
        {
            return _current != null && !_current.IsDestroyed ? _current : null;
        }
    }

    public bool IsDead => HealthState == EntityHealthState.DEAD;

    protected override void Awake()
    {
        _current = this;
    }

    protected override void Start() => Init();

    public override void Init()
    {
        // the Health component sits on the character entity PlayerLink built; this facade reads and writes it
        Bind(RipNRage.Game.Ecs.PlayerLink.PlayerEntity);
        _dieRan = false; // a (re)initialised player can die again
        // fixed maximums; the save data only provides the current values
        InitializeArmor(MaxArmorValue, 1f); // the player's armour takes a hit one for one
        InitializeHealth(MaxHealthValue);
        var saved = SaveData.Current.playerStateData;
        if (saved.PlayerHealth > 0f) Health = Mathf.Min(saved.PlayerHealth, MaxHealthValue);
        Armor = Mathf.Clamp(saved.PlayerArmor, 0f, MaxArmorValue);
        HealthState = EntityHealthState.ALIVE;
        OnHealthChanged?.Invoke();
        OnArmorChanged?.Invoke();
    }

    internal override void NotifyHit(in RipNRage.Game.Ecs.Combat.HitEvent e)
    {
        base.NotifyHit(in e);
        if (!IsDead) RipNRage.Game.Ecs.PlayerCamera.Kick(0.5f);
    }

    /// <summary>Die must run its sequence exactly once, but cannot gate on IsDead any more: the pipeline applies the
    /// Dead tag and the zero health before the bridge delivers the DeathEvent, so IsDead is already true here (the
    /// legacy flow called Die while HealthState still said ALIVE).</summary>
    private bool _dieRan;

    public override void Die()
    {
        if (_dieRan) return;
        _dieRan = true;
        HealthState = EntityHealthState.DEAD;
        Health = 0f;

        Debug.LogWarning("Player died");

        OnHealthChanged?.Invoke(); // HUD: death portrait
        OnDied?.Invoke();

        // camera fall + slow motion + "You are dead" screen
        var sequence = GetComponent<PlayerDeathSequence>();
        if (sequence != null) sequence.Begin();
        else DeathScreen.Show();
    }

    /// <summary>Save/load support (standalone addition): restores health and armour. The rage meters are a
    /// component now; the save restores them through <c>Rages.Restore</c>.</summary>
    public void RestoreState(float health, float armor)
    {
        Health = health; Armor = armor;
        OnHealthChanged?.Invoke(); OnArmorChanged?.Invoke();
    }

}
