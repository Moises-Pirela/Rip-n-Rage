using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

// MIGRATION (layer 5.3, static since 9.4): firing is WeaponFireSystem on the Weapon / Inventory components. What
// remains is weapon switching (WeaponSelectSystem and the automation scripts call the handlers), the viewmodel
// equip, and thin component-backed accessors the save system and the scripts read. OOP shell gone with the
// GameObject it hung off.
public static class CombatController
{
    /// <summary>The weapon viewmodel sprite (an entity wrapper): the equip retargets its animator and pose.</summary>
    private static RipNRage.Engine.Ecs.Animation.AnimatedSprite _weaponSprite;

    /// <summary>Only a Playing run may jab or switch weapons; firing itself is gated by the input, which the flow blocks.</summary>
    private static bool Live => Flows.Playing;

    private static RipNRage.Engine.Ecs.World W => Application.Current.World;
    private static ref Weapon Gun => ref W.Get<Weapon>(PlayerLink.PlayerEntity);
    private static ref Inventory Inv => ref W.Get<Inventory>(PlayerLink.PlayerEntity);
    private static ref AltFire Combo => ref W.Get<AltFire>(PlayerLink.PlayerEntity);

    public static int CurrentWeaponIndex => Gun.Slot;
    public static WeaponData CurrentWeaponData => Weapons.All.Length > 0 ? Weapons.Data(Gun.Slot) : null;
    public static int AmmoOf(AmmoType type) => Inv.Ammo[(int)type];
    public static int[] AmmoArray() => Weapons.AmmoArray(in Inv);
    public static void LoadAmmo(int[] ammo) => Weapons.LoadAmmo(ref Inv, ammo);

    /// <summary>The player was built: the new-game ammo, weapon 0 (a load overwrites both the frame after, 8.5).</summary>
    public static void Bind(RipNRage.Engine.Ecs.Animation.AnimatedSprite weaponSprite)
    {
        _weaponSprite = weaponSprite;
        _lastWeaponIndex = -1;
        LoadAmmo(Weapons.NewGameAmmo);
        EquipWeapon(0);
    }

    /// <summary>The automation scripts' alt-fire press; the real input is read by <see cref="AltFireSystem"/> itself.</summary>
    public static void OnAltFire(bool isPressed)
    {
        if (!Live || !isPressed) return;
        Combo.Requested = true;
    }

    public static void OnSwapWeapon(float value)
    {
        if (!Live) return;
        int step = value > 0f ? 1 : value < 0f ? -1 : 0;
        if (step == 0) return;
        int count = Weapons.All.Length;
        EquipWeapon((Gun.Slot + step + count) % count);
    }

    public static void OnChangeWeapon1() => ChangeWeapon(0);
    public static void OnChangeWeapon2() => ChangeWeapon(1);
    public static void OnChangeWeapon3() => ChangeWeapon(2);
    public static void OnChangeWeapon4() => ChangeWeapon(3);

    private static int _lastWeaponIndex = -1;

    /// <summary>Quick swap (standalone addition): back to the weapon held before the current one.</summary>
    public static void OnSwapToLastWeapon()
    {
        if (_lastWeaponIndex < 0 || _lastWeaponIndex == Gun.Slot) return;
        ChangeWeapon(_lastWeaponIndex);
    }

    /// <summary>Save/load support (standalone addition).</summary>
    public static void EquipWeaponIndex(int index)
    {
        if (index >= 0 && index < Weapons.All.Length) EquipWeapon(index);
    }

    private static void ChangeWeapon(int index)
    {
        if (!Live) return;
        if (index < 0 || index >= Weapons.All.Length) return;
        EquipWeapon(index);
    }

    private static void EquipWeapon(int index)
    {
        ref var gun = ref Gun;
        if (index != gun.Slot) _lastWeaponIndex = gun.Slot;
        gun = new Weapon { Slot = index, Sprite = gun.Sprite }; // fresh fire state; the viewmodel entity stays

        var data = Weapons.Data(index);
        if (_weaponSprite != null)
        {
            _weaponSprite.runtimeAnimatorController = data.animatorOverrideController;
            _weaponSprite.LocalPosition = data.weaponPosition;
            _weaponSprite.LocalScale = Vector3.one * data.weaponScale;
        }
        // where projectiles leave, in camera space (9.4: was the ProjectileSpawn GameObject under the weapon node)
        WeaponFireSystem.ProjectileSpawnLocal = ViewModel.ArmsHome + data.weaponPosition + data.weaponScale * Vector3.right;
    }
}

namespace RipNRage.Game.Ecs
{
    /// <summary>The weapon-switch keys (9.4: was the PlayerInputActions MonoBehaviour's dispatch): reads the gameplay
    /// scheme's edges and equips through <see cref="CombatController"/>.</summary>
    [RipNRage.Engine.Ecs.Exclusive] // the input device, and the viewmodel equip
    public sealed class WeaponSelectSystem : RipNRage.Engine.Ecs.SystemBase, RipNRage.Engine.Ecs.ITick
    {
        public void Tick(RipNRage.Engine.Ecs.SystemContext ctx)
        {
            if (!PlayerLink.Alive || !ctx.World.GetSingleton<RipNRage.Engine.Ecs.Input.InputState>().Live) return;
            var G = InputSystem.Gameplay;
            if (G["weapon_1"].Pressed) CombatController.OnChangeWeapon1();
            if (G["weapon_2"].Pressed) CombatController.OnChangeWeapon2();
            if (G["weapon_3"].Pressed) CombatController.OnChangeWeapon3();
            if (G["weapon_4"].Pressed) CombatController.OnChangeWeapon4();
            if (G["weapon_next"].Pressed) CombatController.OnSwapWeapon(1f);
            else if (G["weapon_prev"].Pressed) CombatController.OnSwapWeapon(-1f);
            if (G["weapon_last"].Pressed) CombatController.OnSwapToLastWeapon();
        }
    }
}
