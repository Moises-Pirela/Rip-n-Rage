using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class FPSCombat : MonoBehaviour
{
    [Header("Inventory")] 
    public InventoryComponent inventoryComponent;

    [Header("Weapon FX")] 
    [SerializeField] private Animator mainAnimator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject muzzleFlashSpriteObject;
    
    public Transform cameraTransform;
    public Transform muzzle;
    public LayerMask layerMask;
    
    private PlayerInputActions _inputActions;
    private static readonly int Fire = Animator.StringToHash("Fire");
    private static readonly int AltFire = Animator.StringToHash("AltFire");

    public bool CanFire;

    public UnityEngine.Events.UnityEvent OnPrimaryFireCallback;

    private void Start()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        _inputActions.OnPrimaryFire += OnPrimaryFire;
        _inputActions.OnSecondaryFire += OnSecondaryFire;
        
        EquipWeapon(0);
    }

    private void EquipWeapon(int index)
    {
        inventoryComponent.currentWeapon = inventoryComponent.weapons[index];
        mainAnimator.runtimeAnimatorController = inventoryComponent.currentWeapon.weaponData.AnimatorOverrideController;
        CanFire = true;
    }

    private void OnPrimaryFire()
    {
        if (!CanFire) return;
        
        mainAnimator.SetBool("New Bool", true);
        audioSource.PlayOneShot(inventoryComponent.currentWeapon.weaponData.fireSound);
        muzzleFlashSpriteObject.SetActive(true);
        CanFire = false;

        inventoryComponent.currentWeapon.Fire(cameraTransform.forward, cameraTransform.position,
            layerMask, cameraTransform.rotation, muzzle.position, OnPrimaryFireCallback);

        StartCoroutine(AllowRefire());
    }

    public IEnumerator AllowRefire()
    {
        yield return new WaitForSeconds(inventoryComponent.currentWeapon.weaponData.fireRate);
        muzzleFlashSpriteObject.SetActive(false);
        CanFire = true;
        mainAnimator.SetBool("New Bool", false);
    }

    private void OnSecondaryFire()
    {
        if (!CanFire) return;

        inventoryComponent.currentWeapon.AltFire();
        mainAnimator.SetTrigger(AltFire);
        CanFire = false;
    }
}
