using System;
using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;

// Ported from Assets/_AI_BehaviorTree/Scripts/AgentVisionSensor.cs.
public class AgentVisionSensor : MonoBehaviour, ITriggerListener
{
    [SerializeField] public BehaviorTreeOwner _treeOwner;
    
    public void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _treeOwner.tree.blackboard.playerObject = other.gameObject;
    }

    public void OnTriggerExit(Collider other) { }
}
