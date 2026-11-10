using System;
using RipNRage.Game.Ecs;

// MIGRATION FACADE (layer 4; gone in layer 9). The look itself is PlayerLookSystem on the player entity: it
// integrates the mouse / stick delta into the PlayerLook component's Yaw / Pitch / Roll, and PlayerLink aims the
// camera pivot and the Orientation child from them. What is left here is the surface the not-yet-ported scripts
// talk to: save / load reads and writes the view angles, the combat controller snaps the view to a grapple target,
// and the death sequence takes the look away.
public class FPSCameraController : MonoBehaviour
{
    public static FPSCameraController instance;

    [SerializeField] public Transform playerCamera;
    public Transform orientation;

    private PlayerLink _link;
    private PlayerLink Link => _link ??= GetComponent<PlayerLink>();
    private bool Has => Link != null && Link.Alive && Application.Current.World.Has<PlayerLook>(Link.entity);

    protected override void Awake() => instance = this;

    protected override void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public float Yaw => Has ? Link.Look.Yaw : 0f;
    public float Pitch => Has ? Link.Look.Pitch : 0f;

    /// <summary>Mouse sensitivity (the settings page and the prefab write these).</summary>
    public float sensitivity
    {
        get => Has ? Link.Look.Sensitivity : 5f;
        set { if (Has) Link.Look.Sensitivity = value; }
    }

    public float sensitivityMultiplier
    {
        get => Has ? Link.Look.SensitivityMultiplier : 1f;
        set { if (Has) Link.Look.SensitivityMultiplier = value; }
    }

    /// <summary>Extra roll requested by the movement (wall running); smoothed by the look system.</summary>
    public float externalRoll
    {
        get => Has ? Link.Look.ExternalRoll : 0f;
        set { if (Has) Link.Look.ExternalRoll = value; }
    }

    /// <summary>Save / load support: sets the view yaw / pitch directly.</summary>
    public void SetLook(float yaw, float pitch)
    {
        if (!Has) return;
        ref var look = ref Link.Look;
        look.Yaw = yaw;
        look.Pitch = Mathf.Clamp(pitch, -90f, 90f);
    }

    /// <summary>Snaps the view towards a world point (the grapple pull).</summary>
    public void ForceLookRotation(Vector3 lookTarget)
    {
        if (!Has) return;
        Vector3 dir = lookTarget - transform.position;
        if (dir.sqrMagnitude < 1E-06f) return;
        dir.Normalize();
        ref var look = ref Link.Look;
        look.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        look.Pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -90f, 90f);
    }
}
