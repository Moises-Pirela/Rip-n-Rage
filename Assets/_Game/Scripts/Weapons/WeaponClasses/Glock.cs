using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Glock : HitscanWeapon
{
    public override void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback)
    {
        if(!bCanAltFire) return;
        
        bCanAltFire = false;
        StartCoroutine(BeginAlt(direction, origin, layerMask, projectileDirection, projectileSpawn, Callback));
    }

    private IEnumerator BeginAlt(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback)
    {
        StartCoroutine(AllowAltRefire());
        Fire(direction,origin,layerMask,projectileDirection,projectileSpawn,Callback);
        yield return new WaitForSeconds(weaponData.fireRate);
        Fire(direction,origin,layerMask,projectileDirection,projectileSpawn,Callback);
    }
}
