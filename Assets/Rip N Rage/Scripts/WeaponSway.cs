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
    
    void Start()
    {
        startPos = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (!controller.IsGrounded) return;

        if (controller.MovementInput.y != 0 || controller.MovementInput.x != 0)
        {
            timer += Time.deltaTime * swaySpeed;
            
            horizontalTimer += Time.deltaTime * horizontalSwaySpeed;
            
            transform.localPosition = new Vector3(startPos.x + Mathf.Sin(horizontalTimer) * horizontalSwayAmount, 
            startPos.y + Mathf.Sin(timer) * swayAmount, 
            transform.localPosition.z);
        }
        else
        {
            timer = 0;
            horizontalTimer = 0;
            transform.localPosition = new Vector3(Mathf.Lerp(transform.localPosition.x, startPos.x, Time.deltaTime * horizontalSwaySpeed), Mathf.Lerp(transform.localPosition.y, startPos.y,
            Time.deltaTime * swaySpeed), transform.localPosition.z);
        }
    }
}
