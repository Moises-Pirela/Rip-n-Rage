using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/RocketProjectile.cs (logic unchanged;
// the explosion visual is the data-driven "explosions" effect when the VFX library is available).
using RipNRage.Game.Content.Vfx;

public class RocketProjectile : Projectile
{
    public override void OnTriggerEnter(Collider other)
    {
        if (_instigator != null)
        {   
            if (other.gameObject == _instigator.gameObject) return;
        }
        
        var contactPoint = GetPointOfContact();
        GameObject vfx = null;
        if (GameVfx.Available)
        {
            Vector3 fwd = transform.forward;
            Vector3 normal = Physics.Raycast(transform.position - fwd * 0.6f, fwd, out RaycastHit hit, 3f, ~((1 << Layers.Projectile) | (1 << Layers.VisionSensor))) ? hit.normal : -fwd;
            GameVfx.Explosion(string.IsNullOrEmpty(projectileStats.explosionEffect) ? "rocket" : projectileStats.explosionEffect, contactPoint, normal);
        }
        else vfx = projectileStats.hitVFX != null ? Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized)) : null;

        // camera shake and knockback for the player when near (standalone addition) — rocket jumps!
        CameraRecoil.ShakeEverywhere(contactPoint, 22f, 1.0f);
        var player = PlayerState.Current;
        if (player != null && !player.IsDead)
        {
            Vector3 away = player.transform.position - contactPoint;
            float dist = away.magnitude;
            const float blastRadius = 11f;
            if (dist < blastRadius)
            {
                float k = 1f - dist / blastRadius;
                var fps = player.GetComponent<FPSController>();
                fps?.AddImpulse((away.normalized * 13f + Vector3.up * 10f) * k);
            }
        }

        var hits = Physics.SphereCastAll(contactPoint, 10, contactPoint, 100);

        foreach (var enemy in hits)
        {
            var enemyState = enemy.collider.GetComponent<EntityState>();

            if (enemyState == null) continue;
            
            if (enemyState is AIState)
            {
                PlayerState.Current.AddToRage(0.05f);
            }

            bool wasAlive = enemyState.HealthState != EntityHealthState.DEAD;
            // the player's own rockets hurt the player a lot less (rocket jumping is a movement tool, not suicide)
            float dmg = enemyState is PlayerState && _instigator == null ? projectileStats.damage * 0.12f : projectileStats.damage;
            enemyState.TakeDamage(dmg);
            // the player's rockets (no instigator entity) score style kills under the launcher's name
            if (_instigator == null && enemyState is AIState && wasAlive && enemyState.HealthState == EntityHealthState.DEAD)
                StyleMeter.Current?.RegisterHit("RocketLauncher", false, true);
        }
        
        Destroy(gameObject);
        if (vfx != null) Destroy(vfx, 0.4f);
    }
}
