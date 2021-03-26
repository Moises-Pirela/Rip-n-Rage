using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FPSCameraController : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    public Transform orientation;

    [Range(0, 10)] [SerializeField] private float sensitivityMultiplier = 1;
    [SerializeField] private float sensitivity = 5;
    private float xRotation;
    private float desiredX;
    private InputHandler inputHandler;

    private void Start()
    {
        inputHandler = GetComponent<InputHandler>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Look();
    }

    private void Look()
    {

        float mouseX = inputHandler.LookInput.x * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;
        float mouseY = inputHandler.LookInput.y * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;


        Vector3 rot = playerCamera.transform.localRotation.eulerAngles;
        desiredX = rot.y + mouseX;


        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        float z = playerCamera.transform.localRotation.eulerAngles.z;

        float zLerp = Mathf.LerpAngle(z, 0, Time.deltaTime * 12);

        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, desiredX,
        inputHandler.MovementInput.x != 0 ?
        inputHandler.MovementInput.x * -5 * Time.smoothDeltaTime * 20 : zLerp); //apply camera tilt

        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }

}
