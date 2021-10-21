using System.Collections;
using System.Collections.Generic;
using AI.BehaviorTree;
using UnityEngine;


namespace AI.BehaviorTree
{
    public class WaitNode : ActionNode
    {
        public float duration = 1;

        private float startTime;
        
        protected override void OnStart()
        {
            startTime = Time.time;
        }

        protected override void OnStop()
        {
            //throw new System.NotImplementedException();
        }

        protected override State OnUpdate()
        {
            if (Time.time - startTime > duration)
            {
                return State.SUCCESS;
            }

            return State.RUNNING;
        }
    }    
}