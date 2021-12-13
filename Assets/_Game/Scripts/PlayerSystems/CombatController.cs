using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CombatController : MonoBehaviour
{
    public Action OnEquipWeapon;
    public Action OnFireWeapon;
    public Action OnAmmoChanged;

    [Header("Weapon setup")] 
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform projectileSpawnTransform;
    [SerializeField] private LayerMask layerMask;

    [Space(5)] [Header("Combat Animation")] 
    [SerializeField] private Animator weaponAnimator;
    [SerializeField] private Animator leftFistAnimator;
    [SerializeField] private Animator rightFistAnimator;
    [SerializeField] private AudioClip jabLandSound;
    [SerializeField] private AudioSource jabSource;
    

    [Space(5)] [Header("Inventory")]
    public Inventory inventory;

    private PlayerInputActions _playerInputActions;
    private int _comboStepCounter;
    private Coroutine _comboCoroutine;
    private Coroutine _allowJabCoroutine;
    private bool _canJab = true;
    private bool _canFire = true;
    public bool CanFire
    {
        get => _canFire;
        set => _canFire = value;
    }
    private bool _isFiring = false;

    private CameraRecoil _cameraRecoil;
    private static readonly int JabTrigger = Animator.StringToHash("Jab");

    private void Awake()
    {
        EnragedInjector<CombatController>.SetInstance(this);
    }

    private void Start()
    {
        _playerInputActions = EnragedInjector<PlayerInputActions>.GetInstance();
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();

        foreach (var weaponPrefab in inventory.allWeaponPrefabs)
        {
            var prefab = Instantiate(weaponPrefab, this.transform);
            Weapon weaponClass = prefab.GetComponent<Weapon>();

            if (weaponClass.weaponData.weaponStats.isUnlocked)
            {
                inventory.availableWeapons.Add(weaponClass);
            }
        }
        
        inventory.Init();
        
        EquipWeapon(0);
        weaponAnimator.runtimeAnimatorController = inventory.equippedWeapon.weaponData.animatorOverrideController;
    }

    private void Update()
    {
        if (_playerInputActions.IsFiring)
        {
            //Debug.Log("Fire");
            Fire();
        }
    }

    #region Input Events

    private void Fire()
    {
        if (_canFire)
        {
            inventory.equippedWeapon.Fire();
            OnFireWeapon?.Invoke();
            OnAmmoChanged?.Invoke();
        }
    }

    public void OnAltFire(InputValue inputValue)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;

        if (!inputValue.isPressed) return;

        if (_comboStepCounter >= 3 || !_canJab) return;
        
        PlayerState.Current.SetViolentCombatState();

        _comboStepCounter++;

        if (_comboStepCounter < 3)
        {
            Jab();

            if (_comboCoroutine != null)
                StopCoroutine(_comboCoroutine);

            if (_allowJabCoroutine != null)
                StopCoroutine(_allowJabCoroutine);

            _allowJabCoroutine = StartCoroutine(AllowJab_Coroutine());
            _comboCoroutine = StartCoroutine(ResetJabCounter_Coroutine());
        }
        else
        {
            if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, 1, layerMask))
            {
                var enemy = hit.collider.GetComponent<AIState>();

                if (!enemy)
                {
                    OnEndMeleeCombo();
                    return;
                }

                FPSCameraController.instance.ForceLookRotation(enemy.grapplePoint.position);
                //_playerInputActions.SetIsBlockingInput(true);
                inventory.equippedWeapon.Melee();
            }
            else
            {
                OnEndMeleeCombo();
            }    
        }
    }

    public void OnSwapWeapon(InputValue value)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;

        int index = Mathf.FloorToInt(value.Get<float>());

        if (index == 0) return;

        index = Mathf.Clamp(index, -1, 1);

        int newIndex = inventory.SwapWeapon(index);
        
        EquipWeapon(newIndex);
    }

    public void OnChangeWeapon1()
    {
        ChangeWeapon(0);
    }

    public void OnChangeWeapon2()
    {
       ChangeWeapon(1);
    }

    public void OnChangeWeapon3()
    {
       ChangeWeapon(2);
    }

    #endregion

    public void OnEndMeleeCombo()
    {
        _comboStepCounter = 0;
        _canJab = true;
        _canFire = true;
        _playerInputActions.SetIsBlockingInput(false);
    }

    private void Jab()
    {
        weaponAnimator.gameObject.SetActive(false);

        _canFire = false;

        if (_comboStepCounter == 1) leftFistAnimator.SetTrigger(JabTrigger);
        else if (_comboStepCounter == 2) rightFistAnimator.SetTrigger(JabTrigger);

        _canJab = false;
        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, 1, layerMask))
        {
            _cameraRecoil.Shake();
            jabSource.PlayOneShot(jabLandSound);
        }
    }

    private void ChangeWeapon(int index)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;
        EquipWeapon(index);
    }

    private void EquipWeapon(int index)
    {
        inventory.equippedWeapon = inventory.availableWeapons[index];

        Transform neededTransform = cameraTransform;

        if (inventory.equippedWeapon is ProjectileWeapon)
            neededTransform = projectileSpawnTransform;
        
        inventory.equippedWeapon.Equip(weaponAnimator,neededTransform, layerMask, leftFistAnimator);
        
        weaponAnimator.runtimeAnimatorController = inventory.equippedWeapon.weaponData.animatorOverrideController;

        OnEquipWeapon?.Invoke();
        OnAmmoChanged?.Invoke();
    }

    #region Coroutines

    private IEnumerator AllowJab_Coroutine()
    {
        yield return new WaitForSeconds(0.005f);
        _canJab = true;
        _allowJabCoroutine = null;
    }

    private IEnumerator ResetJabCounter_Coroutine()
    {
        yield return new WaitForSeconds(0.75f);
        _comboStepCounter = 0;
        _comboCoroutine = null;
        weaponAnimator.gameObject.SetActive(true);
        _canFire = true;
    }

    #endregion
}