using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputActions : MonoBehaviour
{
    private PlayerControls _playerControls;
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }

    public System.Action OnPrimaryFire;
    public System.Action OnSecondaryFire;
    
    public void OnMove(InputValue value)
    {
        MovementInput = value.Get<Vector2>();
    }
    
    public void OnLook(InputValue value)
    {
        LookInput =value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        IsJumping = true;
        CanJump= true;
    }

    public void OnFire(InputValue value)
    {
        OnPrimaryFire();
    }

    public void OnAltFire(InputValue value)
    {
        OnSecondaryFire();
    }
}
