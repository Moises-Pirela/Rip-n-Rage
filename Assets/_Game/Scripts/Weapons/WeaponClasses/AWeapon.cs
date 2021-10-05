using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;

public abstract class AWeapon : MonoBehaviour
{
    [HideInInspector] public InventoryComponent parentInventory;
    public WeaponData weaponData;

    protected AudioSource _weaponAudioSource;
    protected Animator _mainAnimator;
    protected GameObject _muzzleFlash;
    
    protected static readonly int IsFiring = Animator.StringToHash("IsFiring");

    protected bool bCanFire;
    protected bool bCanAltFire;

    public void Equip(AudioSource audioSource, Animator animator, GameObject muzzleFlash, InventoryComponent inventoryComponent)
    {
        _weaponAudioSource = audioSource;
        _mainAnimator = animator;
        _muzzleFlash = muzzleFlash;
        parentInventory = inventoryComponent;
        
        _mainAnimator.runtimeAnimatorController = weaponData.AnimatorOverrideController;
        
        bCanFire = true;
        bCanAltFire = true;
    }

    public abstract void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection,
        Vector3 projectileSpawn, UnityEvent Callback);
    
    protected IEnumerator AllowRefire(float timer)
    {
        yield return new WaitForSeconds(timer);
        _muzzleFlash.SetActive(false);
        bCanFire = true;
        _mainAnimator.SetBool(IsFiring, false);
    }

    protected IEnumerator AllowAltRefire()
    {
        yield return new WaitForSeconds(weaponData.altRefireRate);
        bCanAltFire = true;
    }


    public abstract void AltFire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback);

    public abstract void CancelAltFire();
}
