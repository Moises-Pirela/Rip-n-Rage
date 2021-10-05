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

    void Update()
    {
        transform.position += transform.forward * projectileSpeed * Time.deltaTime;
    }

    public void SetUp(GameObject instigator, WeaponData weaponData)
    {
        Instigator = instigator;
        owningWeapon = weaponData;
        
        if (shouldTickDestroy)
            Destroy(gameObject, 0.5f);
    }
    private void OnCollisionEnter(Collision other)
    {
        if (shouldTickDestroy) return;

        ContactPoint hitPoint = other.GetContact(0);
        
        Instantiate(impactPrefab, transform.position,  Quaternion.LookRotation(hitPoint.normal));
        GameObject impactSound = Instantiate(Resources.Load("ImpactSound"), hitPoint.point, quaternion.identity) as GameObject;
        impactSound.GetComponent<AudioSource>().PlayOneShot(owningWeapon.projectileImpactSound);
        Destroy(impactSound, 1f);
        Destroy(gameObject);
    }
}
