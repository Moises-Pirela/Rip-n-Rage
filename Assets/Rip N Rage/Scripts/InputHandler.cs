using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class InputHandler : MonoBehaviour, IMovable, IJumpable, IGravitational, ICombatable
{
    private PlayerControls _playerControls;
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }
    public bool IsSliding { get; set; }
    public bool IsShooting { get; set; }
    public bool IsAltFiring { get; set; }

    protected void Awake()
    {
        _playerControls = new PlayerControls();

        _playerControls.Enable();

        _playerControls.Character.Move.performed += context =>
        {
            MovementInput = context.ReadValue<Vector2>();
        };
        _playerControls.Character.Look.performed += context =>
        {
            LookInput = context.ReadValue<Vector2>();
        };
        _playerControls.Character.Jump.performed += context =>
        {
            //if (!IsGrounded) return;

            IsJumping = true;
            CanJump= true;
        };
        _playerControls.Character.Crouch.performed += context =>
        {
            if (MovementInput.y > 0)
                IsSliding = true;
        };
        _playerControls.Character.Fire.performed += context =>
        {
            IsShooting = true;
        };
        _playerControls.Character.AltFire.performed += context =>
        {
            IsAltFiring = true;
        };
    }
}

public interface IMovable
{
    Vector2 MovementInput { get; set; }
    Vector2 LookInput { get; set; }
    bool IsSliding { get; set; }
}

public interface IJumpable
{
    bool CanJump { get; set; }
    bool IsJumping { get; set; }
}

public interface IGravitational
{
    bool IsGrounded { get; set; }
}

public interface ICombatable
{
    bool IsShooting { get; set; }
    bool IsAltFiring { get; set; }
}