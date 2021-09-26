using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;

public abstract class AWeapon : MonoBehaviour
{
    public WeaponData weaponData;
    
    public virtual void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback)
    {
        Callback.Invoke();
        
        Instantiate(weaponData.bulletProjectile, projectileSpawn, projectileDirection);
        //Instantiate(weaponData.muzzleFlash, projectileSpawn, projectileDirection);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100, layerMask))
        {
            Instantiate(weaponData.bulletImpact, hit.point, Quaternion.LookRotation(hit.normal));
        }
    }

    public abstract void AltFire();
}
