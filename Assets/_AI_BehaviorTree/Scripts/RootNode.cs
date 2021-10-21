using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEngine;

namespace AI.BehaviorTree
{
    public class RootNode : BaseNode
    {
        public BaseNode child;

        public override BaseNode Clone()
        {
            RootNode node = Instantiate(this);
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
}