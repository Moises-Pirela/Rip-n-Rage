using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Timers;
using UnityEngine;



[System.Serializable]
public class PlayerState : EntityState
{
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

        _rage = 0.5f;
        
        RageState = GetRageState();
        SetCurrentRageState();
    }

    private void Update()
    {
        TickRage();
        
        RageState = GetRageState();
        
        SetCurrentRageState();

        //string ragestring = String.Format("{0} at Rage meter: {1}", RageState, _rage);
        Debug.Log(CombatState);
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

    private void SetCurrentRageState()
    {
        switch (RageState)
        {
            case PlayerRageState.NORMAL:
                CurrentRageState = rageStates[0];
                break;
            case PlayerRageState.ENRAGED:
                CurrentRageState = rageStates[1];
                break;
            case PlayerRageState.DEPRESSED:
                CurrentRageState = rageStates[2];
                break;
        }
    }

    private PlayerRageState GetRageState()
    {
        if (_rage >= 1)
            return PlayerRageState.ENRAGED;
        
        if (_rage <= 0)
            return PlayerRageState.DEPRESSED;

        return PlayerRageState.NORMAL;
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
