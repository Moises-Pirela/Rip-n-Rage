using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class FPSCameraController : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    public Transform orientation;

    [Range(0, 10)] [SerializeField] private float sensitivityMultiplier = 1;
    [SerializeField] private float sensitivity = 5;
    private float xRotation;
    private float desiredX;

    private PlayerInputActions _inputActions;

    [Space(100)]
    [Header("Camera animation data")] 
    [SerializeField] private float zSmoothLerp = 12;
    [SerializeField] private float zSwayAngle = 1.5f;
    [SerializeField] private float zSwayMultiplier = -50f;
    [SerializeField] private float zSmoothSway = 20f;

    private void Start()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Look();
    }

    private void Look()
    {

        float mouseX = _inputActions.LookInput.x * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;
        float mouseY = _inputActions.LookInput.y * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;
        
        var localRotation = playerCamera.transform.localRotation;
        
        Vector3 rot = localRotation.eulerAngles;
        desiredX = rot.y + mouseX;


        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        float z = localRotation.eulerAngles.z;
        float x = localRotation.eulerAngles.x;

        float zLerp = Mathf.LerpAngle(z, 0, Time.deltaTime * zSmoothLerp);
        
        float zSway = Mathf.Clamp(_inputActions.MovementInput.x * zSwayMultiplier * Time.smoothDeltaTime * zSmoothSway, -zSwayAngle, zSwayAngle);
        
        zSway = Mathf.LerpAngle(z, zSway, Time.deltaTime * zSmoothLerp); 

        localRotation = Quaternion.Euler(xRotation, desiredX,
            _inputActions.MovementInput.x != 0 ? zSway : zLerp); //apply camera tilt
        
        playerCamera.transform.localRotation = localRotation;

        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }

}
