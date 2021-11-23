using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HitscanWeapon : Weapon
{
    public Transform cameraTransform;

    public override void Equip(Animator animator,Transform transform, LayerMask layerMask, Animator leftHandAnimator, Animator rightHandAnimator)
    {
        base.Equip(animator, transform, layerMask, leftHandAnimator, rightHandAnimator);
        
        cameraTransform = transform;
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
        
        int bulletsPerShot = weaponData.weaponStats.bulletsPerShot;

        for (int i = 0; i < bulletsPerShot; i++)
        {
            float randomHorizontalSpread = Random.Range(-weaponData.weaponStats.weaponSpread , weaponData.weaponStats.weaponSpread);
            float randomVerticalSpread = Random.Range(-weaponData.weaponStats.weaponSpread , weaponData.weaponStats.weaponSpread);
            
            var finalDirection = cameraTransform.forward;

            finalDirection += cameraTransform.right * randomHorizontalSpread;
            finalDirection += cameraTransform.up * randomVerticalSpread; 
            
            if (!Physics.Raycast(cameraTransform.position, finalDirection, out RaycastHit hit, 100,
                _LayerMask)) continue;
            
            var entityState = hit.collider.GetComponent<EntityState>();

            GameObject hitVFX = null;

            if (entityState)
            {
                entityState.TakeDamage(weaponData.weaponStats.damagePerShot);
                PlayerState.Current.AddToRage(0.05f);
                //TODO: Apply blood hit effects
                hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
                _hudController.CurrentCrosshair.OnHit();
            }
            else
            {
                hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
            }

            Destroy(hitVFX, 0.5f);
        }
        
        _combatController.inventory.UseAmmo(1);
    }

    public abstract override void Melee();
}

