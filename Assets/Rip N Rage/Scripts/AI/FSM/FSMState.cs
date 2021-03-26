using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ExecutionState 
{
    NONE,
    RUNNING,
    COMPLETED,
    FAILED
}
public abstract class FSMState : ScriptableObject
{
    public ExecutionState ExecutionState {get; protected set;}

    public virtual void OnEnable()
    {
        ExecutionState = ExecutionState.NONE;
    }

    public virtual void EnterState()
    {
        ExecutionState = ExecutionState.RUNNING;
    }

    public virtual void ExitState()
    {
        ExecutionState = ExecutionState.COMPLETED;
    }

    public virtual void FailState()
    {
        ExecutionState = ExecutionState.FAILED;
    }

    public abstract void UpdateState();

    public abstract bool EvaluateState();
}
