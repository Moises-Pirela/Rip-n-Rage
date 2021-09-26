using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private float projectileSpeed;

    [SerializeField] private GameObject impactPrefab;

    private void Awake()
    {
        Destroy(gameObject, 0.5f);
    }

    void Update()
    {
        transform.position += transform.forward * projectileSpeed * Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other)
    {
        if (other.collider.CompareTag("Player")) return;
        
        if (other.collider.CompareTag("FX")) return;

        //Instantiate(impactPrefab, other.collider.transform.position, Quaternion.identity);
        
        //Destroy(gameObject);
    }
}
