using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/FPSController.cs.
// CharacterController / Physics / AnimationCurve / AudioSource are the engine's replacements;
// the locomotion logic is unchanged. Serialized fields are public so the player factory can apply
// the values stored in PlayerCharacter.prefab.
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
    }

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
        if (m_characterController.isGrounded)
        {
            m_inAirTimer = 0f;
            m_finalMoveVector.y = -stickToGroundForce;

             HandleJump();  
             _inputActions.IsJumping = false;
        }
        else
        {
            m_inAirTimer += Time.deltaTime;
            m_finalMoveVector += Physics.gravity * gravityMultiplier * Time.deltaTime;
        }
    }

    protected virtual void ApplyMovement()
    {
        if (!m_characterController.enabled) return;

        m_characterController.Move(m_finalMoveVector * Time.deltaTime);
    }
    #endregion
}
