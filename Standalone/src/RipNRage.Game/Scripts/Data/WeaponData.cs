using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Data/WeaponData.cs. The Unity asset references became engine
// types: AnimatorOverrideController -> AnimatorController, GameObject prefab -> Prefab.
// Instances (Glock, M1887, MG338, RocketLauncher) are defined in Content/GameAssets.cs with the
// exact values of the .asset files.
[CreateAssetMenu(menuName = "ENRAGED/Weapon", fileName = "Weapon")]
public class WeaponData : ScriptableObject
{
    public AmmoType ammoType;
    public WeaponType weaponType;
    
    [Header("Weapon stats")]
    public WeaponRecoil weaponRecoil;
    public WeaponStats weaponStats;
    
    [Space(10)]
    [Header("Weapon FX")]
    public AnimatorController animatorOverrideController;
    public AudioClip fireSound;
    public Prefab spawnableVFX;
    public Vector3 weaponPosition;
    public float weaponScale;
}
