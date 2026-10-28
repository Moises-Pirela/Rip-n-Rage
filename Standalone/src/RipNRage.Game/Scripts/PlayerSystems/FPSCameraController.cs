using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/PlayerSystems/FPSCameraController.cs.
// DOTween's DOLookAt is provided by the engine's tween extensions; Cursor is the engine's.
public class FPSCameraController : MonoBehaviour
{
    public static FPSCameraController instance;
    
    [SerializeField] public Transform playerCamera;
    public Transform orientation;

    [Range(0, 10)] [SerializeField] public float sensitivityMultiplier = 1;
    [SerializeField] public float sensitivity = 5;
    private float xRotation;
    private float desiredX;

    private PlayerInputActions _inputActions;

    [Space(100)] [Header("Camera animation data")] [SerializeField]
    public float zSmoothLerp = 12;

    [SerializeField] public float zSwayAngle = 1.5f;
    [SerializeField] public float zSwayMultiplier = -50f;
    [SerializeField] public float zSmoothSway = 20f;

    protected override void Awake()
    {
        instance = this;
    }

    protected override void Start()
    {
        _inputActions = GetComponent<PlayerInputActions>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    protected override void Update()
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

        float zSway = Mathf.Clamp(_inputActions.MovementInput.x * zSwayMultiplier * Time.smoothDeltaTime * zSmoothSway,
            -zSwayAngle, zSwayAngle);

        zSway = Mathf.LerpAngle(z, zSway, Time.deltaTime * zSmoothLerp);

        localRotation = Quaternion.Euler(xRotation, desiredX,
            _inputActions.MovementInput.x != 0 ? zSway : zLerp); //apply camera tilt

        playerCamera.transform.localRotation = localRotation;

        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }

    /// <summary>Save/load support (standalone addition): sets the view yaw/pitch directly.</summary>
    public void SetLook(float yaw, float pitch)
    {
        xRotation = Mathf.Clamp(pitch, -90f, 90f);
        desiredX = yaw;
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, desiredX, 0f);
        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }

    public float Yaw => playerCamera.transform.localRotation.eulerAngles.y;
    public float Pitch => xRotation;

    public void ForceLookRotation(Vector3 lookRotation)
    {
        playerCamera.transform.DOLookAt(lookRotation, 0.15f);
        orientation.transform.DOLookAt(lookRotation, 0.15f);
        //playerCamera.transform.localRotation = Quaternion.LookRotation(lookRotation);
        //orientation.transform.localRotation = Quaternion.LookRotation(lookRotation);
    }

}
