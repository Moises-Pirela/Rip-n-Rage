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
public class PlayerState : EntityState, ITriggerListener
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
    private Coroutine ResetRageState;
    private CameraRecoil _cameraRecoil;
    private CombatController _combatController;

    protected override void Awake()
    {
        _current = this;
    }

    protected override void Start()
    {
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
        Init();
    }

    public override void Init()
    {
        // fixed maximums; the save data only provides the current values
        InitializeArmor(MaxArmorValue);
        InitializeHealth(MaxHealthValue);
        var saved = SaveData.Current.playerStateData;
        if (saved.PlayerHealth > 0f) Health = Mathf.Min(saved.PlayerHealth, MaxHealthValue);
        Armor = Mathf.Clamp(saved.PlayerArmor, 0f, MaxArmorValue);
        HealthState = EntityHealthState.ALIVE;
        ResetRage();
        OnHealthChanged?.Invoke();
        OnArmorChanged?.Invoke();
    }

    public override void TakeDamage(float damage, float armorDamageResistance = 1)
    {
        if (IsDead) return;

        base.TakeDamage(damage, armorDamageResistance);
        
        if (!IsDead) _cameraRecoil?.Shake(0.5f);
    }

    public override void Die()
    {
        if (IsDead) return;
        HealthState = EntityHealthState.DEAD;
        Health = 0f;

        if (ResetCombatState != null) StopCoroutine(ResetCombatState);
        if (ResetRageState != null) StopCoroutine(ResetRageState);
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
        TickRage();
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
        CombatState = combatState;
        if (ResetCombatState != null) StopCoroutine(ResetCombatState);
        if (combatState == PlayerCombatState.VIOLENT) ResetCombatState = StartCoroutine(ResetViolentCombatState_Coroutine());
        Health = health; Armor = armor;
        OnHealthChanged?.Invoke(); OnArmorChanged?.Invoke(); OnUpdateRage?.Invoke();
    }

    public void AddToRage(float value)
    {
        //_rage = Mathf.Lerp(_rage, _rage + value, 5);
        _rage += value;
    }

    private IEnumerator ResetViolentCombatState_Coroutine()
    {
        yield return new WaitForSeconds(30f);

        CombatState = PlayerCombatState.PACIFIST;
        ResetRage();
    }
    
    private IEnumerator ResetRageState_Coroutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        ResetRage();
    }

    private void ResetRage()
    {
        _rage = 0.5f;
        RageState = PlayerRageState.NORMAL;
        CurrentRageState = rageStates[0];
        OnUpdateRage?.Invoke();
    }

    private void TickRage()
    {
        if (CombatState == PlayerCombatState.PACIFIST ) return;
        
        float rageStateTick = 0.2f;
        
        _rage -= Time.deltaTime * rageStateTick;

        _rage = Mathf.Clamp01(_rage);

        OnUpdateRage?.Invoke();
    }

    private void GetRageState()
    { 
        if (RageState != PlayerRageState.NORMAL) return;
        
        if (_rage >= 1 && RageState != PlayerRageState.ENRAGED)
        {
            SetRageState(PlayerRageState.ENRAGED, 5f);
            
            return;
        }

        if (_rage <= 0 && RageState != PlayerRageState.DEPRESSED)
        {
            SetRageState(PlayerRageState.DEPRESSED, 10f);
        }
    }

    private void SetRageState(PlayerRageState state, float duration)
    {
        RageState = state;
        CurrentRageState = rageStates[(int)state];
            
        if (ResetRageState != null)
            StopCoroutine(ResetRageState);

        ResetRageState = StartCoroutine(ResetRageState_Coroutine(duration));
    }

    public void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Checkpoint")) return;
        
        //Save data

        SaveData.Current.playerStateData.playerPosition = transform.position;
        SaveData.Current.playerStateData.playerRotation = transform.rotation;
        
        SaveData.Current.playerStateData.PlayerHealth = Health;
        SaveData.Current.playerStateData.PlayerArmor = Armor;

        SaveData.Current.inventoryData.ammo = _combatController.inventory.ammo;
        SaveData.Current.inventoryData.currentWeaponIndex = _combatController.inventory.currentWeaponIndex;
        
        SaveManager.Save("Save1", SaveData.Current);
    }

    public void OnTriggerExit(Collider other) { }
}
