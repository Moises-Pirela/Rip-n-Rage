using System;
using System.Collections;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

// Ported from Assets/_Game/Scripts/PlayerSystems/CombatController.cs.
// MIGRATION (layer 5.3): firing is WeaponFireSystem on the Weapon / Inventory components, and the Weapon class
// hierarchy with its prefab GameObjects is gone. What remains is the GameObject-side surface: weapon switching
// (PlayerInputActions and the automation scripts call the handlers), the fist jab combo, death, and thin
// component-backed accessors the HUD / save system read until layer 8.
public class CombatController : MonoBehaviour
{
    public Action OnEquipWeapon;
    public Action OnFireWeapon;
    public Action OnAmmoChanged;

    [Header("Weapon setup")]
    [SerializeField] public Transform cameraTransform;
    /// <summary>The prefab's "Weapon" GameObject node: ProjectileSpawn hangs off it, so equipping keeps its transform on the weapon's data.</summary>
    [SerializeField] public Transform weaponTransform;

    [Space(5)] [Header("Combat Animation")]
    [SerializeField] public RipNRage.Engine.Ecs.Animation.AnimatedSprite weaponAnimator;
    [SerializeField] public RipNRage.Engine.Ecs.Animation.AnimatedSprite leftFistAnimator;
    [SerializeField] public RipNRage.Engine.Ecs.Animation.AnimatedSprite rightFistAnimator;

    private PlayerInputActions _playerInputActions;
    /// <summary>Set by <see cref="DisableCombat"/> (player death): no input path may fire, jab or switch weapons any more.</summary>
    private bool _combatDisabled;
    public bool IsCombatDisabled => _combatDisabled;


    // ---------------------------------------------------------------- the components (the HUD / save surface)

    private static RipNRage.Engine.Ecs.World W => Application.Current.World;
    private ref Weapon Gun => ref W.Get<Weapon>(PlayerLink.PlayerEntity);
    private ref Inventory Inv => ref W.Get<Inventory>(PlayerLink.PlayerEntity);
    private ref AltFire Combo => ref W.Get<AltFire>(PlayerLink.PlayerEntity);

    public int CurrentWeaponIndex => Gun.Slot;
    public WeaponData CurrentWeaponData => Weapons.All.Length > 0 ? Weapons.Data(Gun.Slot) : null;
    /// <summary>Spread as the crosshair shows it (standing values; the fire system adds movement itself).</summary>
    public float CurrentSpread => Weapons.All.Length > 0 ? Weapons.CurrentSpread(in Gun, Vector3.zero, true) : 0f;
    public int AmmoOf(AmmoType type) => Inv.Ammo[(int)type];
    public int[] AmmoArray() => Weapons.AmmoArray(in Inv);
    public void LoadAmmo(int[] ammo) => Weapons.LoadAmmo(ref Inv, ammo);
    public bool AddAmmo(AmmoType type, int amount) => Weapons.AddAmmo(ref Inv, type, amount);

    protected override void Awake()
    {
        EnragedInjector<CombatController>.SetInstance(this);
    }

    protected override void Start()
    {
        _playerInputActions = EnragedInjector<PlayerInputActions>.GetInstance();
        LoadAmmo(SaveData.Current.inventoryData.ammo);
        EquipWeapon(0);
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
        // no combo or finisher may continue past death (the finisher entity would keep driving the gun)
        if (!Combo.Finisher.IsNull && W.IsAlive(Combo.Finisher) && !W.IsIterating) W.DestroyEntity(Combo.Finisher);
        Combo = default;
        Gun.Holstered = true;
        if (weaponAnimator != null) weaponAnimator.SetActive(false);
        if (leftFistAnimator != null) leftFistAnimator.SetActive(false);
        if (rightFistAnimator != null) rightFistAnimator.SetActive(false);
        if (cameraTransform != null) cameraTransform.DOKill();
        if (_playerInputActions != null) _playerInputActions.SetIsBlockingInput(true);
    }

    #region Input Events

    /// <summary>The automation scripts' alt-fire press; the real input is read by <see cref="AltFireSystem"/> itself.</summary>
    public void OnAltFire(bool isPressed)
    {
        if (_combatDisabled || !isPressed || _playerInputActions.GetIsBlockingInput()) return;
        Combo.Requested = true;
    }

    public void OnSwapWeapon(float value)
    {
        if (_combatDisabled) return;
        if (_playerInputActions.GetIsBlockingInput()) return;

        int step = value > 0f ? 1 : value < 0f ? -1 : 0;
        if (step == 0) return;
        int count = Weapons.All.Length;
        EquipWeapon((Gun.Slot + step + count) % count);
    }

    public void OnChangeWeapon1() => ChangeWeapon(0);
    public void OnChangeWeapon2() => ChangeWeapon(1);
    public void OnChangeWeapon3() => ChangeWeapon(2);
    public void OnChangeWeapon4() => ChangeWeapon(3);

    #endregion

    private int _lastWeaponIndex = -1;

    /// <summary>Quick swap (standalone addition): back to the weapon held before the current one.</summary>
    public void OnSwapToLastWeapon()
    {
        if (_lastWeaponIndex < 0 || _lastWeaponIndex == Gun.Slot) return;
        ChangeWeapon(_lastWeaponIndex);
    }

    /// <summary>Save/load support (standalone addition).</summary>
    public void EquipWeaponIndex(int index)
    {
        if (index >= 0 && index < Weapons.All.Length) EquipWeapon(index);
    }

    private void ChangeWeapon(int index)
    {
        if (_combatDisabled) return;
        if (_playerInputActions.GetIsBlockingInput()) return;
        if (index < 0 || index >= Weapons.All.Length) return;
        EquipWeapon(index);
    }

    private void EquipWeapon(int index)
    {
        ref var gun = ref Gun;
        if (index != gun.Slot) _lastWeaponIndex = gun.Slot;
        gun = new Weapon { Slot = index, Sprite = gun.Sprite }; // fresh fire state; the viewmodel entity stays

        var data = Weapons.Data(index);
        weaponAnimator.runtimeAnimatorController = data.animatorOverrideController;
        weaponAnimator.LocalPosition = data.weaponPosition;
        weaponAnimator.LocalScale = Vector3.one * data.weaponScale;
        if (weaponTransform != null)
        {
            weaponTransform.localPosition = data.weaponPosition;
            weaponTransform.localScale = Vector3.one * data.weaponScale;
        }

        OnEquipWeapon?.Invoke();
        OnAmmoChanged?.Invoke();
    }

}
