using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour, IManager
{
    public GameObject playerPrefab;
    
    private static GameManager _current;

    public static GameManager Current {
        get
        {
            if (_current == null)
            {
                _current = new GameManager();
            }

            return _current;
        }
    }

    public UnityEvent OnBeginPlay;

    private List<IManager> _managers = new List<IManager>();
        
    private ManagerInitState ManagerInitState;

    private void Awake()
    {
        _current = this;
        StartCoroutine(Init());
    }
    
    public IEnumerator Init()
    {
        GetAllManagers();

        SaveData.current =  (SaveData)SaveManager.Load("Save1");
        
        foreach (IManager manager in _managers)
        {
            StartCoroutine(manager.Init());
            
            yield return new WaitUntil(()=> manager.GetManagerState() == ManagerInitState.COMPLETE);
        }

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
