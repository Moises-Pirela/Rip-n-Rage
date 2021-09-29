using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelManager : MonoBehaviour, IManager
{
    public Transform playerStart;

    private ManagerInitState ManagerInitState;

    public IEnumerator Init()
    {
        ManagerInitState = ManagerInitState.IN_PROGRESS;
        
        Instantiate(GameManager.Current.playerPrefab, playerStart.position, playerStart.rotation);
        
        ManagerInitState = ManagerInitState.COMPLETE;

        yield return 0;
    }

    public void Kill()
    {
        throw new System.NotImplementedException();
    }
    
    public ManagerInitState GetManagerState()
    {
        return ManagerInitState;
    }
}
