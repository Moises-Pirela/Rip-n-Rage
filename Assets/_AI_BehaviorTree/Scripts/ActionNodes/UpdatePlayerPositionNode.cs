using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEngine;

public class UpdatePlayerPositionNode : ActionNode
{
    protected override void OnStart()
    {
        
    }

    protected override void OnStop()
    {

    }

    protected override State OnUpdate()
    {
        blackboard.MoveToTarget = blackboard.playerObject.transform.position;
        
        return State.SUCCESS;
    }
}
