using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;

public class PlayerInputActions : MonoBehaviour
{
    private PlayerControls _playerControls;
    private PlayerInput _playerInput;
    public Vector2 MovementInput { get; set; }
    public Vector2 LookInput { get; set; }
    public bool IsJumping { get; set; }
    public bool CanJump { get; set; }
    public bool IsGrounded { get; set; }

    public bool IsFiring { get; set; }

    private bool isBlockingInput;

    private void Awake()
    {
        EnragedInjector<PlayerInputActions>.SetInstance(this);

        _playerControls = new PlayerControls();

        _playerInput = new PlayerInput();

        InputSystem.pollingFrequency = 120;

        _playerControls.Character.Fire.performed += context =>
        {
            IsFiring = true;
        };
        _playerControls.Character.Fire.canceled += context =>
        {
            IsFiring = false;
        };
    }

    private void OnEnable()
    {
        _playerControls.Enable();
    }

    private void OnDisable()
    {
        _playerControls.Disable();
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
}