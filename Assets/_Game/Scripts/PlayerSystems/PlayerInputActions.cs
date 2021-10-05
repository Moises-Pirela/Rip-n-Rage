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

    public System.Action OnPrimaryFire;
    public System.Action OnSecondaryFire;
    public System.Action OnCancelAltFire;
    public System.Action<int> OnSwapWheel;
    public System.Action<int> OnSwapButton;
    
    public void OnMove(InputAction.CallbackContext context)
    {
        MovementInput = context.ReadValue<Vector2>();
    }
    
    public void OnLook(InputAction.CallbackContext context)
    {
        LookInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        
        IsJumping = true;
        CanJump= true;
    }

    public void OnFire(InputAction.CallbackContext context)
    {   
        OnPrimaryFire();
    }

    public void OnAltFire(InputAction.CallbackContext context)
    {
        OnSecondaryFire();

        if (context.canceled)
        {
            Debug.Log("Cancelled");
            OnCancelAltFire();
        }
    }

    public void OnSwapWeapon(InputAction.CallbackContext context)
    {
        int index = Mathf.FloorToInt(context.ReadValue<float>());
        
        if (index == 0) return;

        index = Mathf.Clamp(index, -1, 1);
        
        OnSwapWheel(index);
    }

    public void OnChangeWeapon1(InputAction.CallbackContext context)
    {
        OnSwapButton(0);
    }
    
    public void OnChangeWeapon2(InputAction.CallbackContext context)
    {
        OnSwapButton(1);
    }
    
    public void OnChangeWeapon3(InputAction.CallbackContext context)
    {
        OnSwapButton(2);
    }
}
