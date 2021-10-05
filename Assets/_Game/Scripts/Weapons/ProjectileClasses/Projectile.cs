using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private float projectileSpeed;

    [SerializeField] private GameObject impactPrefab;

    [SerializeField] private bool shouldTickDestroy = true;

    [SerializeField] private LayerMask projectileLayerMask;

    private GameObject Instigator;

    private WeaponData owningWeapon;

    private bool setToDestroy = false;

    public virtual void Update()
    {
        transform.position += transform.forward * projectileSpeed * Time.deltaTime;
    }

    public virtual void SetUp(GameObject instigator, WeaponData weaponData)
    {
        Instigator = instigator;
        owningWeapon = weaponData;
        
        if (shouldTickDestroy)
            Destroy(gameObject, 0.5f);
    }
    public virtual void OnCollisionEnter(Collision other)
    {
        if (shouldTickDestroy) return;
        
        if (setToDestroy) return;

        ContactPoint hitPoint = other.GetContact(0);
        
        GameObject impact = Instantiate(impactPrefab, hitPoint.point,  Quaternion.LookRotation(hitPoint.normal));
        impact.transform.SetParent(other.transform);
        GameObject impactSound = Instantiate(Resources.Load("ImpactSound"), hitPoint.normal, quaternion.identity) as GameObject;
        impactSound.GetComponent<AudioSource>().PlayOneShot(owningWeapon.projectileImpactSound);
        Destroy(impactSound, 1f);
        Destroy(gameObject);
    }
}
