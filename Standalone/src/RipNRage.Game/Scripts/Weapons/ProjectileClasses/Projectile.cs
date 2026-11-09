using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/Projectile.cs.
// ECS migration: the legacy KinematicBody this projectile flew as is gone (layer 2). It now moves by its transform
// and hits nothing until weapons are ported (layer 5: projectiles as ECS entities, hits via physics events);
// OnTriggerEnter is kept as the reference for that port.
// Standalone addition: impacts use the data-driven VFX (surface impact / blood) and projectiles can
// tow a trail effect (ProjectileStats.trailEffect) that stops when the projectile dies.
using RipNRage.Game.Content.Vfx;
using RipNRage.Engine.Graphics.Particles;

public class Projectile : MonoBehaviour
{
    /// <summary>What a projectile can hit: level geometry, the player, enemies and the water (solids and trigger volumes alike).</summary>
    public const int HitMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision) |
                               (1 << Layers.Player) | (1 << Layers.Enemies) | (1 << Layers.Water);

    public ProjectileStats projectileStats;
    private Transform _transform;
    private bool _consumed;
    private bool _hasContact;
    private Contact _contact;

    protected EntityState _instigator;
    [SerializeField] protected LayerMask entityDamageLayer;
    protected ParticleEffectInstance _trail;

    /// <summary>True once the projectile hit something (every later overlap report is ignored).</summary>
    protected bool HasHit => _consumed;
    /// <summary>Marks the projectile as spent; returns false when it already was.</summary>
    protected bool Consume() { if (_consumed) return false; _consumed = true; return true; }

    public void SetupProjectile(bool firedByPlayer = false, EntityState entity = null)
    {
        _instigator = entity;
        _transform = transform;
        Destroy(gameObject,  projectileStats.range / projectileStats.speed);
        if (!string.IsNullOrEmpty(projectileStats.trailEffect)) _trail = GameVfx.Trail(projectileStats.trailEffect, transform);
    }

    /// <summary>Which solids stop this projectile. Plain bullets pass through other enemies (the original only damaged non-AI entities).</summary>
    protected virtual bool ShouldCollide(GameObject other)
    {
        if (_instigator != null && other == _instigator.gameObject) return false;
        return !(other.GetComponent<EntityState>() is AIState);
    }

    protected override void OnDestroy()
    {
        _trail?.Stop();
        _trail = null;
    }

    /// <summary>Spawns the impact effect for whatever the projectile ran into (surface from the collider, normal from the sweep contact or a short raycast).</summary>
    protected void SpawnImpactVfx(GameObject other)
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
        string surface = other != null && other.GetComponent<EntityState>() != null ? Surfaces.Flesh : Surfaces.Default;
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

    public virtual void OnTriggerEnter(GameObject other)
    {
        if (_consumed) return;
        if (_instigator != null)
        {   
            if (other == _instigator.gameObject) return;
        }
        
        EntityState entityState = other.GetComponent<EntityState>();

        if (entityState != null)
        {
            if (entityState is AIState) return;

            DamageInfo.Report(transform.position, transform.forward, 5f);
            entityState.TakeDamage(projectileStats.damage);
            DamageInfo.Clear();
        }
        Consume();
        
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

    public void OnTriggerExit(GameObject other) { }

    protected Vector3 GetPointOfContact()
    {
        if (_hasContact) return _contact.point; // the sweep knows exactly where we touched
        RaycastHit hit;
        
        return Physics.Raycast(transform.position, transform.forward, out hit) ? hit.point : transform.position;
    }

    /// <summary>Surface normal of the body contact when there was one (the caller's fallback otherwise).</summary>
    protected bool TryGetContactNormal(out Vector3 normal)
    {
        normal = _hasContact ? _contact.normal : Vector3.zero;
        return _hasContact;
    }
}
