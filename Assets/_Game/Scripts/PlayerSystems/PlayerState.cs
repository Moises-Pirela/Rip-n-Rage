using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class PlayerState : EntityState
{
    public Action OnUpdateRage;
    
    private static PlayerState _current;
    public static PlayerState Current {
        get
        {
            return _current;
        }
    }
    [HideInInspector]public PlayerRageState RageState;
    [HideInInspector]public RageStateData CurrentRageState;

    [HideInInspector]public PlayerCombatState CombatState;

    [SerializeField] private RageStateData[] rageStates;
    
    private float _rage;
    public float Rage
    {
        get { return _rage; }
    }

    private Coroutine ResetCombatState;
    private Coroutine ResetRageState;
    private CameraRecoil _cameraRecoil;
    private CombatController _combatController;

    private void Awake()
    {
        _current = this;
    }

    private void Start()
    {
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
        Init();
    }

    public override void Init()
    {
        InitializeArmor(SaveData.Current.playerStateData.PlayerArmor);
        InitializeHealth(SaveData.Current.playerStateData.PlayerHealth);
        ResetRage();
    }

    public override void TakeDamage(float damage, float armorDamageResistance = 1)
    {
        base.TakeDamage(damage, armorDamageResistance);
        
        _cameraRecoil.Shake(0.5f);
    }

    public override void Die()
    {
        //TODO:Implement player death
    }

    private void Update()
    {
        TickRage();
        GetRageState();
        Debug.Log("Combat state: " + CombatState);
        Debug.Log("rage state: " + RageState);
    }

    public void SetViolentCombatState()
    {
        if (ResetCombatState != null)
            StopCoroutine(ResetCombatState);

        CombatState = PlayerCombatState.VIOLENT;

        ResetCombatState = StartCoroutine(ResetViolentCombatState_Coroutine());
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
        OnUpdateRage();
    }

    private void TickRage()
    {
        if (CombatState == PlayerCombatState.PACIFIST ) return;
        
        float rageStateTick = 0.2f;
        
        _rage -= Time.deltaTime * rageStateTick;

        _rage = Mathf.Clamp01(_rage);

        OnUpdateRage.Invoke();
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

    private void OnTriggerEnter(Collider other)
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
}
