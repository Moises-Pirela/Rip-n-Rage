using System.Collections;
using System.Collections.Generic;

public interface IManager
{
    public ManagerInitState GetManagerState();
    public IEnumerator Init();
    public void Kill();
}
