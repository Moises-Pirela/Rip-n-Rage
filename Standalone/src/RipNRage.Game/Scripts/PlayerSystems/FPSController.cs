using System;
using RipNRage.Game.Ecs;

// MIGRATION FACADE (layer 4; gone in layer 9). The locomotion itself is now PlayerMoveSystem on the player's
// character entity (src/RipNRage.Game/Ecs/PlayerCharacter.cs) — it reads the InputState singleton, computes the
// wanted velocity and hands it to the entity's CharacterMotor. What is left here is the surface the not-yet-ported
// scripts talk to: the weapon sway and head bob ask whether the player is sliding or grounded, the rocket asks for
// a knockback impulse, the fall safety net resets the velocity, and the automation scripts read the state.
//
// Tunables no longer live on this component: MovementSettings writes them straight onto the PlayerMotor component,
// which tracks tunables.cfg [movement] live.
public class FPSController : MonoBehaviour
{
    private PlayerLink _link;
    public PlayerInputActions _inputActions;

    private PlayerLink Link => _link ??= GetComponent<PlayerLink>();
    private bool Has => Link != null && Link.HasMotor;

    protected override void Start()
    {
        _link = GetComponent<PlayerLink>();
        _inputActions = GetComponent<PlayerInputActions>();
    }

    // ---- state the other scripts read ----
    public bool IsSliding => Has && Link.Motor.IsSliding;
    public bool IsWallRunning => Has && Link.Motor.IsWallRunning;
    public bool IsDashing => Has && Link.Motor.IsDashing;
    /// <summary>-1 = wall on the left, +1 = wall on the right.</summary>
    public int WallSide => Has ? Link.Motor.WallSide : 0;
    public bool IsGrounded => Link != null && Link.IsGrounded;
    public Vector3 Velocity => Link != null ? Link.Velocity : Vector3.zero;
    /// <summary>The speed the motor is being asked for (the overlay and the automation logs).</summary>
    public float MCurrentSpeed => Has ? Link.Motor.CurrentSpeed : 0f;
    /// <summary>Camera drop the slide asks for; HeadBob applies it.</summary>
    public float CameraHeightOffset => Has ? Link.Motor.CameraHeightOffset : 0f;

    /// <summary>Knockback / rocket-jump impulse.</summary>
    public void AddImpulse(Vector3 impulse) => Link?.AddImpulse(impulse);
    /// <summary>Drops every accumulated velocity (fall respawn / teleports).</summary>
    public void ResetVelocity() => Link?.ResetVelocity();
}
