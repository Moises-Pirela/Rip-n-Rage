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

    [Space(5)] [Header("State tree (the values the behaviour tree's nodes carried)")]
    /// <summary>Seconds the enemy idles before picking the next wander point.</summary>
    public float wanderWait = 1f;
    /// <summary>Wander points are picked inside this radius ...</summary>
    public float wanderRadius = 15f;
    /// <summary>... and rejected when closer than this.</summary>
    public float wanderMinDistance = 10f;

    [Space(5)] [Header("Senses")]
    /// <summary>Metres the enemy sees (the old vision trigger's sphere).</summary>
    public float sightRange = 10f;
    /// <summary>Full field of view, degrees, about the body's facing.</summary>
    public float sightFov = 180f;
    /// <summary>Seconds a target stays known after it was last seen or heard.</summary>
    public float memory = 5f;
    /// <summary>Metres the enemy's own shot carries to other ears.</summary>
    public float noise = 30f;

    [Space(5)] [Header("Enemy status")] 
    public float maxHealth;
    public float maxArmor;
    
    [Space(10)]
    [Header("Animation Settings")]
    public EnemyAnimationData enemyAnimationData;

    /// <summary>Standalone addition: skeletons/&lt;name&gt;.json used to slice the sprite into a ragdoll on death (empty = death animation only).</summary>
    public string skeleton = "";
}
