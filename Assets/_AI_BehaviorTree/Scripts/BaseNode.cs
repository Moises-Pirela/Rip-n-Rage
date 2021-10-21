using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace AI.BehaviorTree
{
    public abstract class BaseNode : ScriptableObject
    {
        public enum State
        {
            RUNNING,
            FAILURE,
            SUCCESS
        }

        [HideInInspector] public State state = State.RUNNING;
        [HideInInspector] public bool hasStarted = false;
        [HideInInspector] public string guid;
        [HideInInspector] public Vector2 graphPosition;
        [HideInInspector] public Blackboard blackboard;
        [HideInInspector] public AIAgent agent;
        [TextArea] public string description;

        public State Update()
        {
            if (!hasStarted)
            {
                OnStart();
                hasStarted = true;
            }

            state = OnUpdate();

            if (state != State.RUNNING)
            {
                OnStop();
                hasStarted = false;
            }

            return state;
        }

        public virtual BaseNode Clone()
        {
            return Instantiate(this);
        }

        protected abstract void OnStart();
        protected abstract void OnStop();
        protected abstract State OnUpdate();
    }
}
