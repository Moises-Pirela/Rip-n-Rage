using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class PlayerController : InputHandler
{
    public Transform playerCamera;
    public Transform orientation;
    public Transform feet;
    private Rigidbody rigidbody;
    private float xRotation;
    [SerializeField] private float sensitivity = 5;
    [Range(0, 10)] public float sensitivityMultiplier = 1;
    public float movementSpeed = 4500; //Equal to force to apply
    public float maxSpeed = 20;
    public LayerMask groundLayerMask;

    public float counterMovement = 0.175f; //Breaking force
    private float threshold = 0.5f;
    public float maxSlopeAngle = 35f;

    private Vector3 crouchScale = new Vector3(1, 0.5f, 1);
    private Vector3 playerScale;
    public float slideForce;
    public float slideCounterMovement = 0.2f;
    private float jumpCooldown = 0.25f;
    public float jumpForce = 550f;

    private Vector3 normalVector = Vector3.up;
    private Vector3 wallNormalDirection;
    private CharacterController character;

    [SerializeField] private PlayerInventory playerInventory;

    void Awake()
    {
        base.Awake();
        rigidbody = GetComponent<Rigidbody>();
        character = GetComponent<CharacterController>();
    }

    void Start()
    {
        playerScale = transform.localScale;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void FixedUpdate()
    {
        CheckForGround();

        ApplyMovement();

        if (IsJumping && CanJump) Jump();
        
        if(IsSliding)
            StartCoroutine(Slide());
    }

    void Update()
    {
        Look();
        HandleFire();
        HandleAltFire();
    }

    void HandleFire()
    {
        if (!IsShooting) return;

        playerInventory.Fire();

        IsShooting = false;
    }

    void HandleAltFire()
    {
        if (!IsAltFiring) return;

        playerInventory.AltFire();

        IsAltFiring = false;
    }

    void ApplyMovement()
    {
        if (IsSliding) return;
        
        var x = 0f;
        var y = 1f;
        x = MovementInput.x;
        y = MovementInput.y;

        Vector2 magnitud = FindVelocityRelativeToLookDirection();

        float xMagnitude = magnitud.x, yMagnitud = magnitud.y;

        //CounterMovement(x, y, magnitud);

        float maxSpeed = this.maxSpeed;

        //rigidbody.AddForce(Vector3.down * 10);

        // if (IsSliding && IsGrounded && CanJump)
        // {
        //     rigidbody.AddForce(Vector3.down * Time.deltaTime * 3000);
        //     return;
        // }

        if (x > 0 && xMagnitude > maxSpeed) x = 0;
        if (x < 0 && xMagnitude < -maxSpeed) x = 0;
        if (y > 0 && yMagnitud > maxSpeed) y = 0;
        if (y > 0 && yMagnitud > maxSpeed) y = 0;

        float airControlMultiplier = 1f;
        float forwardMomentum = 1f;

        if (!IsGrounded)
        {
            airControlMultiplier = 0.4f;
            forwardMomentum = 0.5f;
        }

        if (!IsGrounded || IsSliding) forwardMomentum = 0;

        float directionalMovementSpeed = MovementInput.y > 0 ? movementSpeed : movementSpeed * 0.8f;

        // rigidbody.AddForce(orientation.transform.forward * y * directionalMovementSpeed * airControlMultiplier, ForceMode.VelocityChange); // );
        // rigidbody.AddForce(orientation.transform.right * x * (movementSpeed * 0.7f) * airControlMultiplier, ForceMode.VelocityChange);

        // CharacterController character = GetComponent<CharacterController>();

        character.Move(orientation.transform.forward * y * directionalMovementSpeed * Time.deltaTime);
        character.Move(orientation.transform.right * x * directionalMovementSpeed * Time.deltaTime);

        // transform.position += orientation.transform.forward * y * movementSpeed * Time.deltaTime; 
        // transform.position += orientation.transform.right * x * movementSpeed * Time.deltaTime;
    }

    private void Jump()
    {
        if (IsGrounded)
        {
            rigidbody.constraints = RigidbodyConstraints.None;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            CanJump = false;

            //Add jump forces
            rigidbody.AddForce(Vector3.up * jumpForce * 1.5f);
            rigidbody.AddForce(normalVector * jumpForce * 0.5f);

             //rigidbody.transform.position += Vector3.up * jumpForce * Time.deltaTime;
            // rigidbody.transform.position += normalVector * jumpForce * 0.5f * Time.deltaTime;

            //If jumping while falling, reset y velocity.
            Vector3 vel = rigidbody.velocity;
            if (rigidbody.velocity.y < 0.5f)
                rigidbody.velocity = new Vector3(vel.x, 0, vel.z);
            else if (rigidbody.velocity.y > 0)
                rigidbody.velocity = new Vector3(vel.x, vel.y / 2, vel.z);

            // Invoke(nameof(ResetJump), jumpCooldown);
        }
    }

    private void ResetJump()
    {
        CanJump = false;
    }
    private IEnumerator Slide()
    {
        if (!IsSliding) yield break;

        //playerCamera.localPosition = Vector3.Lerp(transform.localPosition, new Vector3(0,0.4f,0), 1f * Time.deltaTime);
        float height = transform.GetComponent<CapsuleCollider>().height;
        transform.GetComponent<CapsuleCollider>().height = Mathf.Lerp(height, 1f, 0.5f);
        //transform.position = Vector3.Lerp(transform.position, new Vector3(transform.position.x, transform.position.y - 0.5f, transform.position.z), 1f * Time.deltaTime);
        
        if (IsGrounded)
        {
            rigidbody.AddForce(orientation.transform.forward * slideForce, ForceMode.Impulse);
            //rigidbody.position += orientation.transform.forward * slideForce * Time.deltaTime;
        }

        yield return new WaitForSeconds(0.5f);

        IsSliding = false;
        StopSliding();
    }

    private void StopSliding()
    {
        //playerCamera.localPosition = Vector3.Lerp(transform.localPosition, new Vector3(0,0.8f,0), 1f * Time.deltaTime);
        //transform.localScale = playerScale;
        float height = transform.GetComponent<CapsuleCollider>().height;
        transform.GetComponent<CapsuleCollider>().height = Mathf.Lerp(height, 1.8f, 0.5f);
        //transform.position = new Vector3(transform.position.x, transform.position.y + 0.5f, transform.position.z);
    }

    private float desiredX;
    private void Look()
    {

        float mouseX = LookInput.x * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;
        float mouseY = LookInput.y * sensitivity * Time.fixedDeltaTime * sensitivityMultiplier;

        
        Vector3 rot = playerCamera.transform.localRotation.eulerAngles;
        desiredX = rot.y + mouseX;

        
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        float z = playerCamera.transform.localRotation.eulerAngles.z;

        float zLerp = Mathf.LerpAngle(z, 0, Time.deltaTime * 12);

        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, desiredX,
        MovementInput.x != 0 ? 
        MovementInput.x * -5 * Time.smoothDeltaTime * 20  : zLerp); //apply camera tilt
        
        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }
    public Vector2 FindVelocityRelativeToLookDirection()
    {
        float lookAngle = orientation.transform.eulerAngles.y;
        float moveAngle = Mathf.Atan2(rigidbody.velocity.x, rigidbody.velocity.z) * Mathf.Rad2Deg;

        float u = Mathf.DeltaAngle(lookAngle, moveAngle);
        float v = 90 - u;

        float magnitue = rigidbody.velocity.magnitude;
        float yMag = magnitue * Mathf.Cos(u * Mathf.Deg2Rad);
        float xMag = magnitue * Mathf.Cos(v * Mathf.Deg2Rad);

        return new Vector2(xMag, yMag);
    }

    private bool IsFloor(Vector3 v)
    {
        float angle = Vector3.Angle(Vector3.up, v);
        return angle < maxSlopeAngle;
    }

    private void CounterMovement(float x, float y, Vector2 mag)
    {
        if (!IsGrounded || IsJumping) return;

        // if (IsSliding)
        // {
        //     rigidbody.AddForce(movementSpeed * Time.deltaTime * -rigidbody.velocity.normalized * slideCounterMovement);
        //     return;
        // }
        
        if (Math.Abs(mag.x) > threshold && Math.Abs(x) < 0.05f || (mag.x < -threshold && x > 0) || (mag.x > threshold && x < 0))
        {
            rigidbody.AddForce(movementSpeed * orientation.transform.right * Time.deltaTime * -mag.x * counterMovement);
        }
        if (Math.Abs(mag.y) > threshold && Math.Abs(y) < 0.05f || (mag.y < -threshold && y > 0) || (mag.y > threshold && y < 0))
        {
            rigidbody.AddForce(movementSpeed * orientation.transform.forward * Time.deltaTime * -mag.y * counterMovement);
        }
    }

    private void CheckForGround()
    {
        IsGrounded = Physics.Raycast(feet.transform.position, -feet.transform.up,  out RaycastHit hit, 1f,groundLayerMask);
    }
}
