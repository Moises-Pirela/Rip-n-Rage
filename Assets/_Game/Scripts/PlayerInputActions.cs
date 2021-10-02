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
    public System.Action<int> OnSwapWheel;
    public System.Action<int> OnSwapButton;
    
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

    public void OnSwapWeapon(InputValue value)
    {
        int index = Mathf.FloorToInt(value.Get<float>());
        
        if (index == 0) return;

        index = Mathf.Clamp(index, -1, 1);
        
        OnSwapWheel(index);
    }

    public void OnChangeWeapon1(InputValue value)
    {
        OnSwapButton(0);
    }
    
    public void OnChangeWeapon2(InputValue value)
    {
        OnSwapButton(1);
    }
    
    public void OnChangeWeapon3(InputValue value)
    {
        OnSwapButton(2);
    }
}
