using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;

public class PlayerInputActions : MonoBehaviour
{
    private PlayerControls _playerControls;
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }
    
    private bool isBlockingInput;

    public System.Action OnPrimaryFire;
    public System.Action OnSecondaryFire;
    public System.Action OnCancelAltFire;
    public System.Action<int> OnSwapWheel;
    public System.Action<int> OnSwapButton;

    private void Awake()
    {
        EnragedInjector<PlayerInputActions>.SetInstance(this);
    }

    public bool GetIsBlockingInput()
    {
        return isBlockingInput;
    }

    public void SetIsBlockingInput(bool shouldBlock)
    {
        isBlockingInput = shouldBlock;
    }

    public void OnMove(InputValue value)
    {
        if (isBlockingInput) return;

        MovementInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        if (isBlockingInput) return;

        LookInput = value.Get<Vector2>();
    }

    public void OnJump()
    {
        if (isBlockingInput) return;

        if (IsJumping) return;
        
        IsJumping = true;
        CanJump = true;
    }

    public void OnFire(InputValue value)
    {
        if (isBlockingInput) return;

        // if (value.isPressed)
        //     OnPrimaryFire();    
    }

    public void OnAltFire(InputValue value)
    {
        if (isBlockingInput) return;
        //
        // if (value.isPressed)
        //     OnSecondaryFire();
    }
}