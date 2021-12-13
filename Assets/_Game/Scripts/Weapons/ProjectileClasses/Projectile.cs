using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
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

    private void Update()
    {
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

        if (entityState)
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
        GameObject vfx = Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized));
        Destroy(gameObject);
        Destroy(vfx, 0.4f);
    }

    protected Vector3 GetPointOfContact()
    {
        RaycastHit hit;
        
        return Physics.Raycast(transform.position, transform.forward, out hit) ? hit.point : Vector3.zero;
    }
}
