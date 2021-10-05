using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

public class SonicShotgun : ProjectileWeapon
{
    bool isCharged = false;
    Coroutine ChargeCoroutine;
    bool isChargingShot = false;

    Vector3 projectilePos;
    Quaternion projectileDir;

    public override void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback)
    {
        projectileDir = projectileDirection;
        projectilePos = projectileSpawn;

        if (isChargingShot) return;

        ChargeCoroutine = StartCoroutine(ChargeShot());

        
    }

    public override void CancelAltFire()
    {
        StopCoroutine(ChargeCoroutine);

        if (isCharged)
        {
            isCharged = false;
            _mainAnimator.SetTrigger("AltFire");
            GameObject proj =  SpawnProjectile(projectilePos, projectileDir);
            proj.transform.localScale = Vector3.one * 2;
        }

            isChargingShot = false;
    }

    private IEnumerator ChargeShot()
    {
        isChargingShot = true;

        yield return new WaitForSeconds(2f);

        Debug.Log("Is Charged");

        isCharged = true;
    }
}
