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
    protected Animator leftHandAnimator;
    protected Animator rightHandAnimator;
    protected static readonly int IsFiring = Animator.StringToHash("IsFiring");
    protected static readonly int WeaponMelee = Animator.StringToHash("WeaponMelee");
    protected AudioSource _audioSource;
    protected CameraRecoil _cameraRecoil;
    protected CombatController _combatController;
    protected HudController _hudController;
    
    private void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
        _hudController =   EnragedInjector<HudController>.GetInstance();
    }

    public virtual void Equip(Animator animator, Transform transform, LayerMask layerMask, Animator leftHandAnimator, Animator rightHandAnimator = null)
    {
        weaponAnimator = animator;
        _LayerMask = layerMask;
        weaponAnimator.transform.localPosition = weaponData.weaponPosition;
        weaponAnimator.transform.localScale = Vector3.one * weaponData.weaponScale;
        this.leftHandAnimator = leftHandAnimator;
        this.rightHandAnimator = rightHandAnimator;
        weaponAnimator.SetBool(IsFiring, false);
        weaponAnimator.SetBool(WeaponMelee, false);
    }
    public abstract void Fire();
    public abstract void Melee();
    
    protected virtual void EndMelee()
    {
        _combatController.OnEndMeleeCombo();
        weaponAnimator.SetBool(WeaponMelee, false);
        weaponAnimator.gameObject.SetActive(false);
    }
    
    protected virtual IEnumerator AllowRefire_Coroutine()
    {
        _combatController.CanFire = false;
        weaponAnimator.SetBool(IsFiring, true);
        _cameraRecoil.ApplyRecoil(weaponData.weaponRecoil);
        CanFire = false;
        yield return new WaitForSeconds(weaponData.weaponStats.fireRate);
        weaponAnimator.SetBool(IsFiring, false);
        CanFire = true;
        AllowRefireCoroutine = null;
        _combatController.CanFire = true;
    }
}
