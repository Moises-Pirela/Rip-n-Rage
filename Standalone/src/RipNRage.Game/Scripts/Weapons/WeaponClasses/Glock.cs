using System.Collections;
using System.Collections.Generic;

public class Glock : HitscanWeapon
{
    protected static readonly int MeleeTrigger = RipNRage.Engine.Ecs.Animation.AnimatedSprite.StringToHash("Grapple");
    
    public override void Melee()
    {
        StartCoroutine(BeginMelee());
    }

    private IEnumerator BeginMelee()
    {
        leftHandAnimator.SetBool(MeleeTrigger, true);
        yield return new WaitForSeconds(0.25f);
        weaponAnimator.SetActive(true);
        Fire();
        yield return new WaitUntil(()=>CanFire);
        Fire();
        yield return new WaitForSeconds(0.25f);
        EndMelee();
    }

    protected override void EndMelee()
    {
        leftHandAnimator.SetBool(MeleeTrigger, false);
        base.EndMelee();
    }
}
