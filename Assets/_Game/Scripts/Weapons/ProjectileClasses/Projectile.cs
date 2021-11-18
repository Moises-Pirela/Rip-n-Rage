using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public ProjectileStats projectileStats;
    private Transform _transform;

    private EntityState _instigator;

    public void SetupProjectile(EntityState entity)
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

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject == _instigator.gameObject) return;
        
        EntityState entityState = other.gameObject.GetComponent<EntityState>();

        if (entityState)
        {
            if (entityState is AIState) return;
            
            entityState.TakeDamage(projectileStats.damage);
            Debug.Log(entityState.name);
        }
        else
        {
            var contactPoint = GetPointOfContact();
            Debug.Log(other.name);
            Instantiate(projectileStats.hitVFX, contactPoint, Quaternion.LookRotation(contactPoint.normalized));
        }
        Destroy(gameObject);
    }
    
    private Vector3 GetPointOfContact()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit))
        {
            return hit.point;
        }

        return Vector3.zero;
    }
}
