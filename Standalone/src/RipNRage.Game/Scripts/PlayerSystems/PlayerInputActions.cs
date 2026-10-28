using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/PlayerSystems/PlayerInputActions.cs.
// The Unity Input System (PlayerControls.inputactions + generated PlayerControls.cs + PlayerInput
// "Send Messages") is replaced by polling the engine's Input each frame and dispatching the same
// On* callbacks the PlayerInput component used to send. Bindings mirror PlayerControls.inputactions:
//   Move = WASD, Look = mouse delta, Jump = Space, Fire = LMB, AltFire = RMB,
//   ChangeWeapon1/2/3 = 1/2/3, SwapWeapon = mouse wheel.
public class PlayerInputActions : MonoBehaviour
{
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }

    public bool IsFiring { get; set; }

    private bool isBlockingInput;
    private bool _enabled;

    /// <summary>Unity's Mouse/delta is in pixels per frame; the camera code multiplies by sensitivity * fixedDeltaTime.</summary>
    public float lookScale = 1f;

    /// <summary>Automation hooks (DemoScript): when set they replace the real device input for that frame.</summary>
    public bool? overrideFiring;
    public Vector2? overrideMove;
    public Vector2? overrideLook;

    protected override void Awake()
    {
        EnragedInjector<PlayerInputActions>.SetInstance(this);
    }

    protected override void OnEnable()
    {
        _enabled = true;
    }

    protected override void OnDisable()
    {
        _enabled = false;
        MovementInput = Vector2.zero;
        LookInput = Vector2.zero;
        IsFiring = false;
    }

    protected override void Update()
    {
        if (!_enabled) return;

        // Fire.performed / Fire.canceled
        IsFiring = overrideFiring ?? Input.GetMouseButton(0);

        // Move (2D vector composite)
        float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        OnMove(overrideMove ?? new Vector2(x, y));

        // Look (mouse delta)
        OnLook(overrideLook ?? Input.mouseDelta * lookScale);

        if (Input.GetKeyDown(KeyCode.Space)) OnJump();

        var combat = EnragedInjector<CombatController>.GetInstance();
        if (combat != null)
        {
            if (Input.GetMouseButtonDown(1)) combat.OnAltFire(true);
            if (Input.GetKeyDown(KeyCode.Alpha1)) combat.OnChangeWeapon1();
            if (Input.GetKeyDown(KeyCode.Alpha2)) combat.OnChangeWeapon2();
            if (Input.GetKeyDown(KeyCode.Alpha3)) combat.OnChangeWeapon3();
            if (Input.GetKeyDown(KeyCode.Alpha4)) combat.OnChangeWeapon4();
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f) combat.OnSwapWeapon(scroll);
        }
    }

    public bool GetIsBlockingInput()
    {
        return isBlockingInput;
    }

    public void SetIsBlockingInput(bool shouldBlock)
    {
        isBlockingInput = shouldBlock;

        if (!isBlockingInput) return;
        
        LookInput = Vector2.zero;
        MovementInput = Vector2.zero;
    }

    public void OnMove(Vector2 value)
    {
        if (isBlockingInput) return;

        MovementInput = value;
    }

    public void OnLook(Vector2 value)
    {
        if (isBlockingInput) return;

        LookInput = value;
    }

    public void OnJump()
    {
        if (isBlockingInput) return;

        if (IsJumping) return;
        
        IsJumping = true;
        CanJump = true;
    }
}
