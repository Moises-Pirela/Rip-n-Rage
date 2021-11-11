using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    [RequireComponent(typeof(AIAgent))]
    public class BehaviorTreeOwner : MonoBehaviour
    {
        public BehaviorTree tree;

        private AIAgent myAgent;

        private void Awake()
        {
            myAgent = GetComponent<AIAgent>();
        }

        void Start()
        {
            tree = tree.Clone();
            tree.Bind(myAgent);
        }
        
        
        void Update()
        {
            if(myAgent.aiState.HealthState != EntityHealthState.DEAD)
                tree.rootNode.Update();
        }
    }    
}