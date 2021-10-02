using System.Collections;
using System.Collections.Generic;
using System.Resources;
using UnityEngine;

public class LevelManager : MonoBehaviour, IManager
{
    public Transform playerStart;

    private ManagerInitState ManagerInitState;

    public IEnumerator Init()
    {
        ManagerInitState = ManagerInitState.IN_PROGRESS;

        Instantiate(GameManager.Current.playerPrefab, playerStart.position, playerStart.rotation);
        // if (SaveData.current != null)
        // {
        //     Vector3 vector3 = SaveData.current.PlayerStateData.playerPosition;
        //     Quaternion quaternion = SaveData.current.PlayerStateData.playerRotation;
        //     
        //     Instantiate(GameManager.Current.playerPrefab, vector3 , quaternion);
        // }
        // else
        // {
        //     Instantiate(GameManager.Current.playerPrefab, playerStart.position, playerStart.rotation);
        // }
        
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
