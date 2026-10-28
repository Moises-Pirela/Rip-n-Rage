using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/RocketProjectile.cs (logic unchanged).
public class RocketProjectile : Projectile
{
    public override void OnTriggerEnter(Collider other)
    {
        if (_instigator != null)
        {   
            if (other.gameObject == _instigator.gameObject) return;
        }
        
        var contactPoint = GetPointOfContact();
        GameObject vfx = projectileStats.hitVFX != null ? Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized)) : null;

        var hits = Physics.SphereCastAll(contactPoint, 10, contactPoint, 100);

        foreach (var enemy in hits)
        {
            var enemyState = enemy.collider.GetComponent<EntityState>();

            if (enemyState == null) continue;
            
            if (enemyState is AIState)
            {
                PlayerState.Current.AddToRage(0.05f);
            }

            enemyState.TakeDamage(projectileStats.damage);
        }
        
        Destroy(gameObject);
        if (vfx != null) Destroy(vfx, 0.4f);
    }
}
