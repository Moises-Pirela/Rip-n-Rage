using System;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Game.Ecs;

// MIGRATION FACADE (layer 4; gone in layers 5 / 8 as combat and the UI are ported). The devices are sampled by the
// ECS EcsInputSystem into the InputState singleton — one snapshot per frame that every system reads. This component
// no longer polls anything: it mirrors that snapshot onto the fields the not-yet-ported scripts expect, and keeps
// dispatching the weapon actions to the CombatController the way Unity's PlayerInput "Send Messages" did.
//
// Disabling the component (pause menu, death) silences the mirrored input for those scripts.
public class PlayerInputActions : MonoBehaviour
{
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }

    public bool IsFiring { get; set; }
    public bool SlidePressed { get; private set; }
    public bool SlideHeld { get; private set; }
    public bool DashPressed { get; private set; }

    private bool isBlockingInput;
    private bool _enabled;
    private PlayerLink _link;

    /// <summary>Automation hooks (DemoScript): forwarded to the engine-wide <see cref="InputOverride"/>.</summary>
    public bool? overrideFiring { get => InputOverride.Fire; set => InputOverride.Fire = value; }
    public Vector2? overrideMove { get => InputOverride.Move; set => InputOverride.Move = value; }
    public Vector2? overrideLook { get => InputOverride.Look; set => InputOverride.Look = value; }

    protected override void Awake() => EnragedInjector<PlayerInputActions>.SetInstance(this);

    protected override void OnEnable()
    {
        _enabled = true;
        InputOverride.Blocked = isBlockingInput;
    }

    protected override void OnDisable()
    {
        _enabled = false;
        InputOverride.Blocked = true; // pause menu / death: the player's systems go quiet too
        MovementInput = Vector2.zero;
        LookInput = Vector2.zero;
        IsFiring = false;
        SlidePressed = SlideHeld = DashPressed = false;
    }

    protected override void Update()
    {
        if (!_enabled) return;
        var s = Application.Current.World.GetSingleton<InputState>();

        MovementInput = isBlockingInput ? Vector2.zero : s.Move;
        LookInput = isBlockingInput ? Vector2.zero : s.Look;
        IsFiring = s.FireHeld;
        SlidePressed = !isBlockingInput && s.SlidePressed;
        SlideHeld = !isBlockingInput && s.SlideHeld;
        DashPressed = !isBlockingInput && s.DashPressed;
        IsJumping = s.JumpQueued;
        _link ??= GetComponent<PlayerLink>();
        IsGrounded = _link != null && _link.IsGrounded;

        var combat = EnragedInjector<CombatController>.GetInstance();
        if (combat != null && s.Live)
        {
            var G = InputSystem.Gameplay;
            if (G["weapon_1"].Pressed) combat.OnChangeWeapon1();
            if (G["weapon_2"].Pressed) combat.OnChangeWeapon2();
            if (G["weapon_3"].Pressed) combat.OnChangeWeapon3();
            if (G["weapon_4"].Pressed) combat.OnChangeWeapon4();
            if (G["weapon_next"].Pressed) combat.OnSwapWeapon(1f);
            else if (G["weapon_prev"].Pressed) combat.OnSwapWeapon(-1f);
            if (G["weapon_last"].Pressed) combat.OnSwapToLastWeapon();
        }
    }

    public bool GetIsBlockingInput() => isBlockingInput;

    public void SetIsBlockingInput(bool shouldBlock)
    {
        isBlockingInput = shouldBlock;
        InputOverride.Blocked = shouldBlock; // the systems read the singleton, so the gate has to be there too
        if (!isBlockingInput) return;
        LookInput = Vector2.zero;
        MovementInput = Vector2.zero;
    }

    /// <summary>Queues a jump (the automation scripts).</summary>
    public void OnJump() { if (!isBlockingInput) InputOverride.JumpOnce(); }
}
