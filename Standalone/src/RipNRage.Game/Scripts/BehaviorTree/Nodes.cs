using System;
using System.Collections;
using System.Collections.Generic;

// Ported node classes from Assets/_AI_BehaviorTree/Scripts (ActionNode, CompositeNode, DecoratorNode,
// RootNode, SelectorNode, SequencerNode, RepeatNode, WaitNode, DebugLogNode, MoveToNode,
// RandomLocationNode, ActionNodes/*, SelectorNodes/*). Logic unchanged; Instantiate -> Clone.
namespace AI.BehaviorTree
{
    public abstract class ActionNode : BaseNode
    {
    }

    public abstract class CompositeNode : BaseNode
    {
        [HideInInspector] public List<BaseNode> children = new List<BaseNode>();
        
        public override BaseNode Clone()
        {
            CompositeNode node = (CompositeNode)MemberwiseClone();
            
            node.children = children.ConvertAll(c => c.Clone());

            return node;
        }
    }

    public abstract class DecoratorNode : BaseNode
    {
        [HideInInspector] public BaseNode child;
        
        public override BaseNode Clone()
        {
            DecoratorNode node = (DecoratorNode)MemberwiseClone();
            node.child = child.Clone();

            return node;
        }
    }

    public class RootNode : BaseNode
    {
        public BaseNode child;

        public override BaseNode Clone()
        {
            RootNode node = (RootNode)MemberwiseClone();
            node.child = child.Clone();

            return node;
        }

        protected override void OnStart()
        {
           
        }

        protected override void OnStop()
        {
           
        }

        protected override State OnUpdate()
        {
            return child.Update();
        }
    }

    public abstract class SelectorNode : BaseNode
    {
        public List<BaseNode> children = new List<BaseNode>();
        protected BaseNode currentChild;
        
        protected override void OnStart()
        {
            currentChild = null;
        }

        protected override void OnStop()
        {
            currentChild = null;
        }

        protected override State OnUpdate()
        {
            currentChild = Condition() ? children[0] : children[1];

            currentChild.Update();
            
            return State.SUCCESS;
        }
        
        public override BaseNode Clone()
        {
            SelectorNode node = (SelectorNode)MemberwiseClone();
            
            node.children = children.ConvertAll(c => c.Clone());

            return node;
        }

        protected abstract bool Condition();
    }

    public class SequencerNode : CompositeNode
    {
        private int currentChildIndex;
        protected override void OnStart()
        {
            currentChildIndex = 0;
        }

        protected override void OnStop()
        {
            
        }

        protected override State OnUpdate()
        {
            var child = children[currentChildIndex];

            switch (child.Update())
            {
                case State.RUNNING:
                    return  State.RUNNING;
                
                case State.FAILURE:
                    return State.FAILURE;
                
                case State.SUCCESS:
                    currentChildIndex++;
                    break;
            }

            return currentChildIndex == children.Count ? State.SUCCESS : State.RUNNING;
        }
    }

    public class RepeatNode : DecoratorNode
    {
        protected override void OnStart()
        {
           
        }

        protected override void OnStop()
        {
           
        }

        protected override State OnUpdate()
        {
            child.Update();

            return State.RUNNING;
        }
    }

    public class WaitNode : ActionNode
    {
        public float duration = 1;

        private float startTime;
        
        protected override void OnStart()
        {
            startTime = Time.time;
        }

        protected override void OnStop()
        {
            //throw new System.NotImplementedException();
        }

        protected override State OnUpdate()
        {
            if (Time.time - startTime > duration)
            {
                return State.SUCCESS;
            }

            return State.RUNNING;
        }
    }

    class DebugLogNode : ActionNode
    {
        public string message;
        protected override void OnStart()
        {
            Debug.Log($"OnStart{message}");
        }

        protected override void OnStop()
        {
            Debug.Log($"OnStop{message}");
        }

        protected override State OnUpdate()
        {
            Debug.Log($"OnUpdate{message}");
            return State.SUCCESS;
        }
    }

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

    public class RandomLocationNode : ActionNode
    {
        public float radius;
        public float minDistance;
        
        protected override void OnStart()
        {
            
        }

        protected override void OnStop()
        {
        }

        protected override State OnUpdate()
        {
            Vector3 randomLocation = Random.insideUnitSphere * radius;

            randomLocation += agent.transform.position;

            if (Vector3.Distance(randomLocation, agent.transform.position) <= minDistance)
                return State.RUNNING;

            NavMeshHit hit;

            NavMesh.SamplePosition(randomLocation, out hit, 5, 1);

            blackboard.MoveToTarget = hit.position;
            
            return State.SUCCESS;
        }
    }

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

    public class InAttackRangeNode : SelectorNode
    {
        public float attackRange = 10;
        protected override bool Condition()
        {
            if (!(Vector3.Distance(agent.transform.position, blackboard.MoveToTarget) <= attackRange)) return false;
            
            agent.navMeshAgent.isStopped = true;
            
            return true;

        }
    }

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

public class UpdatePlayerPositionNode : AI.BehaviorTree.ActionNode
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
