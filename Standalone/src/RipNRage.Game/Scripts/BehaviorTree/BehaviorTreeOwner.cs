using System;
using System.Collections;
using System.Collections.Generic;

namespace AI.BehaviorTree
{
    [RequireComponent(typeof(AIAgent))]
    public class BehaviorTreeOwner : MonoBehaviour
    {
        public BehaviorTree tree;

        private AIAgent myAgent;

        protected override void Awake()
        {
            myAgent = GetComponent<AIAgent>();
        }

        protected override void Start()
        {
            tree = tree.Clone();
            tree.Bind(myAgent);
        }
        
        
        protected override void Update()
        {
            if(myAgent.aiState.HealthState != EntityHealthState.DEAD)
                tree.rootNode.Update();
        }
    }    
}
