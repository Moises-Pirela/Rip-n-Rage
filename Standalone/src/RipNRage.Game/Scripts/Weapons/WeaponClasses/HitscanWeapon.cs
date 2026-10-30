using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponClasses/HitscanWeapon.cs.
// Physics.Raycast / Instantiate / Destroy are the engine equivalents; `if (entityState)` became a
// null check (no implicit bool on engine objects).
// Standalone addition: hits go through the data-driven VFX (GameVfx) — a tracer per calibre from the
// muzzle, blood on living things and a surface-specific impact otherwise; the old spawnableVFX prefab
// is only used when no effect library is available.
using RipNRage.Game.Content.Vfx;

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
        bool vfx = GameVfx.Available;
        string bulletKey = GameVfx.AmmoKey(weaponData.ammoType);
        Vector3 muzzle = GameVfx.MuzzlePosition(cameraTransform);
        float spread = CurrentSpread; // base + bloom + movement (standalone addition)
        AddBloom();

        for (int i = 0; i < bulletsPerShot; i++)
        {
            float randomHorizontalSpread = Random.Range(-spread, spread);
            float randomVerticalSpread = Random.Range(-spread, spread);
            
            var finalDirection = cameraTransform.forward;

            finalDirection += cameraTransform.right * randomHorizontalSpread;
            finalDirection += cameraTransform.up * randomVerticalSpread; 
            
            if (!Physics.Raycast(cameraTransform.position, finalDirection, out RaycastHit hit, 100,
                _LayerMask))
            {
                if (vfx) GameVfx.Tracer(bulletKey, muzzle, cameraTransform.position + finalDirection.normalized * 100f);
                continue;
            }
            if (vfx) GameVfx.Tracer(bulletKey, muzzle, hit.point);
            
            var entityState = hit.collider.GetComponent<EntityState>();

            GameObject hitVFX = null;

            if (entityState != null)
            {
                // headshots (top of an enemy's capsule) deal double damage; hits and kills feed the style meter
                bool wasAlive = entityState.HealthState != EntityHealthState.DEAD;
                bool headshot = entityState is AIState ai && ai.IsHeadshot(hit.point);
                entityState.TakeDamage(weaponData.weaponStats.damagePerShot * (headshot ? 2f : 1f));
                bool killed = wasAlive && entityState.HealthState == EntityHealthState.DEAD;
                PlayerState.Current.AddToRage(0.1f);
                if (vfx) GameVfx.Blood(hit.point, hit.normal, bulletKey, headshot ? 1.7f : 1f);
                else hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
                _hudController.CurrentCrosshair.OnHit();
                if (entityState is AIState) StyleMeter.Current?.RegisterHit(weaponData.name, headshot, killed);
            }
            else if (vfx)
            {
                GameVfx.Impact(hit, bulletKey);
            }
            else
            {
                hitVFX = Instantiate(weaponData.spawnableVFX, hit.point, Quaternion.LookRotation(hit.normal));
                hitVFX.transform.parent = hit.collider.transform;
            }

            if (hitVFX != null) Destroy(hitVFX, 0.5f);
        }
        
        _combatController.inventory.UseAmmo(1);
    }

    public abstract override void Melee();
}
