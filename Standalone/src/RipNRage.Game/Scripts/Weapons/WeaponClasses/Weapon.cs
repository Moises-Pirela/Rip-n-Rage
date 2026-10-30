using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Weapons/WeaponClasses/Weapon.cs (logic unchanged).
// Standalone addition: bloom — every shot widens the spread (WeaponStats.bloomPerShot, capped at spreadMax),
// it recovers while not firing (bloomRecovery), and moving / being airborne adds moveSpread / airSpread.
// CurrentSpread is what Fire() uses and what the crosshair shows.
[RequireComponent(typeof(AudioSource))]
public abstract class Weapon : MonoBehaviour
{   
    public WeaponData weaponData;
    protected LayerMask _LayerMask;

    protected float _bloom;
    private CharacterController _playerBody;
    public float Bloom => _bloom;

    /// <summary>Spread for the next shot: base + bloom + movement and air penalties (direction offset per unit forward).</summary>
    public float CurrentSpread
    {
        get
        {
            var st = weaponData.weaponStats;
            float s = st.weaponSpread + _bloom;
            var body = PlayerBody;
            if (body != null)
            {
                Vector3 v = body.velocity; v.y = 0f;
                s += st.moveSpread * Mathf.Clamp01(v.magnitude / 16f);
                if (!body.isGrounded) s += st.airSpread;
            }
            float cap = Mathf.Max(st.spreadMax, st.weaponSpread);
            return st.spreadMax > 0f ? Mathf.Min(s, cap) : s;
        }
    }

    private CharacterController PlayerBody => _playerBody ??= GetComponentInParent<CharacterController>();

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

    protected Animator weaponAnimator;
    protected Animator leftHandAnimator;
    protected Animator rightHandAnimator;
    protected static readonly int IsFiring = Animator.StringToHash("IsFiring");
    protected static readonly int WeaponMelee = Animator.StringToHash("WeaponMelee");
    protected AudioSource _audioSource;
    protected CameraRecoil _cameraRecoil;
    protected CombatController _combatController;
    protected HudController _hudController;
    
    protected override void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _cameraRecoil = EnragedInjector<CameraRecoil>.GetInstance();
        _combatController = EnragedInjector<CombatController>.GetInstance();
        _hudController =   EnragedInjector<HudController>.GetInstance();
    }

    public virtual void Equip(Animator animator, Transform transform, LayerMask layerMask, Animator leftHandAnimator, Animator rightHandAnimator = null)
    {
        weaponAnimator = animator;
        _LayerMask = layerMask;
        weaponAnimator.transform.localPosition = weaponData.weaponPosition;
        weaponAnimator.transform.localScale = Vector3.one * weaponData.weaponScale;
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
        weaponAnimator.gameObject.SetActive(false);
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
