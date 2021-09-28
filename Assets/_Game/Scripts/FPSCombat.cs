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
    public Transform weaponHolder;
    public LayerMask layerMask;
    
    private PlayerInputActions _inputActions;
    private static readonly int Fire = Animator.StringToHash("Fire");
    private static readonly int AltFire = Animator.StringToHash("AltFire");

    public bool CanFire;

    public UnityEngine.Events.UnityEvent OnPrimaryFireCallback;
    

    private void Awake()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        _inputActions.OnPrimaryFire += OnPrimaryFire;
        _inputActions.OnSecondaryFire += OnSecondaryFire;
        
        inventoryComponent.SetupInventory();
        
        PrepareWeapons();
        
        EquipWeapon(0);
    }

    private void PrepareWeapons()
    {
        AWeapon[] weapons = inventoryComponent.weapons;
        for (int i = 0; i < weapons.Length; i++)
        {
            AWeapon weaponToSpawn = Instantiate(weapons[i],weaponHolder);
            
            inventoryComponent.availableWeapons.Add(weaponToSpawn);
        }
    }

    private void EquipWeapon(int index)
    {
        inventoryComponent.EquipWeapon(index);
        inventoryComponent.currentWeapon.Equip(audioSource, mainAnimator, muzzleFlashSpriteObject, inventoryComponent);
    }

    private void OnPrimaryFire()
    {
        inventoryComponent.currentWeapon.Fire(cameraTransform.forward, cameraTransform.position,
            layerMask, cameraTransform.rotation, muzzle.position, OnPrimaryFireCallback);
    }
    
    private void OnSecondaryFire()
    {
        inventoryComponent.currentWeapon.AltFire(cameraTransform.forward, cameraTransform.position,
            layerMask, cameraTransform.rotation, muzzle.position, OnPrimaryFireCallback);
        mainAnimator.SetTrigger(AltFire);
    }
}
