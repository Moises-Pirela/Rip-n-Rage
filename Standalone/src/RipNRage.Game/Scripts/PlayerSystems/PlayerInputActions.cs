using System;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Game.Ecs;

// MIGRATION FACADE (layer 4; gone in layers 5 / 8 as combat and the UI are ported). The devices are sampled by the
// ECS EcsInputSystem into the InputState singleton — one snapshot per frame that every system reads. This component
// no longer polls anything: it mirrors that snapshot onto the fields the not-yet-ported scripts expect, and keeps
// dispatching the weapon actions to the CombatController the way Unity's PlayerInput "Send Messages" did.
//
// Pause and death silence it through the flow (8.3): FlowSystem sets InputOverride.Blocked for every phase but
// Playing, the snapshot reads neutral and not Live, and the mirror below follows it.
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

    private PlayerLink _link;

    /// <summary>Automation hooks (DemoScript): forwarded to the engine-wide <see cref="InputOverride"/>.</summary>
    public bool? overrideFiring { get => InputOverride.Fire; set => InputOverride.Fire = value; }
    public Vector2? overrideMove { get => InputOverride.Move; set => InputOverride.Move = value; }
    public Vector2? overrideLook { get => InputOverride.Look; set => InputOverride.Look = value; }

    protected override void Awake() => EnragedInjector<PlayerInputActions>.SetInstance(this);

    protected override void Update()
    {
        var s = Application.Current.World.GetSingleton<InputState>();

        MovementInput = s.Move;
        LookInput = s.Look;
        IsFiring = s.FireHeld;
        SlidePressed = s.SlidePressed;
        SlideHeld = s.SlideHeld;
        DashPressed = s.DashPressed;
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

    /// <summary>Queues a jump (the automation scripts).</summary>
    public void OnJump() { if (RipNRage.Game.Ecs.Flows.Playing) InputOverride.JumpOnce(); }
}
