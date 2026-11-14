using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/PlayerState.cs.
// OnTriggerEnter is delivered through the engine's ITriggerListener instead of Unity's magic method.
// Standalone additions: Die() is implemented (death sequence + death screen, see PlayerDeathSequence),
// damage is ignored once dead, Init() keeps the maximum health/armour fixed at 100 (the original used
// the saved value as the maximum, so healing after a checkpoint load capped at the saved health),
// Current returns null once the player object was destroyed (level restart), and the per-frame
// Debug.Log calls in Update were removed (they built two strings every frame).
[System.Serializable]
public class PlayerState : EntityState, RipNRage.Game.Ecs.ITriggerListener
{
    public const float MaxHealthValue = 100f;
    public const float MaxArmorValue = 100f;

    public Action OnUpdateRage;
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
    [HideInInspector]public PlayerRageState RageState;
    [HideInInspector]public RageStateData CurrentRageState;

    [HideInInspector]public PlayerCombatState CombatState;

    [SerializeField] public RageStateData[] rageStates;
    
    private float _rage;
    public float Rage
    {
        get { return _rage; }
    }

    private Coroutine ResetCombatState;

    // ---- rage tuning (2026-11-14: the small fixes, before the layer-7 redesign) ----
    /// <summary>Enraged / depressed durations.</summary>
    public const float EnragedDuration = 5f, DepressedDuration = 10f;
    /// <summary>Each hit landed while enraged extends it, up to <see cref="EnragedDuration"/> left.</summary>
    public const float EnragedExtendPerHit = 0.25f;
    /// <summary>Where rage lands when enraged ends: above the middle, so topping out is not a step back.</summary>
    public const float AfterEnraged = 0.7f;
    /// <summary>Draining in a fight; drifting down while out of one (never below the floor, so exploring cannot depress).</summary>
    public const float FightDecay = 0.2f, PassiveDrift = 0.02f, PassiveFloor = 0.25f;

    /// <summary>Seconds left in ENRAGED / DEPRESSED (a timer instead of a coroutine: it survives a save restore).</summary>
    private float _rageStateLeft;
    /// <summary><see cref="Time.time"/> of the last rage gain; with the style meter's last score it forms one
    /// "since the player last did something" clock (<see cref="StyleMeter.SinceLastAction"/>).</summary>
    public float LastRageGainTime { get; private set; } = -100f;
    private CombatController _combatController;

    protected override void Awake()
    {
        _current = this;
    }

    protected override void Start()
    {
        _combatController = EnragedInjector<CombatController>.GetInstance();
        Init();
    }

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
        ResetRage();
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

        if (ResetCombatState != null) StopCoroutine(ResetCombatState);
        Debug.LogWarning("Player died");

        OnHealthChanged?.Invoke(); // HUD: death portrait
        OnDied?.Invoke();

