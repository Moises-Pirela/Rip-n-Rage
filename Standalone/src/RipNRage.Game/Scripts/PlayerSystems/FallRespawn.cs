using System;
using System.Collections.Generic;
using RipNRage.Game.Content.Level;

/// <summary>What happens when the player drops below the level's kill height.</summary>
public enum FallMode
{
    /// <summary>The fall is lethal: the normal death sequence plays and RESTART puts the player back at the last checkpoint / the level start.</summary>
    Die,
    /// <summary>Arcade style: the player is put back on a spot they stood on a moment before the fall, no damage.</summary>
    Respawn,
}

/// <summary>
/// Handles falling off the level (standalone addition). The kill height comes from the level: worldspawn
/// <c>kill_y</c>, else one metre under the water plane, else well below the lowest brush.
///
/// <see cref="FallMode.Die"/> (default, worldspawn <c>fall_mode die</c>): dropping into the harbour kills the
/// player — the body stops where it is, the camera falls, "YOU ARE DEAD" fades in and RESTART reloads the
/// last checkpoint or the level start, exactly like any other death.
///
/// <see cref="FallMode.Respawn"/> (worldspawn <c>fall_mode respawn</c> or <c>--fall-mode respawn</c>): the
/// player is put back, with no damage and a short black fade, on a position they stood on at least
/// <see cref="rewindSeconds"/> before the fall whose whole footprint has solid ground under it — never the
/// very last grounded frame, which for a walk off an edge is the brink itself (being put back on the brink
/// while still holding the key was the "endless fall loop" of the first version). If the player keeps falling
/// (more than <see cref="repeatFallLimit"/> falls in <see cref="repeatFallWindow"/> seconds) the guaranteed
/// level start is used instead.
///
/// Every fall is classified and logged so a real collision bug cannot hide behind a walk off the edge: the
/// position where the controller lost ground is probed straight down — floor right there means the player
/// went <b>through</b> geometry, no floor means they stepped <b>off an edge</b>. The F3 overlay's MOVER line
/// shows the last verdict.
/// </summary>
public class FallRespawn : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Overlay;

    /// <summary>World Y below which the player has fallen out of the level (set by the level loader / TestArea).</summary>
    public static float KillY = -50f;
    /// <summary>Guaranteed-good fallback position/rotation (the level's info_player_start).</summary>
    public static Vector3 SpawnPosition = new Vector3(0f, 2.6f, 8f);
    public static Quaternion SpawnRotation = Quaternion.identity;
    /// <summary>Lethal fall (default) or arcade respawn. Set from the command line (<c>--fall-mode</c>) and overridden by the level's worldspawn <c>fall_mode</c>.</summary>
    public static FallMode Mode = FallMode.Die;

    /// <summary>Seconds of standing normally on the ground before positions start being recorded as safe.</summary>
    public float safeDelay = 0.35f;
    /// <summary>Spacing of the recorded positions.</summary>
    public float historyInterval = 0.25f;
    /// <summary>How far back the record reaches.</summary>
    public float historySeconds = 5f;
    /// <summary>A respawn uses a position at least this old — the last frames before a fall are by definition next to the drop.</summary>
    public float rewindSeconds = 1.0f;
    /// <summary>How far under the feet the footprint probes look for ground.</summary>
    public float supportDepth = 1.0f;
    public float respawnHeight = 0.35f;
    public float fadeDuration = 0.7f;
    /// <summary>Falling again within this many seconds of a respawn means the recorded spot was not actually safe.</summary>
    public float repeatFallWindow = 8f;
    /// <summary>Repeated falls inside that window before giving up on the record and using the level start.</summary>
    public int repeatFallLimit = 2;

    public int Falls { get; private set; }
    /// <summary>Short verdict of the last fall for the overlay ("EDGE" / "THROUGH GEOMETRY").</summary>
    public string LastFallKind { get; private set; } = "-";
    /// <summary>Full report of the last fall (also written to the log).</summary>
    public string LastFallReport { get; private set; } = "";

    /// <summary>What counts as ground for the probes: level geometry only — never the player's own capsule, enemies, projectiles or pickups.</summary>
    private const int GroundMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision);

    private struct Sample { public Vector3 pos; public float yaw; public float time; }

    private FPSController _fps;
    private PlayerInputActions _input;
    private PlayerState _state;
    private readonly List<Sample> _history = new List<Sample>();
    private readonly List<float> _recentFallTimes = new List<float>();
    private float _groundedTime;
    private float _lastSampleTime = -10f;
    private Vector3 _lastGroundedPos;
    private Vector3 _dropPos;
    private float _dropTime;
    private bool _dropRecorded;
    private float _fade;
    private float _cooldown;

    /// <summary>Respawn needs the player character body (layer 4).</summary>
    public void Respawn() { }
    public void DrawUi(UiBatch ui)
    {
        if (_fade <= 0f) return;
        float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_fade));
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, a));
    }
}
