using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
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
}