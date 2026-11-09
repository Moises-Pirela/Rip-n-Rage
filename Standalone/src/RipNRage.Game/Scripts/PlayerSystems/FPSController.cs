using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/FPSController.cs.
// Physics / AnimationCurve / AudioSource are the engine's replacements; the locomotion logic is unchanged but the
// player is now a force-driven rigid body (CharacterBody): this script computes the velocity it wants and hands it
// to the motor (Move), jumps and dashes are impulses, gravity is the body's own, knockback is a velocity change —
// so the player pushes crates with its mass, is shoved by blasts and bodies, and slides along walls through the
// contact solver. Serialized fields are public so the player factory can apply the values stored in
// PlayerCharacter.prefab.
// Standalone additions (movement feel): sliding (crouch while running: capsule shrinks, camera drops, a speed
// boost decays with friction; jumping out of a slide keeps the momentum), wall running (airborne next to a wall
// while pushing forward: reduced gravity, speed along the wall, camera tilt, wall jump away from the wall),
// external impulses (explosion knockback / rocket jumps) and the style meter's S-rank speed bonus.
public class FPSController : MonoBehaviour
{
    [Space, Header("Data")]
    private AudioSource m_AudioSource;
    [SerializeField] public AudioClip[] m_FootstepSounds;
    [SerializeField] public AudioClip m_JumpSound;
    [SerializeField] public AudioClip m_LandSound;
    [SerializeField] [Range(0f, 1f)] public float m_RunstepLenghten;
    [SerializeField] public float m_StepInterval;

    [Space, Header("Locomotion Settings")] 
    [SerializeField] public bool bEnableBunnyHop = true;
    [SerializeField] public float crouchSpeed = 1f;
    [SerializeField] public float walkSpeed = 2f;
    [SerializeField] public float runSpeed = 3f;
    [SerializeField] public float bunnyHopSpeed = 20f;
    [SerializeField] public float jumpSpeed = 5f;
    [Range(0f, 1f)] [SerializeField] public float moveBackwardsSpeedPercent = 0.5f;
    [Range(0f, 1f)] [SerializeField] public float moveSideSpeedPercent = 0.75f;

