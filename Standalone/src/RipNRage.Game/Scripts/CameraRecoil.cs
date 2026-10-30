using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/CameraRecoil.cs.
// Standalone additions: ApplyRecoil is implemented (the original left it commented out) — a weapon kick
// (pitch up + random yaw/roll, per WeaponRecoil) — and a "trauma" shake on top: explosions nearby and heavy
// weapons add trauma that decays over time and drives a smooth noise on the camera's rotation and position
// (shake = trauma², so small hits are subtle and big ones violent).
public class CameraRecoil : MonoBehaviour
{
    private float rotationSpeed = 6f;
    private float returnSpeed = 25f;
    
    [Header("Camera Shake:")]
    public Vector3 recoilCameraShake = new Vector3(0.5f, 0.5f, 1.5f);

    [Header("Trauma shake (explosions, heavy weapons)")]
    public float traumaDecay = 1.5f;        // trauma lost per second
    public float maxShakeAngle = 5f;        // degrees at full trauma
    public float maxShakeOffset = 0.1f;     // metres at full trauma
    public float shakeFrequency = 18f;
    /// <summary>Scale applied to WeaponRecoil.recoil.x (the data was authored for Unity's additive model; 12 → 3 degrees).</summary>
    public float kickScale = 0.25f;

    private Vector3 currentRotation;
    
    private Vector3 Rot;
    private float _trauma;
    private float _seed;
    private Vector3 _basePos;

    public float Trauma => _trauma;

    protected override void Awake()
    {
        EnragedInjector<CameraRecoil>.SetInstance(this);
        _seed = Random.Range(0f, 100f);
        _basePos = transform.localPosition;
    }

    protected override void FixedUpdate()
    {   
        currentRotation = Vector3.Lerp(currentRotation, Vector3.zero, returnSpeed * Time.deltaTime);
        Rot = Vector3.Slerp(Rot, currentRotation, rotationSpeed * Time.deltaTime);
    }

    protected override void LateUpdate()
    {
        _trauma = Mathf.Max(0f, _trauma - traumaDecay * Time.deltaTime);
        float sh = _trauma * _trauma;
        Vector3 shakeRot = Vector3.zero, shakeOff = Vector3.zero;
        if (sh > 0.0001f)
        {
            float t = Time.time * shakeFrequency;
            shakeRot = new Vector3(Noise(t, _seed) * maxShakeAngle * sh, Noise(t, _seed + 11f) * maxShakeAngle * sh, Noise(t, _seed + 23f) * maxShakeAngle * 1.5f * sh);
            shakeOff = new Vector3(Noise(t, _seed + 37f) * maxShakeOffset * sh, Noise(t, _seed + 41f) * maxShakeOffset * sh, 0f);
        }
        transform.localRotation = Quaternion.Euler(Rot + shakeRot);
        transform.localPosition = _basePos + shakeOff;
    }

    /// <summary>Smooth -1..1 noise (sum of incommensurate sines) so the shake never looks like a vibration.</summary>
    private static float Noise(float t, float seed) =>
        (Mathf.Sin(t * 1.0f + seed * 7.3f) + Mathf.Sin(t * 2.31f + seed * 3.1f) * 0.5f + Mathf.Sin(t * 0.47f + seed * 1.7f) * 0.35f) / 1.85f;

    /// <summary>Adds shake energy (0..1); effects add, big explosions saturate.</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));

    /// <summary>Shake from a world-space event: full <paramref name="strength"/> at the centre, quadratic falloff to zero at <paramref name="radius"/>.</summary>
    public void ShakeAt(Vector3 position, float radius, float strength)
    {
        float d = Vector3.Distance(transform.position, position);
        float k = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, radius));
        AddTrauma(strength * k * k);
    }

    /// <summary>Static convenience for gameplay code (explosions): shakes the player camera if there is one.</summary>
    public static void ShakeEverywhere(Vector3 position, float radius, float strength)
    {
        var r = EnragedInjector<CameraRecoil>.GetInstance();
        if (r != null && !r.IsDestroyed) r.ShakeAt(position, radius, strength);
    }
    
    public void Shake(float strength = 15)
    {
        var yRotation = Random.Range(-recoilCameraShake.y, recoilCameraShake.y) * strength;
        var zRotation = Random.Range(-recoilCameraShake.z, recoilCameraShake.z) * strength;
        currentRotation += new Vector3(-recoilCameraShake.x, yRotation, zRotation);
    }

    /// <summary>Weapon kick: pitch up by recoil.x (scaled), random yaw / roll within recoil.y / .z, plus trauma for heavy weapons.</summary>
    public void ApplyRecoil(WeaponRecoil currentWeapon)
    {
        var recoil = currentWeapon.recoil;
        if (currentWeapon.recoilSpeed > 0f) rotationSpeed = currentWeapon.recoilSpeed;
        if (currentWeapon.recoilRecoverySpeed > 0f) returnSpeed = currentWeapon.recoilRecoverySpeed;
        float yRotation = Random.Range(-recoil.y, recoil.y) * kickScale;
        float zRotation = Random.Range(-recoil.z, recoil.z) * kickScale;
        currentRotation += new Vector3(-recoil.x * kickScale, yRotation, zRotation);
        AddTrauma(currentWeapon.cameraShake);
    }
}
