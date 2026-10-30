using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/FPSController.cs.
// CharacterController / Physics / AnimationCurve / AudioSource are the engine's replacements;
// the locomotion logic is unchanged. Serialized fields are public so the player factory can apply
// the values stored in PlayerCharacter.prefab.
// Standalone additions (movement feel): sliding (crouch while running: capsule shrinks, camera drops, a speed
// boost decays with friction; jumping out of a slide keeps the momentum), wall running (airborne next to a wall
// while pushing forward: reduced gravity, speed along the wall, camera tilt, wall jump away from the wall),
// external impulses (explosion knockback / rocket jumps) and the style meter's S-rank speed bonus.
[RequireComponent(typeof(CharacterController))]
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

    #region Components / Custom Classes / Caches
    private CharacterController m_characterController;
    private Transform m_yawTransform;
    private Transform m_camTransform;
    private HeadBob m_headBob;
    public PlayerInputActions _inputActions;

    private RaycastHit m_hitInfo;
    private IEnumerator m_CrouchRoutine;
    private IEnumerator m_LandRoutine;
    #endregion

    #region Debug
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

    #endregion

    protected override void Start()
    {
        GetComponents();
        InitVariables();
    }

    protected override void FixedUpdate()
    {
        ProgressStepCycle(m_smoothCurrentSpeed);
    }

    protected override void Update()
    {
        if (m_characterController != null)
        {
            // Check if Grounded,Wall etc
            CheckIfGrounded();
            CheckIfWall();

            CalculateSpeed();

            // Apply Smoothing
            SmoothInput();
            SmoothSpeed();
            SmoothDir();

            // Calculate Movement
            CalculateMovementDirection();

            CalculateFinalMovement();

            // standalone: sliding / wall running / dashing override the horizontal motion computed above
            UpdateSlide();
            UpdateWallRun();
            UpdateDash();

            HandleLanding();

            ApplyGravity();

            ApplyMovement();

            m_previouslyGrounded = _inputActions.IsGrounded;
        }
    }

    protected virtual void GetComponents()
    {
        m_AudioSource = GetComponent<AudioSource>();
        m_characterController = GetComponent<CharacterController>();
        _inputActions = GetComponent<PlayerInputActions>();
        m_headBob = GetComponentInChildren<HeadBob>();
        _cameraController = GetComponent<FPSCameraController>();
        _standHeight = m_characterController.height; _standCenter = m_characterController.center;
        m_initHeight = m_characterController.height;
        //m_cameraController = GetComponentInChildren<CameraController>();
        //m_yawTransform = m_cameraController.transform;
    }

    protected virtual void InitVariables()
    {
        // Calculate where our character center should be based on height and skin width
       // m_characterController.center = new Vector3(0f, m_characterController.height / 2f + m_characterController.skinWidth, 0f);

        // Sphere radius not included. If you want it to be included just decrease by sphere radius at the end of this equation
        m_finalRayLength = rayLength + m_characterController.center.y;

        _inputActions.IsGrounded = true;
        m_previouslyGrounded = true;

        m_inAirTimer = 0f;

        m_walkRunSpeedDifference = runSpeed - walkSpeed;

        m_StepCycle = 0f;
        m_NextStep = m_StepCycle / 2f;
    }

    private void PlayLandingSound()
    {
        if (m_AudioSource == null) return;
        m_AudioSource.clip = m_LandSound;
        m_AudioSource.Play();
        m_NextStep = m_StepCycle + .5f;
    }

    private void PlayJumpSound()
    {
        if (m_AudioSource == null) return;
        m_AudioSource.clip = m_JumpSound;
        m_AudioSource.Play();
    }
    private void ProgressStepCycle(float speed)
    {
        if (m_characterController == null) return;

        if (m_characterController.velocity.sqrMagnitude > 0 && (m_inputVector.x != 0 || m_inputVector.y != 0))
        {
            m_StepCycle += (m_characterController.velocity.magnitude + (speed * m_RunstepLenghten)) * Time.fixedDeltaTime;
        }

        if (!(m_StepCycle > m_NextStep))
        {
            return;
        }

        m_NextStep = m_StepCycle + m_StepInterval;

        PlayFootStepAudio();
    }


    private void PlayFootStepAudio()
    {
        if (!m_characterController.isGrounded)
        {
            return;
        }
        if (m_AudioSource == null || m_FootstepSounds == null || m_FootstepSounds.Length < 2) return;
        // pick & play a random footstep sound from the array,
        // excluding sound at index 0
        int n = Random.Range(1, m_FootstepSounds.Length);
        m_AudioSource.clip = m_FootstepSounds[n];
        m_AudioSource.PlayOneShot(m_AudioSource.clip);
        // move picked sound to index 0 so it's not picked next time
        m_FootstepSounds[n] = m_FootstepSounds[0];
        m_FootstepSounds[0] = m_AudioSource.clip;
    }

    #region Smoothing Methods
    protected virtual void SmoothInput()
    {
        m_inputVector = _inputActions.MovementInput.normalized;
        m_smoothInputVector = Vector2.Lerp(m_smoothInputVector, m_inputVector, Time.deltaTime * smoothInputSpeed);
        //Debug.DrawRay(transform.position, new Vector3(m_smoothInputVector.x,0f,m_smoothInputVector.y), Color.green);
    }

    protected virtual void SmoothSpeed()
    {
        m_smoothCurrentSpeed = Mathf.Lerp(m_smoothCurrentSpeed, m_currentSpeed, Time.deltaTime * smoothVelocitySpeed);

        m_finalSmoothCurrentSpeed = m_smoothCurrentSpeed;
    }

    protected virtual void SmoothDir()
    {

        m_smoothFinalMoveDir = Vector3.Lerp(m_smoothFinalMoveDir, m_finalMoveDir, Time.deltaTime * smoothFinalDirectionSpeed);
        Debug.DrawRay(transform.position, m_smoothFinalMoveDir, Color.yellow);
    }
    #endregion

    #region Locomotion Calculation Methods
    protected virtual bool CheckIfGrounded()
    {
        Vector3 _origin = transform.position + m_characterController.center;

        bool _hitGround = m_characterController.isGrounded; //Physics.SphereCast(_origin,raySphereRadius,Vector3.down,out m_hitInfo,m_finalRayLength,groundLayer);
        Debug.DrawRay(_origin, Vector3.down * (m_finalRayLength), Color.red);

        _inputActions.IsGrounded = _hitGround ? true : false;

        return _inputActions.IsGrounded;
    }

    protected virtual void CheckIfWall()
    {

        Vector3 _origin = transform.position + m_characterController.center;
        RaycastHit _wallInfo;

        bool _hitWall = false;

        if (_inputActions.MovementInput.sqrMagnitude > 0 && m_finalMoveDir.sqrMagnitude > 0)
            _hitWall = Physics.SphereCast(_origin, rayObstacleSphereRadius, m_finalMoveDir, out _wallInfo, rayObstacleLength, obstacleLayers);
        Debug.DrawRay(_origin, m_finalMoveDir * rayObstacleLength, Color.blue);

        m_hitWall = _hitWall ? true : false;
    }

    protected virtual bool CheckIfRoof()
    {
        var originVector = transform.position;

        var hitRoof = Physics.SphereCast(originVector, raySphereRadius, Vector3.up, out _, m_initHeight, obstacleLayers);

        return hitRoof;
    }

    protected virtual void CalculateMovementDirection()
    {
        var movementTransform = GetComponent<FPSCameraController>().orientation;

        var vDirection = movementTransform.forward * m_smoothInputVector.y;
        var hDirection = movementTransform.right * m_smoothInputVector.x;

        var desiredDirection = vDirection + hDirection;
        var flattenDirection = FlattenVectorOnSlopes(desiredDirection);

        m_finalMoveDir = flattenDirection;
    }

    protected virtual Vector3 FlattenVectorOnSlopes(Vector3 vectorToFlat)
    {
        if (_inputActions.IsGrounded)
            vectorToFlat = Vector3.ProjectOnPlane(vectorToFlat, m_hitInfo.normal);

        return vectorToFlat;
    }

    protected virtual void CalculateSpeed()
    {
        if (bEnableBunnyHop) m_currentSpeed = CheckIfGrounded() ? runSpeed : bunnyHopSpeed;
        else m_currentSpeed = runSpeed;
        
        
        m_currentSpeed = Math.Abs(_inputActions.MovementInput.y - (-1)) < 1 ? m_currentSpeed * moveBackwardsSpeedPercent : m_currentSpeed;
        m_currentSpeed = _inputActions.MovementInput.x != 0 && _inputActions.MovementInput.y == 0 ? m_currentSpeed * moveSideSpeedPercent : m_currentSpeed;
        if (PlayerState.Current != null && PlayerState.Current.CurrentRageState != null)
            m_currentSpeed *= PlayerState.Current.CurrentRageState.movementSpeedModifier;
        m_currentSpeed *= StyleMeter.SpeedMultiplier; // S rank: faster
    }

    #region Slide & wall run (standalone additions)

    private Vector3 HorizontalVelocity => new Vector3(m_characterController.velocity.x, 0f, m_characterController.velocity.z);

    protected virtual void UpdateSlide()
    {
        float dt = Time.deltaTime;
        if (_slideCooldownTimer > 0f) _slideCooldownTimer -= dt;
        bool grounded = m_characterController.isGrounded;

        if (!IsSliding)
        {
            if (enableSlide && grounded && !IsWallRunning && _inputActions.SlidePressed && _slideCooldownTimer <= 0f)
            {
                Vector3 hv = HorizontalVelocity;
                Vector3 wish = new Vector3(m_finalMoveVector.x, 0f, m_finalMoveVector.z);
                Vector3 dir = hv.sqrMagnitude > wish.sqrMagnitude ? hv : wish;
                if (dir.magnitude >= slideStartSpeed * 0.75f)
                {
                    IsSliding = true;
                    _slideTime = 0f;
                    _slideDir = dir.normalized;
                    _slideSpeed = Mathf.Max(dir.magnitude, runSpeed) + slideSpeedBoost;
                    float drop = (_standHeight - slideHeight) * 0.5f;
                    m_characterController.height = slideHeight;
                    m_characterController.center = _standCenter - new Vector3(0f, drop, 0f);
                }
            }
            if (m_headBob != null) m_headBob.heightOffset = Mathf.Lerp(m_headBob.heightOffset, 0f, dt * 10f);
            return;
        }

        _slideTime += dt;
        // steer a little, lose speed to friction (downhill keeps it: gravity adds along the slope through the controller)
        Vector3 right = Vector3.Cross(Vector3.up, _slideDir);
        _slideDir = (_slideDir + right * (_inputActions.MovementInput.x * slideSteer * dt)).normalized;
        _slideSpeed -= slideFriction * dt;
        m_finalMoveVector.x = _slideDir.x * _slideSpeed;
        m_finalMoveVector.z = _slideDir.z * _slideSpeed;
        if (m_headBob != null) m_headBob.heightOffset = Mathf.Lerp(m_headBob.heightOffset, -slideCameraDrop, dt * 12f);

        bool wantStop = _slideSpeed < slideMinSpeed || !grounded || (_slideTime > 0.25f && !_inputActions.SlideHeld);
        if (_inputActions.IsJumping && grounded)
        {
            // slide jump: keep the momentum, pop up
            m_finalMoveVector.y = jumpSpeed;
            _inputActions.IsJumping = false;
            _inputActions.IsGrounded = false;
            m_previouslyGrounded = true;
            PlayJumpSound();
            wantStop = true;
        }
        if (wantStop && !CheckIfRoof()) EndSlide();
        else if (wantStop) { _slideSpeed = Mathf.Max(_slideSpeed, 2f); } // crawl on until there is room to stand
    }

    private void EndSlide()
    {
        IsSliding = false;
        _slideCooldownTimer = slideCooldown;
        m_characterController.height = _standHeight;
        m_characterController.center = _standCenter;
    }

    protected virtual void UpdateWallRun()
    {
        float dt = Time.deltaTime;
        if (_wallJumpCooldown > 0f) _wallJumpCooldown -= dt;
        bool grounded = m_characterController.isGrounded;
        if (!enableWallRun || IsSliding || grounded)
        {
            if (IsWallRunning) EndWallRun();
            return;
        }

        Vector3 origin = transform.position + m_characterController.center;
        Vector3 fwd = _cameraController != null ? _cameraController.orientation.forward : transform.forward;
        Vector3 rightDir = Vector3.Cross(Vector3.up, fwd).normalized;
        float reach = m_characterController.radius + wallCheckDistance;
        int side = 0; Vector3 normal = Vector3.zero;
        if (Physics.Raycast(origin, rightDir, out RaycastHit rh, reach, obstacleLayers) && Mathf.Abs(rh.normal.y) < 0.35f) { side = 1; normal = rh.normal; }
        else if (Physics.Raycast(origin, -rightDir, out RaycastHit lh, reach, obstacleLayers) && Mathf.Abs(lh.normal.y) < 0.35f) { side = -1; normal = lh.normal; }

        bool pushingForward = _inputActions.MovementInput.y > 0.3f;
        if (!IsWallRunning)
        {
            if (side != 0 && pushingForward && _wallJumpCooldown <= 0f && m_inAirTimer > 0.05f && HorizontalVelocity.magnitude > 3f)
            {
                IsWallRunning = true; WallSide = side; _wallNormal = normal; _wallRunTimer = 0f;
                m_finalMoveVector.y = Mathf.Max(m_finalMoveVector.y, wallRunEntryBoost);
            }
            else return;
        }
        else if (side == 0 || !pushingForward || _wallRunTimer > wallRunMaxTime)
        {
            EndWallRun();
            return;
        }
        else { WallSide = side; _wallNormal = normal; }

        _wallRunTimer += dt;
        if (_cameraController != null) _cameraController.externalRoll = -WallSide * wallRunTilt;
        Vector3 along = Vector3.ProjectOnPlane(fwd, _wallNormal);
        if (along.sqrMagnitude < 1E-04f) { EndWallRun(); return; }
        along.Normalize();
        Vector3 run = along * wallRunSpeed - _wallNormal * 1.5f; // hug the wall so the side ray keeps seeing it
        m_finalMoveVector.x = run.x; m_finalMoveVector.z = run.z;
        if (m_finalMoveVector.y < -3f) m_finalMoveVector.y = -3f; // glide instead of falling

        if (_inputActions.IsJumping)
        {
            // wall jump: away from the wall, up, keep the run speed
            Vector3 launch = along * wallRunSpeed * 0.8f + _wallNormal * wallJumpAway;
            m_finalMoveVector.x = launch.x; m_finalMoveVector.z = launch.z; m_finalMoveVector.y = wallJumpUp;
            _inputActions.IsJumping = false;
            _wallJumpCooldown = 0.35f;
            PlayJumpSound();
            EndWallRun();
        }
    }

    private void EndWallRun()
    {
        IsWallRunning = false;
        WallSide = 0;
        if (_cameraController != null) _cameraController.externalRoll = 0f;
    }

    /// <summary>Dash: a short burst in the direction of the movement input (or forward), no gravity while it lasts; one air dash per jump.</summary>
    protected virtual void UpdateDash()
    {
        float dt = Time.deltaTime;
        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
        bool grounded = m_characterController.isGrounded;
        if (grounded && !IsDashing) _airDashesLeft = airDashes;

        if (!IsDashing && enableDash && _inputActions.DashPressed && _dashCooldownTimer <= 0f && (grounded || _airDashesLeft > 0))
        {
            var o = _cameraController != null ? _cameraController.orientation : transform;
            Vector2 input = _inputActions.MovementInput;
            Vector3 dir = o.forward * input.y + o.right * input.x;
            if (dir.sqrMagnitude < 0.05f) dir = o.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1E-04f) return;
            dir.Normalize();
            if (IsSliding) EndSlide();
            if (IsWallRunning) EndWallRun();
            if (!grounded) _airDashesLeft--;
            IsDashing = true; _dashTimer = dashDuration; _dashDir = dir; _dashCooldownTimer = dashCooldown;
            EnragedInjector<CameraRecoil>.GetInstance()?.AddTrauma(0.14f);
            if (m_AudioSource != null) m_AudioSource.PlayOneShot(RipNRage.Game.Content.ProceduralSounds.Dash, 0.6f);
            RipNRage.Game.Content.Vfx.GameVfx.Spawn("dash", transform.position - dir * 0.6f, -dir);
        }

        if (!IsDashing) return;
        _dashTimer -= dt;
        m_finalMoveVector.x = _dashDir.x * dashSpeed;
        m_finalMoveVector.z = _dashDir.z * dashSpeed;
        m_finalMoveVector.y = 0f;
        if (_dashTimer <= 0f)
        {
            IsDashing = false;
            // hand back a fast but normal run so the dash does not end in a dead stop
            m_finalMoveVector.x = _dashDir.x * runSpeed * 1.1f;
            m_finalMoveVector.z = _dashDir.z * runSpeed * 1.1f;
        }
    }

    /// <summary>World-space velocity change applied on the next movement step (explosion knockback, rocket jumps).</summary>
    public void AddImpulse(Vector3 impulse)
    {
        _pendingImpulse += impulse;
        const float maxImpulse = 40f; // defensive cap: a mistuned knockback must not fling the player through the world in one step
        if (_pendingImpulse.sqrMagnitude > maxImpulse * maxImpulse) _pendingImpulse = _pendingImpulse.normalized * maxImpulse;
    }

    #endregion

    protected virtual void CalculateFinalMovement()
    {
        var smoothInputVectorMagnitude = 1f;
        var finalVector = m_smoothFinalMoveDir * (m_finalSmoothCurrentSpeed * smoothInputVectorMagnitude);

        m_finalMoveVector.x = finalVector.x;
        m_finalMoveVector.z = finalVector.z;

        if (m_characterController.isGrounded)
            m_finalMoveVector.y += finalVector.y;
    }
    #endregion

    #region Landing Methods
    protected virtual void HandleLanding()
    {
        if (m_previouslyGrounded || !_inputActions.IsGrounded) return;

        InvokeLandingRoutine();
        PlayLandingSound();
    }

    protected virtual void InvokeLandingRoutine()
    {
        if (m_LandRoutine != null)
            StopCoroutine(m_LandRoutine);

        m_LandRoutine = LandingRoutine();
        StartCoroutine(m_LandRoutine);
    }

    protected virtual IEnumerator LandingRoutine()
    {
        var percent = 0f;
        var landAmount = 0f;

        var speed = 1f / landDuration;

        var localPos = Vector2.zero;// m_yawTransform.localPosition;
        var initLandHeight = localPos.y;

        landAmount = m_inAirTimer > landTimer ? highLandAmount : lowLandAmount;

        while (percent < 1f)
        {
            percent += Time.deltaTime * speed;
            var desiredY = landCurve.Evaluate(percent) * landAmount;

            localPos.y = initLandHeight + desiredY;
            //m_yawTransform.localPosition = localPos;

            yield return null;
        }
    }
    #endregion

    #region Locomotion Apply
    protected virtual void HandleJump()
    {
        if (_inputActions.IsJumping)
        {
            m_finalMoveVector.y = jumpSpeed;

            m_previouslyGrounded = true;
            _inputActions.IsGrounded = false;

            PlayJumpSound();
        }
    }

    protected virtual void ApplyGravity()
    {
        if (IsDashing) { m_finalMoveVector.y = 0f; return; } // the dash ignores gravity for its duration
        if (m_characterController.isGrounded && m_finalMoveVector.y <= 0.01f)
        {
            m_inAirTimer = 0f;
            m_finalMoveVector.y = -stickToGroundForce;

             HandleJump();  
             _inputActions.IsJumping = false;
        }
        else
        {
            m_inAirTimer += Time.deltaTime;
            float g = IsWallRunning ? wallRunGravity : 1f;
            m_finalMoveVector += Physics.gravity * gravityMultiplier * g * Time.deltaTime;
        }

        if (_pendingImpulse != Vector3.zero)
        {
            m_finalMoveVector += _pendingImpulse;
            if (_pendingImpulse.y > 0f) { _inputActions.IsGrounded = false; m_previouslyGrounded = true; }
            _pendingImpulse = Vector3.zero;
        }
    }

    protected virtual void ApplyMovement()
    {
        if (!m_characterController.enabled) return;

        m_characterController.Move(m_finalMoveVector * Time.deltaTime);
    }

    /// <summary>Standalone addition: drops every accumulated velocity (fall respawn / teleports).</summary>
    public void ResetVelocity()
    {
        _pendingImpulse = Vector3.zero;
        IsDashing = false;
        if (IsSliding) EndSlide();
        if (IsWallRunning) EndWallRun();
        m_finalMoveVector = Vector3.zero;
        m_smoothFinalMoveDir = Vector3.zero;
        m_smoothInputVector = Vector2.zero;
        m_smoothCurrentSpeed = 0f;
        m_inAirTimer = 0f;
    }
    #endregion
}
