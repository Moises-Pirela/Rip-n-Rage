using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class DoubleBarrelGlock : Weapon
{
    Transform playerCam;

    [SerializeField] private ParticleSystem[] bullets;

    public override void Equip(Transform camera)
    {
        playerCam = camera;
    }
    public override void Fire()
    {
        Shoot();
    }
    public override void AltFire()
    {
        StartCoroutine(BeginAltFire());
    }

    private void Shoot()
    {
        primaryAudioSource.PlayOneShot(gunShot);
        secondaryAudioSource.PlayOneShot(gunShot);

        if (Physics.Raycast(playerCam.transform.position + new Vector3(0.02f,0,0) , playerCam.transform.forward, out RaycastHit hit, weaponData.range))
        {
           TryToApplyDamage(hit);
           
        }
        if (Physics.Raycast(playerCam.transform.position + new Vector3(-0.02f,0,0), playerCam.transform.forward,  out RaycastHit hit1, weaponData.range))
        {
            TryToApplyDamage(hit1);
        }

        ApplyRecoil(playerCam);

        bullets[0].Play();
        bullets[1].Play();
    }

    private IEnumerator BeginAltFire()
    {
        Shoot();

        yield return new WaitForSeconds(0.2f);

        Shoot();
    }

    private void TryToApplyDamage(RaycastHit hit)
    {
        IDamageable target = hit.collider.GetComponent<IDamageable>();

        if (target == null) return;

        target.TakeDamage(weaponData.damagePerBullet);
        
        if (onHitTarget != null)
            onHitTarget();
    }
}
