using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class FPSCombat : MonoBehaviour, IProjectileSpawner
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
    private Coroutine MeleeComboTime;
    private bool isInCombo = false;
    private int comboStep = 0;

    public bool CanFire;

    public UnityEngine.Events.UnityEvent OnPrimaryFireCallback;
    public Action OnProjectileHit {get;set;}
    
    private void Start()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        _inputActions.OnPrimaryFire += OnPrimaryFire;
        _inputActions.OnSecondaryFire += OnSecondaryFire;
        _inputActions.OnSwapWheel += SwapWeaponWheel;
        _inputActions.OnSwapButton += SwapWeapon;
        
        inventoryComponent.SetupInventory(gameObject);
        
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
        //if (inventoryComponent.currentWeaponIndex == index) return;

        OnProjectileHit -= OnProjectileHit_Implementation;
        
        inventoryComponent.EquipWeapon(index);
        inventoryComponent.currentWeapon.Equip(audioSource, mainAnimator, muzzleFlashSpriteObject, inventoryComponent);

        if (inventoryComponent.currentWeapon is ProjectileWeapon)
        {
            OnProjectileHit += OnProjectileHit_Implementation;
        }
    }
    private void OnPrimaryFire()
    {
        inventoryComponent.currentWeapon.Fire(cameraTransform.forward, cameraTransform.position,
            layerMask, cameraTransform.rotation, muzzle.position, OnPrimaryFireCallback);
    }
    private void OnSecondaryFire()
    {
        comboStep = Mathf.Clamp(comboStep + 1, 0,3);

        switch(comboStep)
        {
            case 1:
            Debug.Log("Move 1");
            break;
            case 2:
            Debug.Log("Move 2");
            break;
            case 3:
            inventoryComponent.currentWeapon.MeleeCombo();
            break;    
        }

        if (MeleeComboTime != null)
            StopCoroutine(MeleeComboTime);


         if (comboStep < 3)   
            MeleeComboTime = StartCoroutine(ExpireMeleeCombo());
            else
                EndCombo();


        // inventoryComponent.currentWeapon.AltFire(cameraTransform.forward, cameraTransform.position,
        //     layerMask, cameraTransform.rotation, muzzle.position, OnPrimaryFireCallback);
    }

    private void EndCombo()
    {
        Debug.Log("End combo");
        comboStep = 0;
        isInCombo = false;
    }

    private IEnumerator ExpireMeleeCombo()
    {
            isInCombo = true;
            yield return new WaitForSeconds(1f);
            EndCombo();
    }
    private void SwapWeaponWheel(int index)
    {
        int newIndex = inventoryComponent.SwapWeapon(index);
        
        EquipWeapon(newIndex);
    }
    
    private void SwapWeapon(int index)
    {
        EquipWeapon(index);
    }

    public void OnProjectileHit_Implementation()
    {
        PlayerState.Current.AddToRage(0.05f);
        PlayerState.Current.SetViolentCombatState();
    }
    
}
