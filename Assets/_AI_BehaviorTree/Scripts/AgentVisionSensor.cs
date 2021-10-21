using System;
using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEngine;

public class AgentVisionSensor : MonoBehaviour
{
    [SerializeField] protected BehaviorTreeOwner _treeOwner;
    
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _treeOwner.tree.blackboard.playerObject = other.gameObject;
    }
}
