using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
        
        #if UNITY_EDITOR

        public BaseNode CreateNode(Type type)
        {
            BaseNode node = ScriptableObject.CreateInstance(type) as BaseNode;

            node.name = type.Name;
            node.guid = GUID.Generate().ToString();
            
            Undo.RecordObject(this, "Behavior Tree (CreateNode)");
            nodes.Add(node);

            if (Application.isPlaying) return null;
            
            AssetDatabase.AddObjectToAsset(node,this);
            Undo.RegisterCompleteObjectUndo(node, "Behavior Tree (CreateNode)");
            AssetDatabase.SaveAssets();

            return node;
        }

        public void DeleteNode(BaseNode node)
        {
            Undo.RecordObject(this, "Behavior Tree (RemoveNode)");
            nodes.Remove(node);
            Undo.DestroyObjectImmediate(node);
            AssetDatabase.SaveAssets();
        }

        public void AddChild(BaseNode parentNode, BaseNode childNode)
        {
            RootNode rootNode = parentNode as RootNode;

            if (rootNode)
            {
                Undo.RecordObject(rootNode, "Behavior Tree(AddChild)");
                rootNode.child = childNode;
                EditorUtility.SetDirty(rootNode);
            }
            
            SelectorNode selectorNode = parentNode as SelectorNode;

            if (selectorNode)
            {
                Undo.RecordObject(selectorNode, "Behavior Tree(AddChild)");
                selectorNode.children.Add(childNode);
                EditorUtility.SetDirty(selectorNode);
            }

            DecoratorNode decoratorNode = parentNode as DecoratorNode;

            if (decoratorNode)
            {
                Undo.RecordObject(decoratorNode, "Behavior Tree(AddChild)");
                decoratorNode.child = childNode;
                EditorUtility.SetDirty(decoratorNode);
            }
            
            CompositeNode compositeNode = parentNode as CompositeNode;

            if (compositeNode)
            {
                Undo.RecordObject(compositeNode, "Behavior Tree(AddChild)");
                compositeNode.children.Add(childNode);
                EditorUtility.SetDirty(compositeNode);
            }
        }
        
        public void RemoveChild(BaseNode parentNode, BaseNode childNode)
        {
            RootNode rootNode = parentNode as RootNode;

            if (rootNode)
            {
                Undo.RecordObject(rootNode, "Behavior Tree(RemoveChild)");
                rootNode.child = null;
                EditorUtility.SetDirty(rootNode);
            }
            
            SelectorNode selectorNode = parentNode as SelectorNode;

            if (selectorNode)
            {
                Undo.RecordObject(selectorNode, "Behavior Tree(AddChild)");
                selectorNode.children.Remove(childNode);
                EditorUtility.SetDirty(selectorNode);
            }
            
            DecoratorNode decoratorNode = parentNode as DecoratorNode;

            if (decoratorNode)
            {
                Undo.RecordObject(decoratorNode, "Behavior Tree(RemoveChild)");
                decoratorNode.child = null;
                EditorUtility.SetDirty(decoratorNode);
            }
            
            CompositeNode compositeNode = parentNode as CompositeNode;

            if (compositeNode)
            {
                Undo.RecordObject(compositeNode, "Behavior Tree(RemoveChild)");
                compositeNode.children.Remove(childNode);
                EditorUtility.SetDirty(compositeNode);
            }
        }
#endif
        
        public List<BaseNode> GetChildren(BaseNode parentNode)
        {
            List<BaseNode> children = new List<BaseNode>();
            
            RootNode rootNode = parentNode as RootNode;
            DecoratorNode decoratorNode = parentNode as DecoratorNode;
            CompositeNode compositeNode = parentNode as CompositeNode;
            SelectorNode selectorNode = parentNode as SelectorNode;

            if (rootNode && rootNode.child != null)
            {
                children.Add(rootNode.child);
            }
            if (decoratorNode && decoratorNode.child != null)
            {
                children.Add(decoratorNode.child);
            }
            if (compositeNode && compositeNode.children != null)
            {
                return compositeNode.children;
            }
            if (selectorNode && selectorNode.children != null)
            {
                return selectorNode.children;
            }

            return children;
        }
        
        public void Traverse(BaseNode node, Action<BaseNode> visitor)
        {
            if (node)
            {
                visitor.Invoke(node);
                var children = GetChildren(node);
                children.ForEach((n) => Traverse(n,visitor));
            }
        }
        
        public void Bind(AIAgent agent)
        {
            Traverse(rootNode, node =>
            {
                node.blackboard = blackboard;
                node.agent = agent;
            });
        }
        
        public BehaviorTree Clone()
        {
            BehaviorTree tree = Instantiate(this);
            
            tree.rootNode = tree.rootNode.Clone();

            tree.nodes = new List<BaseNode>();
            
            Traverse(tree.rootNode, (n) =>
            {
                tree.nodes.Add(n);
            });
            return tree;
        }
    }
}

