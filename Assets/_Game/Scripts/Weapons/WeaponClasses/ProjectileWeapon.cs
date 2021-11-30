using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ProjectileWeapon : Weapon
{
    public Transform projectileSpawn;
    public GameObject projectilePrefab;
    public override void Equip(Animator animator,Transform transform, LayerMask layerMask, Animator leftHandAnimator, Animator rightHandAnimator)
    {
        base.Equip(animator, transform, layerMask, leftHandAnimator, rightHandAnimator);

        projectileSpawn = transform;
    }
    
    public override void Fire()
    {
        if (!_combatController.inventory.HasAmmoOfType(weaponData.ammoType))
        {
            //todo: play no ammo sound or swap weapon
            return;
        }
        
        if (!CanFire) return;
        
        if (AllowRefireCoroutine != null)
            StopCoroutine(AllowRefire_Coroutine());
        
        _hudController.CurrentCrosshair.OnShoot();
        
        AllowRefireCoroutine = StartCoroutine(AllowRefire_Coroutine());
        PlayerState.Current.SetViolentCombatState();
        
        
        weaponAnimator.SetBool(IsFiring, true);
        _audioSource.PlayOneShot(weaponData.fireSound);

        GameObject projectile = Instantiate(projectilePrefab, projectileSpawn.position, Quaternion.LookRotation(projectileSpawn.forward));
        Projectile projectileClass = projectile.GetComponent<Projectile>();
        
        projectileClass.SetupProjectile();
    }
}
