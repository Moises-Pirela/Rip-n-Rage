using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_AI_BehaviorTree/Scripts/BehaviorTree.cs. The UNITY_EDITOR graph-editing
// helpers were dropped; trees are built in code (see Content/EnemyContent.cs).
namespace AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "AI/Behavior Tree", fileName ="BehaviorTree")]
    public class BehaviorTree : ScriptableObject
    {
        public BaseNode rootNode;
        public BaseNode.State treeState = BaseNode.State.RUNNING;
        public List<BaseNode> nodes = new List<BaseNode>();
        public Blackboard blackboard;

        public BaseNode.State Update()
        {
            if (rootNode.state == BaseNode.State.RUNNING)
                return rootNode.Update();

            return treeState;
        }

        public List<BaseNode> GetChildren(BaseNode parentNode)
        {
            List<BaseNode> children = new List<BaseNode>();
            
            RootNode rootNode = parentNode as RootNode;
            DecoratorNode decoratorNode = parentNode as DecoratorNode;
            CompositeNode compositeNode = parentNode as CompositeNode;
            SelectorNode selectorNode = parentNode as SelectorNode;

            if (rootNode != null && rootNode.child != null) children.Add(rootNode.child);
            if (decoratorNode != null && decoratorNode.child != null) children.Add(decoratorNode.child);
            if (compositeNode != null) children.AddRange(compositeNode.children);
            if (selectorNode != null) children.AddRange(selectorNode.children);

            return children;
        }

        public void Traverse(BaseNode node, Action<BaseNode> visitor)
        {
            if (node == null) return;
            visitor.Invoke(node);
            foreach (var child in GetChildren(node)) Traverse(child, visitor);
        }

        public new BehaviorTree Clone()
        {
            BehaviorTree tree = (BehaviorTree)MemberwiseClone();
            tree.rootNode = rootNode.Clone();
            tree.nodes = new List<BaseNode>();
            Traverse(tree.rootNode, n => tree.nodes.Add(n));
            tree.blackboard = new Blackboard();
            return tree;
        }

        public void Bind(AIAgent agent)
        {
            Traverse(rootNode, node =>
            {
                node.agent = agent;
                node.blackboard = blackboard;
            });
        }
    }
}
