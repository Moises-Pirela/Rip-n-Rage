using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    [RequireComponent(typeof(AIAgent))]
    public class BehaviorTreeOwner : MonoBehaviour
    {
        public BehaviorTree tree;
        
        void Start()
        {
            tree = tree.Clone();
            tree.Bind(GetComponent<AIAgent>());
        }
        
        
        void Update()
        {
            tree.rootNode.Update();
        }
    }    
}