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
    /// <summary>The "Camera" pivot GameObject (follows the pivot entity; kept for the scripts that read it).</summary>
    public Transform cameraPivot;
    /// <summary>The WorldCamera child GameObject.</summary>
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
        // The view rig is entities now: take the pivot away from PlayerViewSystem (so the look angles and the head
        // bob stop writing it), stop the bob, and drop any kick / trauma still on the camera. Disabling
        // PlayerInputActions above already blocked the input the look integrates, so the angles are frozen too.
        RipNRage.Game.Ecs.PlayerCamera.SetExternal(true);
        StopBob();
        RipNRage.Game.Ecs.PlayerCamera.ClearShake();
        if (cameraPivot != null) cameraPivot.DOKill(); // a grapple's ForceLookRotation tween must not fight the fall

        // 2. sound
        var clip = deathSound ?? ProceduralSounds.PlayerDeath;
        if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 1f);

        // 3. the screen starts fading while the camera falls
        DeathScreen.Show();
        StartCoroutine(FallRoutine());
    }

    /// <summary>Freezes the head bob: it owns the pivot's vertical position while it is active.</summary>
    private void StopBob()
    {
        var w = Application.Current?.World;
        var pivot = RipNRage.Game.Ecs.PlayerLink.PivotEntity;
        if (w == null || !w.IsAlive(pivot) || !w.Has<RipNRage.Engine.Ecs.View.ViewBob>(pivot)) return;
        w.Get<RipNRage.Engine.Ecs.View.ViewBob>(pivot).Active = false;
    }

    /// <summary>The pivot entity's local pose, which the fall animates (the GameObject node mirrors it).</summary>
    private static bool PivotPose(out RipNRage.Engine.Ecs.World w, out RipNRage.Engine.Ecs.Entity pivot)
    {
        w = Application.Current?.World;
        pivot = RipNRage.Game.Ecs.PlayerLink.PivotEntity;
        return w != null && w.IsAlive(pivot) && w.Has<RipNRage.Engine.Ecs.Transforms.LocalTransform>(pivot);
    }

    private IEnumerator FallRoutine()
    {
        if (!PivotPose(out var world, out var pivotEntity)) yield break;

        Vector3 startPos = world.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(pivotEntity).Position;
        Quaternion startRot = world.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(pivotEntity).Rotation;
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
            SetPivot(Vector3.Lerp(startPos, endPos, fall) + Vector3.up * thud, Quaternion.Slerp(startRot, endRot, Mathf.SmoothStep(0f, 1f, k)));
            Time.timeScale = Mathf.Lerp(1f, slowMotion, k);
            yield return null;
        }
        SetPivot(endPos, endRot);
        RipNRage.Game.Ecs.PlayerCamera.ClearShake(); // drop any leftover recoil on the camera child
        Time.timeScale = slowMotion;
    }

    /// <summary>Writes the pivot entity's local pose (the fall owns it while <c>PlayerViewRig.External</c> is set).</summary>
    private static void SetPivot(Vector3 position, Quaternion rotation)
    {
        if (!PivotPose(out var w, out var pivot)) return;
        ref var lt = ref w.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(pivot);
        lt.Position = position;
        lt.Rotation = rotation;
    }
}
