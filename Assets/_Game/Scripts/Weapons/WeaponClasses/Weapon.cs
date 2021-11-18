using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public abstract class Weapon : MonoBehaviour
{   
    public WeaponData weaponData;
    protected LayerMask _LayerMask;

    protected bool CanFire = true;
    protected Coroutine AllowRefireCoroutine;

    protected Animator weaponAnimator;
    protected static readonly int IsFiring = Animator.StringToHash("IsFiring");
    protected AudioSource _audioSource;
    protected CameraRecoil _cameraRecoil;
    protected CombatController _combatController;

    private void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
    }

    public virtual void Equip(Animator animator, Transform transform, LayerMask layerMask)
    {
        weaponAnimator = animator;
        _LayerMask = layerMask;
        weaponAnimator.transform.localPosition = weaponData.weaponPosition;
    }
    public abstract void Fire();
    public abstract void Melee();
    
    protected virtual IEnumerator AllowRefire_Coroutine()
    {
        weaponAnimator.SetBool(IsFiring, true);
        _cameraRecoil.ApplyRecoil(weaponData.weaponRecoil);
        CanFire = false;
        yield return new WaitForSeconds(weaponData.weaponStats.fireRate);
        weaponAnimator.SetBool(IsFiring, false);
        CanFire = true;
        AllowRefireCoroutine = null;
    }
}
