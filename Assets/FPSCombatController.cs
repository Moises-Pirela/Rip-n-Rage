using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FPSCombatController : MonoBehaviour
{
    private InputHandler inputHandler;
    private PlayerInventory playerInventory;

    void Start()
    {
        inputHandler = GetComponent<InputHandler>();
        playerInventory = GetComponent<PlayerInventory>();
    }

    void Update()
    {
        HandleFire();
        HandleAltFire();
    }

    void HandleFire()
    {
        if (!inputHandler.IsShooting) return;

        playerInventory.Fire();

        inputHandler.IsShooting = false;
    }

    void HandleAltFire()
    {
        if (!inputHandler.IsAltFiring) return;

        playerInventory.AltFire();

        inputHandler.IsAltFiring = false;
    }
}
