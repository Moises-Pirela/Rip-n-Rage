using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponClasses/Weapon.cs (logic unchanged).
// Standalone addition: bloom — every shot widens the spread (WeaponStats.bloomPerShot, capped at spreadMax),
// it recovers while not firing (bloomRecovery), and moving / being airborne adds moveSpread / airSpread.
// CurrentSpread is what Fire() uses and what the crosshair shows.
public abstract class Weapon : MonoBehaviour
{   
    public WeaponData weaponData;
    protected LayerMask _LayerMask;

    protected float _bloom;
    public float Bloom => _bloom;

    /// <summary>Spread for the next shot: base + bloom + movement and air penalties (direction offset per unit forward).</summary>
    public float CurrentSpread
    {
        get
        {
            var st = weaponData.weaponStats;
            float s = st.weaponSpread + _bloom;
            float cap = Mathf.Max(st.spreadMax, st.weaponSpread);
            return st.spreadMax > 0f ? Mathf.Min(s, cap) : s;
        }
    }


    /// <summary>Call once per shot.</summary>
    protected void AddBloom()
    {
        var st = weaponData.weaponStats;
        float room = Mathf.Max(0f, st.spreadMax - st.weaponSpread);
        _bloom = Mathf.Min(_bloom + st.bloomPerShot, room);
    }

    protected override void Update()
    {
        if (_bloom > 0f) _bloom = Mathf.MoveTowards(_bloom, 0f, weaponData.weaponStats.bloomRecovery * Time.deltaTime);
    }

    protected bool CanFire = true;
    protected Coroutine AllowRefireCoroutine;

    protected RipNRage.Engine.Ecs.Animation.AnimatedSprite weaponAnimator;
    protected RipNRage.Engine.Ecs.Animation.AnimatedSprite leftHandAnimator;
    protected RipNRage.Engine.Ecs.Animation.AnimatedSprite rightHandAnimator;
    protected static readonly int IsFiring = RipNRage.Engine.Ecs.Animation.AnimatedSprite.StringToHash("IsFiring");
    protected static readonly int WeaponMelee = RipNRage.Engine.Ecs.Animation.AnimatedSprite.StringToHash("WeaponMelee");
    protected CameraRecoil _cameraRecoil;
    protected CombatController _combatController;
    protected HudController _hudController;
    
    protected override void Start()
    {
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
        _hudController =   EnragedInjector<HudController>.GetInstance();
    }

    public virtual void Equip(RipNRage.Engine.Ecs.Animation.AnimatedSprite animator, Transform transform, LayerMask layerMask, RipNRage.Engine.Ecs.Animation.AnimatedSprite leftHandAnimator, RipNRage.Engine.Ecs.Animation.AnimatedSprite rightHandAnimator = null)
    {
        weaponAnimator = animator;
        _LayerMask = layerMask;
        weaponAnimator.LocalPosition = weaponData.weaponPosition;
        weaponAnimator.LocalScale = Vector3.one * weaponData.weaponScale;
        this.leftHandAnimator = leftHandAnimator;
        this.rightHandAnimator = rightHandAnimator;
        weaponAnimator.SetBool(IsFiring, false);
        weaponAnimator.SetBool(WeaponMelee, false);
    }
    public abstract void Fire();
    public abstract void Melee();
    
    protected virtual void EndMelee()
    {
        _combatController.OnEndMeleeCombo();
        weaponAnimator.SetBool(WeaponMelee, false);
        weaponAnimator.SetActive(false);
    }
    
    protected virtual IEnumerator AllowRefire_Coroutine()
    {
        _combatController.CanFire = false;
        weaponAnimator.SetBool(IsFiring, true);
        _cameraRecoil.ApplyRecoil(weaponData.weaponRecoil);
        CanFire = false;
        yield return new WaitForSeconds(weaponData.weaponStats.fireRate);
        weaponAnimator.SetBool(IsFiring, false);
        CanFire = true;
        AllowRefireCoroutine = null;
        _combatController.CanFire = true;
    }
}
