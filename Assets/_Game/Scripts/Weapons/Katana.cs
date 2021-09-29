using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Katana : AWeapon
{
    public override void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback)
    {
        if (!bCanFire) return;
        
        Callback.Invoke();
        bCanFire = false;
        
        _mainAnimator.SetBool(IsFiring, true);
        _weaponAudioSource.PlayOneShot(weaponData.fireSound);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100, layerMask))
        {
            //deal damage
        }
        
        StartCoroutine(AllowRefire(weaponData.fireRate));
    }
    public override void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        throw new System.NotImplementedException();
    }
}
