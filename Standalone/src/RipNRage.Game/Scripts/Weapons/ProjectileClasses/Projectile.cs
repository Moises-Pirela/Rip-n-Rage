using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/ProjectileClasses/Projectile.cs.
// OnTriggerEnter comes through ITriggerListener; the projectile registers itself as a physics
// "mover" so the engine checks its overlap against the world each fixed step.
public class Projectile : MonoBehaviour, ITriggerListener
{
    public ProjectileStats projectileStats;
    private Transform _transform;

    protected EntityState _instigator;
    [SerializeField] protected LayerMask entityDamageLayer;

    public void SetupProjectile(bool firedByPlayer = false, EntityState entity = null)
    {
        _instigator = entity;
        _transform = transform;
        Destroy(gameObject,  projectileStats.range / projectileStats.speed);
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
