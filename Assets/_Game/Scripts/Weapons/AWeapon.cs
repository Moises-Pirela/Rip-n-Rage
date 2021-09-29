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
    private GameObject _muzzleFlash;
    
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

    public virtual void Fire(Vector3 direction, Vector3 origin, LayerMask layerMask, Quaternion projectileDirection, Vector3 projectileSpawn, UnityEvent Callback)
    {
        if (!bCanFire) return;
        
        Callback.Invoke();
        bCanFire = false;
        
        _mainAnimator.SetBool(IsFiring, true);
        _weaponAudioSource.PlayOneShot(weaponData.fireSound);
        _muzzleFlash.SetActive(true);
        
        Instantiate(weaponData.bulletProjectile, projectileSpawn, projectileDirection);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100, layerMask))
        {
            Instantiate(weaponData.bulletImpact, hit.point, Quaternion.LookRotation(hit.normal));
        }
        
        parentInventory.UseAmmo(1);
        
        StartCoroutine(AllowRefire(weaponData.fireRate));
    }
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
}
