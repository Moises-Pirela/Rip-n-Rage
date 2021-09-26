using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class WeaponSway : MonoBehaviour
{
    [Range(0,100)]
    [SerializeField] private float swayAmount;

    [Range(0,100)]
    [SerializeField] private float swaySpeed; 
     [Range(0,100)]
    [SerializeField] private float horizontalSwayAmount;

    [Range(0,100)]
    [SerializeField] private float horizontalSwaySpeed;       

    [SerializeField] private FPSController controller;

    private Vector3 startPos;
    private float timer;
    float horizontalTimer;
    
    [Space(100)]
    [Header("Sway animation data")] 
    [SerializeField] private float zSmoothLerp = 12;
    [SerializeField] private float zSwayAngle = 1.5f;
    [SerializeField] private float zSwayMultiplier = -50f;
    [SerializeField] private float zSmoothSway = 20f;
    
    void Start()
    {
        startPos = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (controller._inputActions.MovementInput.y != 0 || controller._inputActions.MovementInput.x != 0)
        {
            timer += Time.deltaTime * swaySpeed;
            
            horizontalTimer += Time.deltaTime * horizontalSwaySpeed;
            
            float z = transform.localRotation.eulerAngles.z;
            
            float zLerp = Mathf.LerpAngle(z, 0, Time.deltaTime * zSmoothLerp);
            
            float zSway = Mathf.Clamp(controller._inputActions.MovementInput.x * zSwayMultiplier * Time.smoothDeltaTime * zSmoothSway, -zSwayAngle, zSwayAngle);
            
            zSway = Mathf.LerpAngle(z, zSway, Time.deltaTime * zSmoothLerp);
            
            transform.localRotation = Quaternion.Euler(transform.localEulerAngles.x, transform.localEulerAngles.y, zSway);

            if (!controller._inputActions.IsGrounded) return;
            
            transform.localPosition = new Vector3(startPos.x + Mathf.Sin(horizontalTimer) * horizontalSwayAmount, 
                    startPos.y + Mathf.Sin(timer) * swayAmount, 
                    transform.localPosition.z);
        }
        else
        {
            float z = transform.localRotation.eulerAngles.z;
            
            float zLerp = Mathf.LerpAngle(z, 0, Time.deltaTime * 12);
            
            transform.localRotation = Quaternion.Euler(transform.localEulerAngles.x, transform.localEulerAngles.y, zLerp);
            
            if (!controller._inputActions.IsGrounded) return;
            
            timer = 0;
            horizontalTimer = 0;
            transform.localPosition = new Vector3(Mathf.Lerp(transform.localPosition.x, startPos.x, Time.deltaTime * horizontalSwaySpeed), Mathf.Lerp(transform.localPosition.y, startPos.y,
            Time.deltaTime * swaySpeed), transform.localPosition.z);
        }
    }
}
