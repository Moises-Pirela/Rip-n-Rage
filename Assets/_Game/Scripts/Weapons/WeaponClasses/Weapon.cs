using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Weapon : MonoBehaviour
{   
    public WeaponData WeaponData;
    protected LayerMask _LayerMask;

    protected bool CanFire = true;
    protected Coroutine AllowRefireCoroutine;

    public virtual void Equip(Transform transform, LayerMask layerMask)
    {
        _LayerMask = layerMask;
    }
    public abstract void Fire();
    public abstract void Melee();
    
    protected virtual IEnumerator AllowRefire_Coroutine()
    {
        CanFire = false;
        yield return new WaitForSeconds(WeaponData.fireRate);
        CanFire = true;
        AllowRefireCoroutine = null;
    }
}
