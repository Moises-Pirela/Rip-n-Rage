using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/Projectile.cs.
// OnTriggerEnter comes through ITriggerListener; the projectile registers itself as a physics
// "mover" so the engine checks its overlap against the world each fixed step.
// Standalone addition: impacts use the data-driven VFX (surface impact / blood) and projectiles can
// tow a trail effect (ProjectileStats.trailEffect) that stops when the projectile dies.
using RipNRage.Game.Content.Vfx;
using RipNRage.Engine.Graphics.Particles;

public class Projectile : MonoBehaviour, ITriggerListener
{
    public ProjectileStats projectileStats;
    private Transform _transform;

    protected EntityState _instigator;
    [SerializeField] protected LayerMask entityDamageLayer;
    protected ParticleEffectInstance _trail;

    public void SetupProjectile(bool firedByPlayer = false, EntityState entity = null)
    {
        _instigator = entity;
        _transform = transform;
        Destroy(gameObject,  projectileStats.range / projectileStats.speed);
        if (!string.IsNullOrEmpty(projectileStats.trailEffect)) _trail = GameVfx.Trail(projectileStats.trailEffect, transform);
    }

    protected override void OnDestroy()
    {
        _trail?.Stop();
        _trail = null;
    }

    /// <summary>Spawns the impact effect for whatever the projectile ran into (surface from the collider, normal from a short raycast).</summary>
    protected void SpawnImpactVfx(Collider other)
    {
        if (!GameVfx.Available) return;
        string bulletKey = string.IsNullOrEmpty(projectileStats.bulletKey) ? "enemy" : projectileStats.bulletKey;
        Vector3 fwd = transform.forward;
        if (Physics.Raycast(transform.position - fwd * 0.6f, fwd, out RaycastHit hit, 1.5f, ~((1 << Layers.Projectile) | (1 << Layers.VisionSensor))))
        {
            GameVfx.Impact(hit, bulletKey);
            return;
        }
        // no geometry behind the trigger overlap: fall back to the collider we touched
        string surface = other != null && other.gameObject.GetComponent<EntityState>() != null ? Surfaces.Flesh : Surfaces.Default;
        GameVfx.ImpactOn(surface, transform.position, -fwd, bulletKey);
    }

    public void SetupProjectile(EntityState entity) => SetupProjectile(false, entity);

    protected override void Update()
    {
        if (_transform == null) return;
        Move();
    }

    private void Move()
    {
        _transform.position += transform.forward * projectileStats.speed * Time.deltaTime;
    }

    public virtual void OnTriggerEnter(Collider other)
    {
        if (_instigator != null)
        {   
            if (other.gameObject == _instigator.gameObject) return;
        }
        
        EntityState entityState = other.gameObject.GetComponent<EntityState>();

        if (entityState != null)
        {
            if (entityState is AIState) return;

            entityState.TakeDamage(projectileStats.damage);
        }
        else
        {
           // var contactPoint = GetPointOfContact();
            //Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized));
        }
        
        if (GameVfx.Available)
        {
            SpawnImpactVfx(other);
            Destroy(gameObject);
            return;
        }
        var contactPoint = GetPointOfContact();
        GameObject vfx = projectileStats.hitVFX != null ? Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized)) : null;
        Destroy(gameObject);
        if (vfx != null) Destroy(vfx, 0.4f);
    }

    public void OnTriggerExit(Collider other) { }

    protected Vector3 GetPointOfContact()
    {
        RaycastHit hit;
        
        return Physics.Raycast(transform.position, transform.forward, out hit) ? hit.point : transform.position;
    }
}
