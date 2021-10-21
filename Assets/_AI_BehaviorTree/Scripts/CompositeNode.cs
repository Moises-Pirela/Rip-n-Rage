using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    public abstract class CompositeNode : BaseNode
    {
        [HideInInspector] public List<BaseNode> children = new List<BaseNode>();
        
        public override BaseNode Clone()
        {
            CompositeNode node = Instantiate(this);
            
            node.children = children.ConvertAll(c => c.Clone());

            return node;
        }
    }
}
