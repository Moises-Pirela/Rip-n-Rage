using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    public class InAttackRangeNode : SelectorNode
    {
        public float attackRange = 10;
        protected override bool Condition()
        {
            if (!(Vector3.Distance(agent.transform.position, blackboard.MoveToTarget) <= attackRange)) return false;
            
            agent._navMeshAgent.isStopped = true;
            
            return true;

        }
    }
}
