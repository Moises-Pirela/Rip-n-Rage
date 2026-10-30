using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/PlayerSystems/PlayerInputActions.cs.
// The Unity Input System (PlayerControls.inputactions + generated PlayerControls.cs + PlayerInput
// "Send Messages") is replaced by the engine's InputSystem: named, rebindable actions of the "gameplay"
// scheme (keyboard / mouse slot + gamepad slot each, saved in controls.json) polled every frame and
// dispatched through the same On* callbacks the PlayerInput component used to send. Defaults mirror
// PlayerControls.inputactions (WASD / mouse / Space / LMB / RMB / 1-4 / wheel) plus a gamepad layout
// (left stick move, right stick look, RT fire, LT jab, A jump, D-pad weapons, bumpers cycle, Start pause).
// While any UI screen owns the input the gameplay scheme is idle, so nothing here fires.
// Disabling the component (pause menu, death) silences every input; on re-enable the fire button has
// to be released once so the click that closed a menu does not fire the weapon.
public class PlayerInputActions : MonoBehaviour
{
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }

    public bool IsFiring { get; set; }
    /// <summary>Standalone additions: slide / crouch button state for the movement controller.</summary>
    public bool SlidePressed { get; private set; }
    public bool SlideHeld { get; private set; }
    public bool DashPressed { get; private set; }

    private bool isBlockingInput;
    private bool _enabled;
    /// <summary>After (re)enabling, the fire button must be released once before it counts: a click that closed a menu must not fire the gun.</summary>
    private bool _waitForFireRelease;

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
        _waitForFireRelease = true; // released once before it counts (see the header comment)
    }

    protected override void OnDisable()
    {
        _enabled = false;
        MovementInput = Vector2.zero;
        LookInput = Vector2.zero;
        IsFiring = false;
        SlidePressed = SlideHeld = DashPressed = false;
    }

    protected override void Update()
    {
        if (!_enabled) return;
        var G = InputSystem.Gameplay;
        bool live = G.Enabled; // false while a menu / death screen / settings page owns the input

        // Fire.performed / Fire.canceled
        bool fireHeld = live && G["fire"].Held;
        if (_waitForFireRelease && !fireHeld) _waitForFireRelease = false;
        IsFiring = overrideFiring ?? (fireHeld && !_waitForFireRelease);

        // Move (2D vector composite: digital keys or analog stick directions, whichever the action is bound to)
        Vector2 move = Vector2.zero;
        if (live)
        {
            move = new Vector2(G["move_right"].Value - G["move_left"].Value, G["move_forward"].Value - G["move_back"].Value);
            if (move.sqrMagnitude > 1f) move = move.normalized;
        }
        OnMove(overrideMove ?? move);

        // Look: mouse delta (pixels) + right stick scaled to the same units (FPSCameraController: degrees = value * 0.1)
        Vector2 look = Vector2.zero;
        if (live)
        {
            look = Input.mouseDelta * lookScale * InputSystem.mouseSensitivity;
            if (InputSystem.invertMouseY) look.y = -look.y;
            Vector2 stick = InputSystem.RightStick();
            if (stick.sqrMagnitude > 0f)
            {
                Vector2 s = stick * (InputSystem.gamepadLookSpeed * Time.deltaTime * 10f);
                if (InputSystem.invertGamepadY) s.y = -s.y;
                look += s;
            }
        }
        OnLook(overrideLook ?? look);

        if (live && G["jump"].Pressed) OnJump();
        SlidePressed = live && !isBlockingInput && G["slide"].Pressed;
        SlideHeld = live && !isBlockingInput && G["slide"].Held;
        DashPressed = live && !isBlockingInput && G["dash"].Pressed;

        var combat = EnragedInjector<CombatController>.GetInstance();
        if (combat != null && live)
        {
            if (G["alt_fire"].Pressed) combat.OnAltFire(true);
            if (G["weapon_1"].Pressed) combat.OnChangeWeapon1();
            if (G["weapon_2"].Pressed) combat.OnChangeWeapon2();
            if (G["weapon_3"].Pressed) combat.OnChangeWeapon3();
            if (G["weapon_4"].Pressed) combat.OnChangeWeapon4();
            if (G["weapon_next"].Pressed) combat.OnSwapWeapon(1f);
            else if (G["weapon_prev"].Pressed) combat.OnSwapWeapon(-1f);
            if (G["weapon_last"].Pressed) combat.OnSwapToLastWeapon();
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
