using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "RageData", fileName = "RageData")]
public class RageStateData : ScriptableObject
{
   public float damageDealtModifier = 1;
   public float damageTakenModifer = 1;
   public float movementSpeedModifier = 1;
   public float ammoDropChanceModifier = 1;
   public float healthDropChanceModifier = 1;
}
