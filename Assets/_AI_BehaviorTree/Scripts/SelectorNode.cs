using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace AI.BehaviorTree
{
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
            SelectorNode node = Instantiate(this);
            
            node.children = children.ConvertAll(c => c.Clone());

            return node;
        }

        protected abstract bool Condition();
    }
}