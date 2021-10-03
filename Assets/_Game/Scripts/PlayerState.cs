using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class PlayerState : EntityState
{
    public UnityEvent OnEnterEnraged;
    public UnityEvent OnEnterDepressed;
    public UnityEvent OnEnterNormal;
    
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

    private void Awake()
    {
        _current = this;
        RageState = PlayerRageState.NONE;
        _rage = 0.5f;
        GetRageState();
    }

    private void Update()
    {
        TickRage();
        GetRageState();

        //string ragestring = String.Format("{0} at Rage meter: {1}", RageState, _rage);
        //Debug.Log(CombatState);
    }

    public void SetViolentCombatState()
    {
        if (ResetCombatState != null)
            StopCoroutine(ResetCombatState);

        CombatState = PlayerCombatState.VIOLENT;

        ResetCombatState = StartCoroutine(ResetViolentCombatState());
    }

    private IEnumerator ResetViolentCombatState()
    {
        yield return new WaitForSeconds(3f);

        CombatState = PlayerCombatState.PACIFIST;
    }

    private void TickRage()
    {
        float rageStateTick = CombatState == PlayerCombatState.PACIFIST ? -0.1f : 0.1f;
        
        _rage += Time.deltaTime * rageStateTick;

        _rage = Mathf.Clamp01(_rage);
    }

    private void GetRageState()
    {
        if (_rage >= 1 && RageState != PlayerRageState.ENRAGED)
        {
            RageState = PlayerRageState.ENRAGED;
            CurrentRageState = rageStates[1];
            OnEnterEnraged.Invoke();
            return;
        }

        if (_rage <= 0 && RageState != PlayerRageState.DEPRESSED)
        {
            RageState = PlayerRageState.DEPRESSED;
            CurrentRageState = rageStates[2];
            OnEnterDepressed.Invoke();
            return;
        }

        if (_rage > 0 && _rage < 1 && RageState != PlayerRageState.NORMAL)
        {
            RageState = PlayerRageState.NORMAL;
            CurrentRageState = rageStates[0];
            OnEnterNormal.Invoke();
        }

       
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Checkpoint")) return;
        
        //Save data

        SaveData.current.PlayerStateData.playerPosition = transform.position;
        SaveData.current.PlayerStateData.playerRotation = transform.rotation;

        healthComponent.Health = 50;
        
        SaveData.current.PlayerStateData.PlayerHealth = healthComponent.Health;

        FPSCombat fpsCombat = GetComponent<FPSCombat>();

        SaveData.current.PlayerStateData.Ammo = fpsCombat.inventoryComponent.InventoryAmmo;
        
        Debug.LogWarning("Entering checkpoint - save data");
        
        SaveManager.Save("Save1", SaveData.current);
    }
}
