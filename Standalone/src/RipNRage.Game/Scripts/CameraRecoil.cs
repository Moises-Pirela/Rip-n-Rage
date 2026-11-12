using RipNRage.Engine.Ecs.View;
using RipNRage.Game.Ecs;

// MIGRATION FACADE (layer 4; gone in layer 9). The shake itself is the CameraShake component on the player's camera
// entity, applied by CameraShakeSystem: the weapon kick (pitch up + random yaw / roll, per WeaponRecoil) and the
// "trauma" shake on top (explosions and heavy weapons add trauma, which decays and drives smooth noise on the
// camera's rotation and position; shake = trauma², so small hits are subtle and big ones violent).
//
// What is left here is the surface the not-yet-ported scripts talk to: the weapons apply recoil, the explosions shake
// everything, PlayerState reads the trauma, and the death sequence switches it off.
public class CameraRecoil : MonoBehaviour
{
    /// <summary>Weapon kick bounds (pitch, yaw, roll) used by <see cref="Shake"/>.</summary>
    public Vector3 recoilCameraShake = new Vector3(0.5f, 0.5f, 1.5f);

    /// <summary>Scale applied to WeaponRecoil.recoil.x (the data was authored for Unity's additive model; 12 → 3 degrees).</summary>
    public float kickScale = 0.25f;

    public float Trauma => PlayerCamera.Has ? PlayerCamera.Shake.Trauma : 0f;

    protected override void Awake() => EnragedInjector<CameraRecoil>.SetInstance(this);

    /// <summary>Adds shake energy (0..1); effects add, big explosions saturate.</summary>
    public void AddTrauma(float amount) { if (PlayerCamera.Has) PlayerCamera.Shake.AddTrauma(amount); }

    /// <summary>Shake from a world-space event: full <paramref name="strength"/> at the centre, quadratic falloff to zero at <paramref name="radius"/>.</summary>
    public void ShakeAt(Vector3 position, float radius, float strength)
    {
        if (PlayerCamera.Has) PlayerCamera.Shake.ShakeAt(PlayerCamera.Position, position, radius, strength);
    }

    /// <summary>Static convenience for gameplay code (explosions): shakes the player camera if there is one.</summary>
    public static void ShakeEverywhere(Vector3 position, float radius, float strength)
    {
        var r = EnragedInjector<CameraRecoil>.GetInstance();
        if (r != null && !r.IsDestroyed) r.ShakeAt(position, radius, strength);
    }

    /// <summary>A plain kick with no weapon data behind it (the grapple, scripted hits).</summary>
    public void Shake(float strength = 15)
    {
        if (!PlayerCamera.Has) return;
        PlayerCamera.Shake.Kick(recoilCameraShake.x, recoilCameraShake.y * strength, recoilCameraShake.z * strength);
    }

    /// <summary>Weapon kick: pitch up by recoil.x (scaled), random yaw / roll within recoil.y / .z, plus trauma for heavy weapons.</summary>
    public void ApplyRecoil(WeaponRecoil currentWeapon)
    {
        if (!PlayerCamera.Has) return;
        ref var shake = ref PlayerCamera.Shake;
        var recoil = currentWeapon.recoil;
        if (currentWeapon.recoilSpeed > 0f) shake.RotationSpeed = currentWeapon.recoilSpeed;
        if (currentWeapon.recoilRecoverySpeed > 0f) shake.ReturnSpeed = currentWeapon.recoilRecoverySpeed;
        shake.Kick(recoil.x * kickScale, recoil.y * kickScale, recoil.z * kickScale);
        shake.AddTrauma(currentWeapon.cameraShake);
    }

    /// <summary>Drops the kick and the trauma (the death sequence takes the camera over).</summary>
    public void Clear() { if (PlayerCamera.Has) PlayerCamera.Shake.Clear(); }
}
