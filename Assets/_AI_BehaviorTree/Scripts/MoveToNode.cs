using UnityEngine;

namespace AI.BehaviorTree
{
    public class MoveToNode : ActionNode
    {
        protected override void OnStart()
        {
            
        }

        protected override void OnStop()
        {
            
        }

        protected override State OnUpdate()
        {
            agent.Move(blackboard.MoveToTarget);
            
            if(agent.HasReachedLocation())
                return State.SUCCESS;
            
            return State.RUNNING;
        }
    }
}