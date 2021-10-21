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

        SpawnProjectile(projectileSpawn, projectileDirection);

        parentInventory.UseAmmo(1);
        
        Callback.Invoke();
        
        StartCoroutine(AllowRefire(weaponData.fireRate));   
    }

    public virtual GameObject SpawnProjectile(Vector3 spawnPos, Quaternion spawnDirection)
    {
        GameObject projectile = Instantiate(weaponData.bulletProjectile, spawnPos, spawnDirection);

        Projectile projectileClass = projectile.GetComponent<Projectile>();
        
        projectileClass.SetUp(parentInventory.Owner, weaponData);

        return projectile;
    }
}