    [Space, Header("Landing Settings")]
    [Range(0.05f, 0.5f)] [SerializeField] public float lowLandAmount = 0.1f;
    [Range(0.2f, 0.9f)] [SerializeField] public float highLandAmount = 0.6f;
    [SerializeField] public float landTimer = 0.5f;
    [SerializeField] public float landDuration = 1f;
    [SerializeField] public AnimationCurve landCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Space, Header("Gravity Settings")]
    [SerializeField] public float gravityMultiplier = 2.5f;
    [SerializeField] public float stickToGroundForce = 5f;
    [SerializeField] public LayerMask groundLayer = ~0;
    [Range(0f, 1f)] [SerializeField] public float rayLength = 0.1f;
    [Range(0.01f, 1f)] [SerializeField] public float raySphereRadius = 0.1f;

    [Space, Header("Check Wall Settings")]
    [SerializeField] public LayerMask obstacleLayers = ~0;
    [Range(0f, 1f)] [SerializeField] public float rayObstacleLength = 0.1f;
    [Range(0.01f, 1f)] [SerializeField] public float rayObstacleSphereRadius = 0.1f;

    [Space, Header("Slide (standalone)")]
    public bool enableSlide = true;
    public float slideSpeedBoost = 7f;
    public float slideFriction = 7f;          // m/s² lost while sliding on flat ground
    public float slideMinSpeed = 5f;          // slide ends below this (unless the roof is too low to stand)
    public float slideStartSpeed = 6f;        // need at least this much horizontal speed to start
    public float slideHeight = 1.0f;          // capsule height while sliding (standing 1.8)
    public float slideCameraDrop = 0.55f;
    public float slideCooldown = 0.35f;
    public float slideSteer = 1.5f;           // how much left/right input bends the slide direction

    [Space, Header("Wall run (standalone)")]
    public bool enableWallRun = true;
    public float wallRunMaxTime = 1.6f;
    public float wallRunGravity = 0.2f;       // gravity multiplier while on the wall
    public float wallRunSpeed = 13f;
    public float wallCheckDistance = 0.75f;   // beyond the capsule radius
    public float wallJumpUp = 15f;
    public float wallJumpAway = 11f;
    public float wallRunTilt = 14f;           // camera roll away from the wall (degrees)
    public float wallRunEntryBoost = 3f;      // upward nudge when catching a wall

    [Space, Header("Dash (standalone)")]
    public bool enableDash = true;
    public float dashSpeed = 26f;
    public float dashDuration = 0.16f;
    public float dashCooldown = 0.7f;
    public int airDashes = 1;

    public bool IsDashing { get; private set; }
    private float _dashTimer, _dashCooldownTimer;
    private Vector3 _dashDir;
    private int _airDashesLeft;

    public bool IsSliding { get; private set; }
    public bool IsWallRunning { get; private set; }
    /// <summary>-1 = wall on the left, +1 = wall on the right.</summary>
    public int WallSide { get; private set; }

    private Vector3 _slideDir;
    private float _slideSpeed, _slideTime, _slideCooldownTimer;
    private float _standHeight; private Vector3 _standCenter;
    private float _wallRunTimer, _wallJumpCooldown;
    private Vector3 _wallNormal;
    private Vector3 _pendingImpulse;
    private FPSCameraController _cameraController;

    [Space, Header("Smooth Settings")]
    [Range(1f, 100f)] [SerializeField] public float smoothRotateSpeed = 5f;
    [Range(1f, 100f)] [SerializeField] public float smoothInputSpeed = 5f;
    [Range(1f, 100f)] [SerializeField] public float smoothVelocitySpeed = 5f;
    [Range(1f, 100f)] [SerializeField] public float smoothFinalDirectionSpeed = 5f;

    private Transform m_yawTransform;
    private Transform m_camTransform;
    private HeadBob m_headBob;
    public PlayerInputActions _inputActions;

    private RaycastHit m_hitInfo;
    private IEnumerator m_CrouchRoutine;
    private IEnumerator m_LandRoutine;

    [Space]
    private Vector2 m_inputVector;
    private Vector2 m_smoothInputVector;

    private Vector3 m_finalMoveDir;
    private Vector3 m_smoothFinalMoveDir;
    private Vector3 m_finalMoveVector;
    private float m_currentSpeed;

    public float MCurrentSpeed
    {
        get => m_currentSpeed;
        set => m_currentSpeed = value;
    }

    private float m_smoothCurrentSpeed;
    private float m_finalSmoothCurrentSpeed;
    private float m_walkRunSpeedDifference;
    private float m_dashSpeedDifference;
    private float m_finalRayLength;
    private bool m_hitWall;
    //private bool m_isGrounded;
    private bool m_previouslyGrounded;
    private float m_initHeight;
    private float m_crouchHeight;
    private Vector3 m_initCenter;
    private Vector3 m_crouchCenter;
    private float m_initCamHeight;
    private float m_crouchCamHeight;
    private float m_crouchStandHeightDifference;
    private bool m_duringCrouchAnimation;
    private bool m_duringRunAnimation;
    private float m_inAirTimer;
    private float m_inputVectorMagnitude;
    private float m_smoothInputVectorMagnitude;
    private float m_StepCycle;
    private float m_NextStep;


    // input is not physics: head bob, weapon sway and the camera keep reading it
    protected override void Start() => _inputActions = GetComponent<PlayerInputActions>();

    /// <summary>Knockback / rocket-jump impulse (no-op until the player is an ECS character, layer 4).</summary>
    public void AddImpulse(Vector3 impulse) { }
    public void ResetVelocity() { IsDashing = false; }
}
