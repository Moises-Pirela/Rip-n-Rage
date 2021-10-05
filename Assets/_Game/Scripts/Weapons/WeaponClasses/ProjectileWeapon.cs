using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ProjectileWeapon : AWeapon
{
    public override void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        if (!bCanFire) return;
        
        bCanFire = false;
        
        _mainAnimator.SetBool(IsFiring, true);
        _weaponAudioSource.PlayOneShot(weaponData.fireSound);
        //_muzzleFlash.SetActive(true);
        
        GameObject projectile = Instantiate(weaponData.bulletProjectile, projectileSpawn, projectileDirection);

        Projectile projectileClass = projectile.GetComponent<Projectile>();
        
        projectileClass.SetUp(parentInventory.Owner, weaponData);

        parentInventory.UseAmmo(1);
        
        Callback.Invoke();
        
        StartCoroutine(AllowRefire(weaponData.fireRate));   
    }
    
    public override void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        throw new System.NotImplementedException();
    }
}
