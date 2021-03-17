using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum AmmoType {PISTOL, SHOTGUN, LAUNCHER}
public abstract class Weapon : MonoBehaviour
{
  public abstract void Equip(Transform camera);
  public abstract void Fire();
  public abstract void AltFire();
}
