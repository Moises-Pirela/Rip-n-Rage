using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AI.BehaviorTree
{
    public class RandomLocationNode : ActionNode
    {
        public float radius;
        public float minDistance;
        
        protected override void OnStart()
        {
            
        }

        protected override void OnStop()
        {
        }

        protected override State OnUpdate()
        {
            Vector3 randomLocation = Random.insideUnitSphere * radius;

            randomLocation += agent.transform.position;

            if (Vector3.Distance(randomLocation, agent.transform.position) <= minDistance)
                return State.RUNNING;

            NavMeshHit hit;

            NavMesh.SamplePosition(randomLocation, out hit, 5, 1);

            blackboard.MoveToTarget = hit.position;
            
            return State.SUCCESS;
        }
    }
}