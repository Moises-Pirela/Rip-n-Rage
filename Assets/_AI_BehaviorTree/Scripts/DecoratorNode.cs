using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    public abstract class DecoratorNode : BaseNode
    {
        [HideInInspector] public BaseNode child;
        
        public override BaseNode Clone()
        {
            DecoratorNode node = Instantiate(this);
            node.child = child.Clone();

            return node;
        }
    }
}
