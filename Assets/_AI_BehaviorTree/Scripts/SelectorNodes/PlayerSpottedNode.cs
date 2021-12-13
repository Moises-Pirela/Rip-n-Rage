using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    public class PlayerSpottedNode : SelectorNode
    {
        protected override bool Condition()
        {
            if (blackboard.playerObject == null)
                return false;
            
            blackboard.MoveToTarget = blackboard.playerObject.transform.position;
            return true;

        }
    }
}
