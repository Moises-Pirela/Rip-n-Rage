using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponClasses/M1887Shotgun.cs (logic unchanged).
public class M1887Shotgun : HitscanWeapon
{
    public override void Melee()
    {
        StartCoroutine(BeginMelee());
    }

    private IEnumerator BeginMelee()
    {
        weaponAnimator.gameObject.SetActive(true);
        weaponAnimator.SetBool(WeaponMelee, true);
        yield return new WaitForSeconds(0.5f);
        MeleeShot();
    }

    private void MeleeShot()
    {  
         if (!_combatController.inventory.HasAmmoOfType(weaponData.ammoType))
        {
            //todo: play no ammo sound or swap weapon
            return;
        }
        
        if (!CanFire) return;
        
        StartCoroutine(AllowMeleeRefire_Coroutine());
        PlayerState.Current.SetViolentCombatState();
        
        _audioSource.PlayOneShot(weaponData.fireSound);
        
        int bulletsPerShot = weaponData.weaponStats.bulletsPerShot;

        for (int i = 0; i < bulletsPerShot; i++)
        {
            float randomHorizontalSpread = 0;
            float randomVerticalSpread = 0;
            
            var finalDirection = cameraTransform.forward;

            finalDirection += cameraTransform.right * randomHorizontalSpread;
            finalDirection += cameraTransform.up * randomVerticalSpread; 
            
            if (!Physics.Raycast(cameraTransform.position, finalDirection, out RaycastHit hit, 100,
                _LayerMask)) continue;
            
            var entityState = hit.collider.GetComponent<EntityState>();

            GameObject hitVFX = null;

            if (entityState != null)
            {
                entityState.TakeDamage(weaponData.weaponStats.damagePerShot);
                PlayerState.Current.AddToRage(0.05f);
                //TODO: Apply blood hit effects
                //hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
            }
            else
            {
                hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
            }

            if (hitVFX != null) Destroy(hitVFX, 0.5f);
        }
        
        _combatController.inventory.UseAmmo(1);
    }

    protected override void EndMelee()
    {
        base.EndMelee();
    }
    
    protected IEnumerator AllowMeleeRefire_Coroutine()
    {
        _cameraRecoil.ApplyRecoil(weaponData.weaponRecoil);
        CanFire = false;
        EndMelee();
        yield return new WaitForSeconds(weaponData.weaponStats.fireRate);
        weaponAnimator.SetBool(WeaponMelee, false);
        CanFire = true;
    }
}
