using UnityEngine;

namespace AI.BehaviorTree
{
    public class AttackNode : ActionNode
    {
        protected override void OnStart()
        {
            
        }

        protected override void OnStop()
        {
            
        }

        protected override State OnUpdate()
        {
            agent.Attack(blackboard.MoveToTarget);
            return State.SUCCESS;
        }
    }
}