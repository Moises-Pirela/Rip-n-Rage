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

        SaveData.Current =  (SaveData)SaveManager.Load("Save1");
        
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
