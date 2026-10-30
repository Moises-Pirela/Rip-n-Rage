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

        // corpses are not targets (standalone addition)
        var player = other.GetComponent<PlayerState>();
        if (player != null && player.IsDead) return;

        bool first = _treeOwner.tree.blackboard.playerObject == null;
        _treeOwner.tree.blackboard.playerObject = other.gameObject;
        if (first) _treeOwner.GetComponent<AIAgent>()?.OnAlert(); // readable "noticed you" reaction before engaging
    }

    public void OnTriggerExit(Collider other) { }
}
