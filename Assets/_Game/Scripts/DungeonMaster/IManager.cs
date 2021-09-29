using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IManager
{
    public ManagerInitState GetManagerState();
    public IEnumerator Init();
    public void Kill();
}
