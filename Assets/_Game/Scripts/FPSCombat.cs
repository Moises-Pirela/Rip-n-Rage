using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FPSCombat : MonoBehaviour
{
    [Header("Inventory")] 
    public InventoryComponent inventoryComponent;

    [SerializeField] private Animator mainAnimator;
    
    public Transform cameraTransform;
    public LayerMask layerMask;
    
    private PlayerInputActions _inputActions;
    private static readonly int Fire = Animator.StringToHash("Fire");
    private static readonly int AltFire = Animator.StringToHash("AltFire");

    public bool CanFire;

    private void Start()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        _inputActions.OnPrimaryFire += OnPrimaryFire;
        _inputActions.OnSecondaryFire += OnSecondaryFire;
        
        EquipWeapon(0);
    }

    private void EquipWeapon(int index)
    {
        inventoryComponent.weapon = inventoryComponent.weapons[index];
        mainAnimator.runtimeAnimatorController = inventoryComponent.weapon.weaponData.AnimatorOverrideController;
        CanFire = true;
    }

    private void OnPrimaryFire()
    {
        if (!CanFire) return;

        StartCoroutine(inventoryComponent.weapon.Fire(cameraTransform.position, cameraTransform.forward, layerMask));
        mainAnimator.SetTrigger(Fire);
        CanFire = false;
    }

    private void OnSecondaryFire()
    {
        if (!CanFire) return;

        inventoryComponent.weapon.AltFire();
        mainAnimator.SetTrigger(AltFire);
        CanFire = false;
    }
}
