using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AI.BehaviorTree
{
    [Serializable]
    public class Blackboard
    {
        public Vector3 MoveToTarget;
        public GameObject MoveToObject;

        public GameObject playerObject;
    }    
}

