using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CombatController : MonoBehaviour
{
    [Header("Weapon setup")] [SerializeField]
    private Transform cameraTransform;

    [SerializeField] private Transform projectileSpawnTransform;
    [SerializeField] private LayerMask layerMask;

    [Space(5)] [Header("Combat Animation")] [SerializeField]
    private Animator weaponAnimator;

    [SerializeField] private Animator leftFistAnimator;
    [SerializeField] private Animator rightFistAnimator;

    [Space(5)] [Header("Inventory")] [SerializeField]
    private Inventory inventory;

    private PlayerInputActions _playerInputActions;
    private int _comboStepCounter;
    private Coroutine _comboCoroutine;
    private Coroutine _allowJabCoroutine;
    private bool _canJab = true;
    private bool _canFire = true;

    private CameraRecoil _cameraRecoil;
    private static readonly int JabTrigger = Animator.StringToHash("Jab");
    private static readonly int Fire = Animator.StringToHash("Fire");

    private void Start()
    {
        _playerInputActions = EnragedInjector<PlayerInputActions>.GetInstance();
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
    }

    #region Input Events

    public void OnFire(InputValue inputValue)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;

        if (!inputValue.isPressed) return;

        if (_canFire)
        {
            Debug.Log("Fre");
            //inventory.equippedWeapon.Fire();
            //weaponAnimator.SetTrigger(Fire);
        }
    }

    public void OnAltFire(InputValue inputValue)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;

        if (!inputValue.isPressed) return;

        if (_comboStepCounter >= 3 || !_canJab) return;

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
            _comboStepCounter = 0;
            _canJab = true;
            _canFire = true;
            //_playerInputActions.SetIsBlockingInput(true);
            //inventory.equippedWeapon.Melee();    
        }
    }

    public void OnSwapWeapon(InputValue value)
    {
        if (_playerInputActions.GetIsBlockingInput()) return;

        int index = Mathf.FloorToInt(value.Get<float>());

        if (index == 0) return;

        index = Mathf.Clamp(index, -1, 1);

        inventory.SwapWeapon(index);
    }

    public void OnChangeWeapon1()
    {
        if (_playerInputActions.GetIsBlockingInput()) return;
        inventory.SwapWeapon(0);
    }

    public void OnChangeWeapon2()
    {
        if (_playerInputActions.GetIsBlockingInput()) return;
        inventory.SwapWeapon(1);
    }

    public void OnChangeWeapon3()
    {
        if (_playerInputActions.GetIsBlockingInput()) return;
        inventory.SwapWeapon(2);
    }

    #endregion

    private void Jab()
    {
        weaponAnimator.gameObject.SetActive(false);

        _canFire = false;

        if (_comboStepCounter == 1) leftFistAnimator.SetTrigger(JabTrigger);
        else if (_comboStepCounter == 2) rightFistAnimator.SetTrigger(JabTrigger);

        _canJab = false;
        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, 3, layerMask))
        {
            _cameraRecoil.Shake();
        }
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
    }

    #endregion
}