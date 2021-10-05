using System.Collections;
using System.Collections.Generic;
using NodeCanvas.Tasks.Actions;
using UnityEngine;
using UnityEngine.Events;

public class HitscanWeapon : AWeapon
{
    public override void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        if (!bCanFire) return;
        
        bCanFire = false;
        
        _mainAnimator.SetBool(IsFiring, true);
        _weaponAudioSource.PlayOneShot(weaponData.fireSound);
        
        if (weaponData.FireType != FireType.MELEE)
            _muzzleFlash.SetActive(true);
        
        if (weaponData.bulletProjectile)
            Instantiate(weaponData.bulletProjectile, projectileSpawn, projectileDirection);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100, layerMask))
        {
            if (weaponData.bulletImpact)
                Instantiate(weaponData.bulletImpact, hit.point, Quaternion.LookRotation(hit.normal));
            
            PlayerState.Current.SetViolentCombatState();
            PlayerState.Current.AddToRage(0.1f); 
        }
        
        parentInventory.UseAmmo(1);
        
        Callback.Invoke();
        
        StartCoroutine(AllowRefire(weaponData.fireRate));
    }

    public override void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        throw new System.NotImplementedException();
    }

    public override void CancelAltFire()
    {
        throw new System.NotImplementedException();
    }
}
