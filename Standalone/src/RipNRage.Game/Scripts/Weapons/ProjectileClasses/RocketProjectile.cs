using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/RocketProjectile.cs (logic unchanged;
// the explosion visual is the data-driven "explosions" effect when the VFX library is available).
using RipNRage.Game.Content.Vfx;

public class RocketProjectile : Projectile
{
    public override void OnTriggerEnter(GameObject other)
    {
        if (HasHit) return;
        if (_instigator != null)
        {   
            if (other == _instigator.gameObject) return;
        }
        Consume();
        
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
        // rigid bodies (crates, barrels) are thrown by the blast
        if (projectileStats.explosionImpulse > 0f && projectileStats.explosionRadius > 0f)
            Physics.AddExplosionForce(projectileStats.explosionImpulse, contactPoint, projectileStats.explosionRadius, 0.6f, ~(1 << Layers.Player)); // the player gets the tuned rocket-jump impulse above
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

        // blast damage by distance (the legacy sphere cast returned colliders; hits now name ECS entities, and
        // characters are not entities until layers 4 / 6)
        foreach (var enemyState in Scene.Current.FindObjectsOfType<EntityState>())
        {
            if ((enemyState.transform.position - contactPoint).sqrMagnitude > 10f * 10f) continue;
            
            if (enemyState is AIState)
            {
                PlayerState.Current.AddToRage(0.05f);
            }

            bool wasAlive = enemyState.HealthState != EntityHealthState.DEAD;
            // the player's own rockets hurt the player a lot less (rocket jumping is a movement tool, not suicide)
            float dmg = enemyState is PlayerState && _instigator == null ? projectileStats.damage * 0.12f : projectileStats.damage;
            Vector3 blastDir = enemyState.transform.position - contactPoint; blastDir.y += 0.5f;
            DamageInfo.Report(contactPoint, blastDir, Mathf.Max(projectileStats.explosionImpulse, 60f), true, Mathf.Max(projectileStats.explosionRadius, 6f));
            enemyState.TakeDamage(dmg);
            DamageInfo.Clear();
            // the player's rockets (no instigator entity) score style kills under the launcher's name
            if (_instigator == null && enemyState is AIState && wasAlive && enemyState.HealthState == EntityHealthState.DEAD)
                StyleMeter.Current?.RegisterHit("RocketLauncher", false, true);
        }
        
        Destroy(gameObject);
        if (vfx != null) Destroy(vfx, 0.4f);
    }
}
