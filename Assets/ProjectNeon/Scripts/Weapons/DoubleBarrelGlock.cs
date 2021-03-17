using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoubleBarrelGlock : Weapon
{
    Transform playerCam;

    public override void Equip(Transform camera)
    {
        playerCam = camera;
    }
    public override void Fire()
    {
        Shoot();
    }
    public override void AltFire()
    {
         for (int i = 0; i < 2; i++)
            Shoot();
    }

    private void Shoot()
    {
        if (Physics.Raycast(playerCam.transform.position + new Vector3(0.02f,0,0) , playerCam.transform.forward, 10))
        {
            Debug.Log("Shoot");
        }
        if (Physics.Raycast(playerCam.transform.position + new Vector3(-0.02f,0,0), playerCam.transform.forward, 10))
        {
            Debug.Log("Shoot");
        }
    }
}