        // camera fall + slow motion + "You are dead" screen
        var sequence = GetComponent<PlayerDeathSequence>();
        if (sequence != null) sequence.Begin();
        else DeathScreen.Show();
    }

    protected override void Update()
    {
        if (IsDead) return;
        TickRage(Time.deltaTime);
        GetRageState();
    }

    public void SetViolentCombatState()
    {
        if (ResetCombatState != null)
            StopCoroutine(ResetCombatState);

        CombatState = PlayerCombatState.VIOLENT;

        ResetCombatState = StartCoroutine(ResetViolentCombatState_Coroutine());
    }

    /// <summary>Save/load support (standalone addition): restores rage and the combat/rage states.</summary>
    public void RestoreState(float rage, PlayerRageState rageState, PlayerCombatState combatState, float health, float armor)
    {
        _rage = Mathf.Clamp01(rage);
        RageState = rageState;
        CurrentRageState = rageStates[(int)rageState];
        // the save keeps no remaining time: a restored state gets its full duration (it used to never end)
        _rageStateLeft = rageState == PlayerRageState.ENRAGED ? EnragedDuration : rageState == PlayerRageState.DEPRESSED ? DepressedDuration : 0f;
        CombatState = combatState;
        if (ResetCombatState != null) StopCoroutine(ResetCombatState);
        if (combatState == PlayerCombatState.VIOLENT) ResetCombatState = StartCoroutine(ResetViolentCombatState_Coroutine());
        Health = health; Armor = armor;
        OnHealthChanged?.Invoke(); OnArmorChanged?.Invoke(); OnUpdateRage?.Invoke();
    }

    public void AddToRage(float value)
    {
        LastRageGainTime = Time.time;
        if (RageState == PlayerRageState.ENRAGED)
        {
            // hits feed the rampage instead of a value nothing reads
            _rageStateLeft = Mathf.Min(EnragedDuration, _rageStateLeft + EnragedExtendPerHit);
            return;
        }
        _rage = Mathf.Clamp01(_rage + value);
    }

    /// <summary>A kill by the player: climbs out of DEPRESSED at once (the style meter reads the state first and
    /// pays the comeback bonus — call this after <see cref="StyleMeter.RegisterHit"/>).</summary>
    public void NotifyKill()
    {
        if (RageState == PlayerRageState.DEPRESSED) EndRageState(0.5f);
    }

    private IEnumerator ResetViolentCombatState_Coroutine()
    {
        yield return new WaitForSeconds(30f);

        CombatState = PlayerCombatState.PACIFIST;
        ResetRage();
    }

    private void ResetRage() => EndRageState(0.5f);

    private void EndRageState(float rage)
    {
        _rage = rage;
        _rageStateLeft = 0f;
        RageState = PlayerRageState.NORMAL;
        CurrentRageState = rageStates[0];
        OnUpdateRage?.Invoke();
    }

    private void TickRage(float dt)
    {
        if (RageState != PlayerRageState.NORMAL)
        {
            // ENRAGED / DEPRESSED hold the value; the timer decides
            _rageStateLeft -= dt;
            if (_rageStateLeft <= 0f) EndRageState(RageState == PlayerRageState.ENRAGED ? AfterEnraged : 0.5f);
            return;
        }

        if (CombatState == PlayerCombatState.PACIFIST)
        {
            if (_rage > PassiveFloor) _rage = Mathf.Max(PassiveFloor, _rage - PassiveDrift * dt);
        }
        // in a fight, rage only drains after the same pause that starts the style meter's decay, measured on the
        // same clock — one pause costs once, not twice
        else if (StyleMeter.SinceLastAction(Time.time) > StyleMeter.DecayDelay)
            _rage = Mathf.Clamp01(_rage - FightDecay * dt);

        OnUpdateRage?.Invoke();
    }

    private void GetRageState()
    { 
        if (RageState != PlayerRageState.NORMAL) return;
        if (_rage >= 1f) SetRageState(PlayerRageState.ENRAGED, EnragedDuration);
        else if (_rage <= 0f) SetRageState(PlayerRageState.DEPRESSED, DepressedDuration);
    }

    private void SetRageState(PlayerRageState state, float duration)
    {
        RageState = state;
        CurrentRageState = rageStates[(int)state];
        _rageStateLeft = duration;
    }

    public void OnTriggerEnter(GameObject other)
    {
        if (!other.CompareTag("Checkpoint")) return;
        
        //Save data

        SaveData.Current.playerStateData.playerPosition = transform.position;
        SaveData.Current.playerStateData.playerRotation = transform.rotation;
        
        SaveData.Current.playerStateData.PlayerHealth = Health;
        SaveData.Current.playerStateData.PlayerArmor = Armor;

        SaveData.Current.inventoryData.ammo = _combatController.AmmoArray();
        SaveData.Current.inventoryData.currentWeaponIndex = _combatController.CurrentWeaponIndex;
        
        SaveManager.Save("Save1", SaveData.Current);
    }

    public void OnTriggerExit(GameObject other) { }
}
