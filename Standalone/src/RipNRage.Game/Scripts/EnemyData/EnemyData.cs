using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Enemy/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("AI Settings")]
    public float movementSpeed;
    public float attackRange;
    public float meleeAttackRange;
    public float turnSpeed;
    public float fireRate;

    [Space(5)] [Header("Attack readability (standalone additions)")]
    /// <summary>Seconds the enemy telegraphs (flash + aim glow + sound) before the first shot.</summary>
    public float attackWindup = 0.35f;
    /// <summary>Shots per attack.</summary>
    public int burstCount = 3;
    /// <summary>Seconds between the shots of a burst.</summary>
    public float burstInterval = 0.09f;
    /// <summary>Seconds a hit interrupts the enemy (stops moving, delays the shot).</summary>
    public float staggerTime = 0.25f;
    /// <summary>Seconds between spotting the player and the first reaction.</summary>
    public float alertDelay = 0.2f;

    [Space(5)] [Header("Enemy status")] 
    public float maxHealth;
    public float maxArmor;
    
    [Space(10)]
    [Header("Animation Settings")]
    public EnemyAnimationData enemyAnimationData;
}
