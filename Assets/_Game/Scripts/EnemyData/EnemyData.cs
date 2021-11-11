using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("AI Settings")]
    public float movementSpeed;
    public float attackRange;
    public float meleeAttackRange;
    public float turnSpeed;
    public float fireRate;

    [Space(5)] [Header("Enemy status")] 
    public float maxHealth;
    public float maxArmor;
    
    [Space(10)]
    [Header("Animation Settings")]
    public EnemyAnimationData enemyAnimationData;
}
