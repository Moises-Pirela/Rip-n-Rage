using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class AWeapon : MonoBehaviour
{
    public WeaponData weaponData;

    public virtual IEnumerator Fire(Vector3 direction, Vector3 origin, LayerMask layerMask)
    {
        for (int i = 0; i < weaponData.roundsPerFire; i++)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, 100,layerMask))
            {
            }

            yield return new WaitForSeconds(weaponData.fireRate);
        }
    }

    public abstract void AltFire();
}
