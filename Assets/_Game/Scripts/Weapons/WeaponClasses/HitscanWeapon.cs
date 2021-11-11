using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HitscanWeapon : Weapon
{
    public Transform cameraTransform;

    public override void Equip(Transform transform, LayerMask layerMask)
    {
        base.Equip(transform, layerMask);
        
        cameraTransform = transform;
    }

    public override void Fire()
    {
        if (!CanFire) return;
        
        if (AllowRefireCoroutine != null)
            StopCoroutine(AllowRefire_Coroutine());
        
        AllowRefireCoroutine = StartCoroutine(AllowRefire_Coroutine());
        
        int bulletsPerShot = WeaponData.bulletsPerShot;

        for (int i = 0; i < bulletsPerShot; i++)
        {
            if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, 100, _LayerMask))
            {
                Debug.Log("hit");
            }
        }
    }

    public override void Melee()
    {
        throw new System.NotImplementedException();
    }
}

