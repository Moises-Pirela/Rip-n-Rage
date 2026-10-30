using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/PlayerSystems/CombatController.cs.
// Input System callbacks (InputValue) are now plain parameters dispatched by PlayerInputActions.
public class CombatController : MonoBehaviour
{
    public Action OnEquipWeapon;
    public Action OnFireWeapon;
    public Action OnAmmoChanged;

    [Header("Weapon setup")] 
    [SerializeField] public Transform cameraTransform;
    [SerializeField] public Transform projectileSpawnTransform;
    [SerializeField] public LayerMask layerMask;

    [Space(5)] [Header("Combat Animation")] 
    [SerializeField] public Animator weaponAnimator;
    [SerializeField] public Animator leftFistAnimator;
    [SerializeField] public Animator rightFistAnimator;
    [SerializeField] public AudioClip jabLandSound;
    [SerializeField] public AudioSource jabSource;
    

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
    /// <summary>Set by <see cref="DisableCombat"/> (player death): no input path may fire, jab or switch weapons any more.</summary>
    private bool _combatDisabled;
    public bool IsCombatDisabled => _combatDisabled;

    private CameraRecoil _cameraRecoil;
    private static readonly int JabTrigger = Animator.StringToHash("Jab");

    protected override void Awake()
    {
        EnragedInjector<CombatController>.SetInstance(this);
    }

    protected override void Start()
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

    protected override void Update()
    {
        if (_combatDisabled) return;

        if (_playerInputActions.IsFiring)
        {
            //Debug.Log("Fire");
            Fire();
        }
    }

    /// <summary>
    /// Player death (standalone addition): stops every combat coroutine, blocks firing / jabbing /
    /// weapon switching for good and hides the first-person weapon and fists.
    /// </summary>
    public void DisableCombat()
    {
        if (_combatDisabled) return;
        _combatDisabled = true;
        StopAllCoroutines();
        _comboCoroutine = null;
        _allowJabCoroutine = null;
        _comboStepCounter = 0;
        _canFire = false;
        _canJab = false;
        if (weaponAnimator != null) weaponAnimator.gameObject.SetActive(false);
        if (leftFistAnimator != null) leftFistAnimator.gameObject.SetActive(false);
        if (rightFistAnimator != null) rightFistAnimator.gameObject.SetActive(false);
        if (cameraTransform != null) cameraTransform.DOKill();
        if (_playerInputActions != null) _playerInputActions.SetIsBlockingInput(true);
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

    public void OnAltFire(bool isPressed)
    {
        if (_combatDisabled) return;
        if (_playerInputActions.GetIsBlockingInput()) return;

        if (!isPressed) return;

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

                if (enemy == null)
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

    public void OnSwapWeapon(float value)
    {
        if (_combatDisabled) return;
        if (_playerInputActions.GetIsBlockingInput()) return;

        int index = Mathf.FloorToInt(value);
        if (index == 0 && value != 0f) index = value > 0f ? 1 : -1;

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

    public void OnChangeWeapon4()
    {
       ChangeWeapon(3);
    }

    #endregion

    public void OnEndMeleeCombo()
    {
        if (_combatDisabled) return; // a weapon's melee animation ending after death must not re-arm anything
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

    private int _lastWeaponIndex = -1;

    /// <summary>Quick swap (standalone addition): back to the weapon held before the current one.</summary>
    public void OnSwapToLastWeapon()
    {
        if (_lastWeaponIndex < 0 || _lastWeaponIndex == inventory.currentWeaponIndex) return;
        ChangeWeapon(_lastWeaponIndex);
    }

    /// <summary>Save/load support (standalone addition).</summary>
    public void EquipWeaponIndex(int index)
    {
        if (index >= 0 && index < inventory.availableWeapons.Count) EquipWeapon(index);
    }

    private void ChangeWeapon(int index)
    {
        if (_combatDisabled) return;
        if (_playerInputActions.GetIsBlockingInput()) return;
        if (index < 0 || index >= inventory.availableWeapons.Count) return;
        EquipWeapon(index);
    }

    private void EquipWeapon(int index)
    {
        if (inventory.equippedWeapon != null && index != inventory.currentWeaponIndex) _lastWeaponIndex = inventory.currentWeaponIndex;
        inventory.equippedWeapon = inventory.availableWeapons[index];
        inventory.currentWeaponIndex = index;

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
