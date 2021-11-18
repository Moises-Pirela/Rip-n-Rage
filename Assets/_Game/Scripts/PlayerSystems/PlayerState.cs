using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class PlayerState : EntityState
{
    public Action OnTickRage;
    
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
    private CameraRecoil _cameraRecoil;
    private CombatController _combatController;

    private void Awake()
    {
        _current = this;
        RageState = PlayerRageState.NONE;
        _rage = 0.5f;
        GetRageState();
        Init();
    }

    private void Start()
    {
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
    }

    public override void Init()
    {
        InitializeArmor(SaveData.Current.playerStateData.PlayerArmor);
        InitializeHealth(SaveData.Current.playerStateData.PlayerHealth);
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
    }

    public void SetViolentCombatState()
    {
        if (ResetCombatState != null)
            StopCoroutine(ResetCombatState);

        CombatState = PlayerCombatState.VIOLENT;

        ResetCombatState = StartCoroutine(ResetViolentCombatState());
    }

    public void AddToRage(float value)
    {
        //_rage = Mathf.Lerp(_rage, _rage + value, 5);
        _rage += value;
    }

    private IEnumerator ResetViolentCombatState()
    {
        yield return new WaitForSeconds(1f);

        CombatState = PlayerCombatState.PACIFIST;
    }

    private void TickRage()
    {
        float rageStateTick = CombatState == PlayerCombatState.PACIFIST ? -0.1f : 0f;
        
        _rage += Time.deltaTime * rageStateTick;

        _rage = Mathf.Clamp01(_rage);

        OnTickRage.Invoke();
    }

    private void GetRageState()
    {
        if (_rage >= 1 && RageState != PlayerRageState.ENRAGED)
        {
            RageState = PlayerRageState.ENRAGED;
            CurrentRageState = rageStates[1];
            return;
        }

        if (_rage <= 0 && RageState != PlayerRageState.DEPRESSED)
        {
            RageState = PlayerRageState.DEPRESSED;
            CurrentRageState = rageStates[2];
            return;
        }

        if (_rage > 0 && _rage < 1 && RageState != PlayerRageState.NORMAL)
        {
            RageState = PlayerRageState.NORMAL;
            CurrentRageState = rageStates[0];
        }

       
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
