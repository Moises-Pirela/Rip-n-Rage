using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/Projectile.cs.
// The projectile flies as a KinematicBody in Stop mode: the engine sweeps it through the world every frame, so
// it cannot skip through a wall or an enemy however fast it goes or however long the frame was, and the first
// thing it touches is reported through the same OnTriggerEnter the original script handled (with the exact
// contact point). Trigger volumes (water, checkpoints) still arrive through ITriggerListener. Without a body
// on the prefab the legacy transform move is used.
// Standalone addition: impacts use the data-driven VFX (surface impact / blood) and projectiles can
// tow a trail effect (ProjectileStats.trailEffect) that stops when the projectile dies.
using RipNRage.Game.Content.Vfx;
using RipNRage.Engine.Graphics.Particles;

public class Projectile : MonoBehaviour, ITriggerListener
{
    /// <summary>What a projectile can hit: level geometry, the player, enemies and the water (solids and trigger volumes alike).</summary>
    public const int HitMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision) |
                               (1 << Layers.Player) | (1 << Layers.Enemies) | (1 << Layers.Water);

    public ProjectileStats projectileStats;
    private Transform _transform;
    private KinematicBody _body;
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
        _body = GetComponent<KinematicBody>();
        if (_body != null)
        {
            _body.velocity = transform.forward * projectileStats.speed;
            _body.collisionPredicate = ShouldCollide;
            _body.ignore = entity != null ? entity.transform : null;
            _body.Collided += OnBodyCollided;
        }
        Destroy(gameObject,  projectileStats.range / projectileStats.speed);
        if (!string.IsNullOrEmpty(projectileStats.trailEffect)) _trail = GameVfx.Trail(projectileStats.trailEffect, transform);
    }

    /// <summary>Which solids stop this projectile. Plain bullets pass through other enemies (the original only damaged non-AI entities).</summary>
    protected virtual bool ShouldCollide(Collider other)
    {
        if (_instigator != null && other.gameObject == _instigator.gameObject) return false;
        return !(other.gameObject.GetComponent<EntityState>() is AIState);
    }

    private void OnBodyCollided(KinematicBody body, Contact contact)
    {
        if (_consumed) return;
        _contact = contact; _hasContact = true;
        OnTriggerEnter(contact.collider);
    }

    protected override void OnDestroy()
    {
        _trail?.Stop();
        _trail = null;
    }

    /// <summary>Spawns the impact effect for whatever the projectile ran into (surface from the collider, normal from the sweep contact or a short raycast).</summary>
    protected void SpawnImpactVfx(Collider other)
    {
        if (!GameVfx.Available) return;
        string bulletKey = string.IsNullOrEmpty(projectileStats.bulletKey) ? "enemy" : projectileStats.bulletKey;
        Vector3 fwd = transform.forward;
        if (_hasContact && _contact.collider == other)
        {
            GameVfx.Impact(new RaycastHit { collider = other, point = _contact.point, normal = _contact.normal, distance = 0f }, bulletKey);
            return;
        }
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
        if (_transform == null || _body != null) return; // the body moves it (Physics.Step, after Update)
        Move();
    }

    private void Move()
    {
        _transform.position += transform.forward * projectileStats.speed * Time.deltaTime;
    }

    public virtual void OnTriggerEnter(Collider other)
    {
        if (_consumed) return;
        if (_instigator != null)
        {   
            if (other.gameObject == _instigator.gameObject) return;
        }
        
        EntityState entityState = other.gameObject.GetComponent<EntityState>();

        if (entityState != null)
        {
            if (entityState is AIState) return;

            DamageInfo.Report(transform.position, transform.forward, 5f);
            entityState.TakeDamage(projectileStats.damage);
            DamageInfo.Clear();
        }
        else
        {
            // a loose prop or a jointed machine part takes the hit as an impulse
            var rb = other != null ? other.attachedRigidbody : null;
            if (rb != null && !rb.isKinematic) rb.AddImpulseAtPosition(transform.forward * Mathf.Max(projectileStats.damage * 0.4f, 4f), _hasContact && _contact.collider == other ? _contact.point : transform.position);
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

    public void OnTriggerExit(Collider other) { }

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
