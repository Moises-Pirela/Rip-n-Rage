using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("AI Settings")]
    public float MovementSpeed;
    public float AttackRange;
    public float MeleeAttackRange;
    public float TurnSpeed;
    
    [Space(10)]
    [Header("Animation Settings")]
    public EnemyAnimationData EnemyAnimationData;
}
