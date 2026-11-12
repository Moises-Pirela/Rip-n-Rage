using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponClasses/ProjectileWeapon.cs (prefab -> Prefab).
public abstract class ProjectileWeapon : Weapon
{
    public Transform projectileSpawn;
    public Prefab projectilePrefab;
    public override void Equip(RipNRage.Engine.Ecs.Animation.AnimatedSprite animator,Transform transform, LayerMask layerMask, RipNRage.Engine.Ecs.Animation.AnimatedSprite leftHandAnimator, RipNRage.Engine.Ecs.Animation.AnimatedSprite rightHandAnimator)
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
        RipNRage.Engine.Ecs.Audio.Sfx.Play(weaponData.fireSound);

        // spread / bloom (standalone addition): rockets scatter a little too, less than bullets
        float spread = CurrentSpread * weaponData.weaponStats.projectileSpreadScale;
        AddBloom();
        Vector3 dir = projectileSpawn.forward + projectileSpawn.right * Random.Range(-spread, spread) + projectileSpawn.up * Random.Range(-spread, spread);
        GameObject projectile = Instantiate(projectilePrefab, projectileSpawn.position, Quaternion.LookRotation(dir.normalized));
        Projectile projectileClass = projectile.GetComponent<Projectile>();
        
        projectileClass.SetupProjectile(true);
        
        _combatController.inventory.UseAmmo(1);
    }
}
