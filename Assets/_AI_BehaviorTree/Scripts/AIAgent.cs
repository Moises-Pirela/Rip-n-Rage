using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AIAgent : MonoBehaviour, IEnemyAI
{
    public NavMeshAgent _navMeshAgent;

    public AnimationStateController AnimationStateController;

    protected Vector3 targetLocation;

    private void Update()
    {
        if (_navMeshAgent.isStopped && AnimationStateController.AnimationState == AnimationState.MOVING)
        {
            AnimationStateController.AnimationState = AnimationState.IDLE;
        }
    }

    public void Move(Vector3 targetLocation)
    {
        this.targetLocation = targetLocation;
        
        _navMeshAgent.SetDestination(targetLocation);

        AnimationStateController.AnimationState = AnimationState.MOVING;
    }

    public void Attack(Vector3 targetLocation)
    {
        StopAgent();
        AnimationStateController.AnimationState = AnimationState.ATTACKING;
    }

    public void Die()
    {
        AnimationStateController.AnimationState = AnimationState.DYING;
    }

    public void StopAgent()
    {
        _navMeshAgent.isStopped = true;
        _navMeshAgent.velocity = Vector3.zero;
    }

    private void AllowAgentToMove()
    {
        _navMeshAgent.isStopped = false;
        _navMeshAgent.velocity = _navMeshAgent.desiredVelocity;
    }

    public bool HasReachedLocation()
    {
        float distanceToTargetLocation = Vector3.Distance(targetLocation, transform.position);

        NavMeshPath path = _navMeshAgent.path;

        float closestCornerDistance = 0;

        foreach (var corner in path.corners)
        {
            if (Vector3.Distance(corner, transform.position) <= closestCornerDistance)
            {   
                closestCornerDistance = Vector3.Distance(corner, transform.position);
            }
        }

        if (_navMeshAgent.remainingDistance + closestCornerDistance <= 5f)
        {
            StopAgent();
            return true;
        }
        
        AllowAgentToMove();
        
        return false;
    }
}
