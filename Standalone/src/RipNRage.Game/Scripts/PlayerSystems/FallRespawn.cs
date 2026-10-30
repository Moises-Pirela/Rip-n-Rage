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

    private CharacterController _cc;
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

    protected override void Start()
    {
        _cc = GetComponent<CharacterController>();
        _fps = GetComponent<FPSController>();
        _input = GetComponent<PlayerInputActions>();
        _state = GetComponent<PlayerState>();
        _lastGroundedPos = _dropPos = transform.position;
    }

    protected override void Update()
    {
        if (_fade > 0f) _fade -= Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeDuration);
        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (_cc == null || (_state != null && _state.IsDead)) return;

        // a position is only trustworthy while standing normally: mid-slide/wall-run/dash the capsule shape or
        // velocity are temporarily abnormal, so that instant must not be remembered as "safe ground".
        bool normalStance = _fps == null || (!_fps.IsSliding && !_fps.IsWallRunning && !_fps.IsDashing);
        bool grounded = _cc.isGrounded && (_input == null || _input.IsGrounded);
        float now = Time.unscaledTime;
        if (grounded)
        {
            _lastGroundedPos = transform.position;
            _dropRecorded = false;
            if (normalStance)
            {
                _groundedTime += Time.deltaTime;
                if (_groundedTime >= safeDelay && now - _lastSampleTime >= historyInterval && transform.position.y > KillY + 2f && Supported(transform.position))
                {
                    _history.Add(new Sample { pos = transform.position, yaw = FPSCameraController.instance != null ? FPSCameraController.instance.Yaw : 0f, time = now });
                    _lastSampleTime = now;
                    while (_history.Count > 0 && now - _history[0].time > historySeconds) _history.RemoveAt(0);
                }
            }
            else _groundedTime = 0f;
        }
        else
        {
            _groundedTime = 0f;
            // the spot where the player first is clearly below the level they last stood on: for a walk or jump
            // off an edge that is beyond the edge, for a fall through the floor it is inside / under the floor
            if (!_dropRecorded && transform.position.y < _lastGroundedPos.y - 0.5f) { _dropPos = transform.position; _dropTime = now; _dropRecorded = true; }
        }

        if (transform.position.y < KillY && _cooldown <= 0f) OnFell();
    }

    // ------------------------------------------------------------------ the fall

    private void OnFell()
    {
        Falls++;
        _cooldown = 1f;
        Classify();
        if (Mode == FallMode.Die) Drown(); else Respawn();
    }

    /// <summary>
    /// Edge or bug? Probes straight down at the spot where the player first dropped clearly below the level they
    /// last stood on. Floor at that standing level right there means they were standing on something and went
    /// through it; no floor means they walked, jumped or were knocked off an edge.
    /// </summary>
    private void Classify()
    {
        float fallTime = Time.unscaledTime - _dropTime;
        if (!_dropRecorded)
        {
            LastFallKind = "UNKNOWN";
            LastFallReport = $"Fall #{Falls}: fell below y={KillY:0.0} without ever standing on ground (spawned or teleported in the air?). Last grounded at {F(_lastGroundedPos)}.";
        }
        else
        {
            Vector3 origin = new Vector3(_dropPos.x, _lastGroundedPos.y + 2f, _dropPos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f + 1.5f, GroundMask))
            {
                LastFallKind = "THROUGH GEOMETRY";
                LastFallReport = $"Fall #{Falls}: FELL THROUGH GEOMETRY — dropped through '{hit.collider?.gameObject?.name}' (top at y={hit.point.y:0.00}) at {F(_dropPos)}, {fallTime:0.0}s before reaching the kill height. Last grounded at {F(_lastGroundedPos)}. This is a collision bug: please report it with these numbers.";
            }
            else
            {
                LastFallKind = "EDGE";
                LastFallReport = $"Fall #{Falls}: went off an edge — at {F(_dropPos)} there is no floor at the level last stood on ({F(_lastGroundedPos)}); reached the kill height y={KillY:0.0} {fallTime:0.0}s later.";
            }
        }
        Debug.LogWarning(LastFallReport);
    }

    private static string F(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

    /// <summary>Lethal fall: freeze the body where it is and run the normal death sequence.</summary>
    private void Drown()
    {
        if (_fps != null) { _fps.ResetVelocity(); _fps.enabled = false; }
        bool edge = LastFallKind == "EDGE";
        GameSaveSystem.OnMessage?.Invoke(edge ? "You fell into the harbour." : "You fell out of the world.");
        DeathScreen.NextCause = edge ? "YOU FELL INTO THE HARBOUR" : "YOU FELL OUT OF THE WORLD";
        if (_state != null && !_state.IsDead) _state.Die();
    }

    /// <summary>Arcade respawn: back to a recorded, footprint-supported spot from before the fall — or the level start if those keep failing.</summary>
    public void Respawn()
    {
        _groundedTime = 0f;
        float now = Time.unscaledTime;
        _recentFallTimes.RemoveAll(t => now - t > repeatFallWindow);
        _recentFallTimes.Add(now);
        bool loopDetected = _recentFallTimes.Count > repeatFallLimit;

        Vector3 basePos = SpawnPosition;
        float yaw = SpawnRotation.eulerAngles.y;
        bool fromHistory = false;
        if (!loopDetected)
        {
            // newest sample that is old enough and still has ground under every corner of the footprint
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var s = _history[i];
                if (now - s.time < rewindSeconds) continue;
                if (!Supported(s.pos)) continue;
                basePos = s.pos; yaw = s.yaw; fromHistory = true;
                break;
            }
        }
        if (!fromHistory)
        {
            if (loopDetected) Debug.LogWarning($"Player fell {_recentFallTimes.Count} times in {repeatFallWindow:0}s — the recorded spots are not trusted any more, using the level start");
            _recentFallTimes.Clear();
            _history.Clear();
        }

        Vector3 target = basePos + Vector3.up * respawnHeight;
        _cc.Warp(target);
        _fps?.ResetVelocity();
        var look = FPSCameraController.instance;
        if (look != null) look.SetLook(yaw, look.Pitch);
        _fade = 1f;
        _lastGroundedPos = _dropPos = target; _dropRecorded = false;
        Debug.LogWarning($"Player respawned at {F(target)} ({(fromHistory ? "recorded spot" : "level start")})");
        GameSaveSystem.OnMessage?.Invoke(LastFallKind == "EDGE" ? "You fell off. Back on your feet." : "You fell out of the world. Back on your feet.");
    }

    /// <summary>True when every corner of the capsule's footprint has ground within <see cref="supportDepth"/> under the feet.</summary>
    private bool Supported(Vector3 center)
    {
        if (_cc == null) return true;
        float r = Mathf.Max(0.05f, _cc.radius - 0.1f);
        float half = Mathf.Max(_cc.height, _cc.radius * 2f) * 0.5f;
        float reach = half + supportDepth;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = center + _cc.center + new Vector3((i & 1) == 0 ? -r : r, 0f, (i & 2) == 0 ? -r : r);
            if (!Physics.Raycast(corner, Vector3.down, reach, GroundMask)) return false;
        }
        return true;
    }

    public void DrawUi(UiBatch ui)
    {
        if (_fade <= 0f) return;
        float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_fade));
        ui.FillRect(new Rect(0, 0, ui.Width, ui.Height), new Color(0f, 0f, 0f, a));
    }
}
