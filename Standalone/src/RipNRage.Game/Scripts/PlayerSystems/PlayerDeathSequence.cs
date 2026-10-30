using System;
using System.Collections;
using RipNRage.Game.Content;

/// <summary>
/// Plays the player's death (standalone addition; the original project left <c>PlayerState.Die</c> empty):
///  1. input, combat and the look / head-bob / recoil controllers are switched off (the body still falls under gravity),
///  2. the camera drops to floor level and rolls over while the world slows down,
///  3. a thud is played and the <see cref="DeathScreen"/> fades in with RESTART / MAIN MENU.
/// Lives on the PlayerCharacter prefab; <see cref="PlayerState.Die"/> calls <see cref="Begin"/>.
/// </summary>
public class PlayerDeathSequence : MonoBehaviour
{
    /// <summary>The "Camera" pivot (moved by HeadBob, rotated by FPSCameraController).</summary>
    public Transform cameraPivot;
    /// <summary>The WorldCamera child (rotated by CameraRecoil).</summary>
    public Transform worldCamera;

    [Header("Fall")]
    public float fallDuration = 1.15f;
    /// <summary>Local height of the camera pivot at the end of the fall (the capsule's bottom is at -0.9).</summary>
    public float floorCameraHeight = -0.62f;
    public float rollAngle = 42f;
    public float extraPitch = 14f;
    /// <summary>Time scale the world settles at while the death screen is shown (0 would freeze enemies completely).</summary>
    public float slowMotion = 0.2f;

    public AudioClip deathSound;

    public bool IsRunning { get; private set; }

    public void Begin()
    {
        if (IsRunning) return;
        IsRunning = true;

        // 1. take the controls away: no look, no move, no shooting, no weapon on screen
        var input = GetComponent<PlayerInputActions>();
        if (input != null) input.enabled = false;
        GetComponent<CombatController>()?.DisableCombat();
        var look = GetComponent<FPSCameraController>();
        if (look != null) look.enabled = false;
        if (cameraPivot != null)
        {
            var bob = cameraPivot.GetComponent<HeadBob>();
            if (bob != null) bob.enabled = false;
            cameraPivot.DOKill(); // a grapple's ForceLookRotation tween must not fight the fall
        }
        if (worldCamera != null)
        {
            var recoil = worldCamera.GetComponent<CameraRecoil>();
            if (recoil != null) recoil.enabled = false;
        }

        // 2. sound
        var audio = GetComponent<AudioSource>();
        var clip = deathSound ?? ProceduralSounds.PlayerDeath;
        if (audio != null && clip != null) audio.PlayOneShot(clip, 1f);

        // 3. the screen starts fading while the camera falls
        DeathScreen.Show();
        StartCoroutine(FallRoutine());
    }

    private IEnumerator FallRoutine()
    {
        if (cameraPivot == null) yield break;

        Vector3 startPos = cameraPivot.localPosition;
        Quaternion startRot = cameraPivot.localRotation;
        Vector3 euler = startRot.eulerAngles;
        float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        float roll = Random.value < 0.5f ? -rollAngle : rollAngle;
        Quaternion endRot = Quaternion.Euler(Mathf.Clamp(pitch + extraPitch, -80f, 80f), euler.y, roll);
        Vector3 endPos = new Vector3(startPos.x, floorCameraHeight, startPos.z);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.05f, fallDuration);
            float k = Mathf.Clamp01(t);
            float fall = k * k;                                                       // accelerates like gravity
            float thud = k > 0.85f ? Mathf.Sin((k - 0.85f) / 0.15f * Mathf.PI) * 0.05f : 0f; // tiny bounce at the end
            cameraPivot.localPosition = Vector3.Lerp(startPos, endPos, fall) + Vector3.up * thud;
            cameraPivot.localRotation = Quaternion.Slerp(startRot, endRot, Mathf.SmoothStep(0f, 1f, k));
            Time.timeScale = Mathf.Lerp(1f, slowMotion, k);
            yield return null;
        }
        cameraPivot.localPosition = endPos;
        cameraPivot.localRotation = endRot;
        if (worldCamera != null) worldCamera.localRotation = Quaternion.identity; // drop any leftover recoil
        Time.timeScale = slowMotion;
    }
}
