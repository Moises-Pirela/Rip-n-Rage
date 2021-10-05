using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SonicProjectile : Projectile
{
    public float forceToAdd = 50;
    public override void OnCollisionEnter(Collision other)
    {
        Rigidbody rg = other.collider.GetComponent<Rigidbody>();
        
        if(rg)
            rg.AddForce(transform.forward * forceToAdd, ForceMode.Impulse);
        
        base.OnCollisionEnter(other);
        
    }
}
