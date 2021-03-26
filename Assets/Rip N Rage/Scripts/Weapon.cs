using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;


public enum AmmoType { PISTOL, SHOTGUN, LAUNCHER }
public abstract class Weapon : MonoBehaviour
{
    public WeaponData weaponData;
    public System.Action onHitTarget;
    public RuntimeAnimatorController animator;
    public AudioClip gunShot;
    public AudioSource primaryAudioSource;
    public AudioSource secondaryAudioSource;

    public virtual void Start()
    {
        onHitTarget += RageManager.instance.OnHitTarget;
    }

    public abstract void Equip(Transform camera);
    public abstract void Fire();
    public abstract void AltFire();

    public virtual void ApplyRecoil(Transform playerCam)
    {
        playerCam.parent.DOShakeRotation(weaponData.recoilDuration, weaponData.recoilPattern, weaponData.recoilStrength, 0, false);
    }
}
