using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEngine;


namespace AI.BehaviorTree
{
    public class LookAtNode : ActionNode
    {
        protected override void OnStart()
        {
     
        }

        protected override void OnStop()
        {
     
        }

        protected override State OnUpdate()
        {
            if (agent.navMeshAgent.velocity.sqrMagnitude > Mathf.Epsilon)
            {
                agent.transform.rotation = Quaternion.LookRotation(-blackboard.MoveToTarget);
            }
            
            return State.SUCCESS;
        }
    }
}
