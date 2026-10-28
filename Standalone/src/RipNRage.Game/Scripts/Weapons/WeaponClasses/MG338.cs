using System.Collections;
using System.Collections.Generic;

public class MG338 : HitscanWeapon
{
    [SerializeField] public AudioClip meleeClip;
    public override void Melee()
    {
        _audioSource.PlayOneShot(meleeClip);
        weaponAnimator.gameObject.SetActive(true);
        weaponAnimator.SetBool(WeaponMelee, true);
        StartCoroutine(WaitMelee());
    }

    private IEnumerator WaitMelee()
    {
        yield return new WaitForSeconds(0.2f);
       EndMelee();
    }
}
