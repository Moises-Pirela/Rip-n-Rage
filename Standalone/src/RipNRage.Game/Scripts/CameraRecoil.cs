using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/CameraRecoil.cs (unchanged logic).
public class CameraRecoil : MonoBehaviour
{
    private float rotationSpeed = 6f;
    private float returnSpeed = 25f;
    
    [Header("Camera Shake:")]
    public Vector3 recoilCameraShake = new Vector3(0.5f, 0.5f, 1.5f);

    private Vector3 currentRotation;
    
    private Vector3 Rot;

    protected override void Awake()
    {
        EnragedInjector<CameraRecoil>.SetInstance(this);
    }

    protected override void FixedUpdate()
    {   
        currentRotation = Vector3.Lerp(currentRotation, Vector3.zero, returnSpeed * Time.deltaTime);
        Rot = Vector3.Slerp(Rot, currentRotation, rotationSpeed * Time.deltaTime);
        transform.localRotation = Quaternion.Euler(Rot);
    }

    
    public void Shake(float strength = 15)
    {
        var yRotation = Random.Range(-recoilCameraShake.y, recoilCameraShake.y) * strength;
        var zRotation = Random.Range(-recoilCameraShake.z, recoilCameraShake.z) * strength;
        currentRotation += new Vector3(-recoilCameraShake.x, yRotation, zRotation);
    }

    public void ApplyRecoil(WeaponRecoil currentWeapon)
    {
        // var recoil = currentWeapon.recoil;
        // rotationSpeed = currentWeapon.recoilSpeed;
        // returnSpeed = currentWeapon.recoilRecoverySpeed;
        //
        // float yRotation = Random.Range(-recoil.y, recoil.y);
        // float zRotation = Random.Range(-recoil.z, recoil.z);
        // currentRotation += new Vector3(-recoil.x, yRotation, zRotation);
    }
}
