using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/DungeonMaster/GameManager.cs (UnityEvent -> UnityEvent stand-in).
public class GameManager : MonoBehaviour, IManager
{
    public Prefab playerPrefab;
    
    private static GameManager _current;

    public static GameManager Current {
        get
        {
            return _current;
        }
    }

    public UnityEvent OnBeginPlay = new UnityEvent();

    private List<IManager> _managers = new List<IManager>();
        
    private ManagerInitState ManagerInitState;

    protected override void Awake()
    {
        _current = this;
        StartCoroutine(Init());
    }
    
    public IEnumerator Init()
    {
        GetAllManagers();

        // The original re-read the legacy "Save1" checkpoint file here, which made every level start
        // (including a restart after death) inherit the health and ammo of an old checkpoint and
        // overwrote the state the save system had just prepared for a load. GameSession now resets
        // SaveData.Current for a fresh start and GameSaveSystem.Load fills it in when restoring a game.
        
        foreach (IManager manager in _managers)
        {
            StartCoroutine(manager.Init());
            
            yield return new WaitUntil(()=> manager.GetManagerState() == ManagerInitState.COMPLETE);
        }

        ManagerInitState = ManagerInitState.COMPLETE;
        OnBeginPlay.Invoke();
        
        yield return 0;
    }

    public void Kill()
    {
        throw new System.NotImplementedException();
    }
    private void GetAllManagers()
    {
        foreach (Transform child in transform)
        {
            IManager manager = child.GetComponent<IManager>();
            
            if (manager == null) continue;
            
            _managers.Add(manager);
        }
    }
    
    public ManagerInitState GetManagerState()
    {
        return ManagerInitState;
    }
    
}
