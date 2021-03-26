using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FiniteStateMachine : MonoBehaviour
{
    [SerializeField] private FSMState[] states;
    
    private FSMState _currentState;
    private FSMState _prevState;
    public FSMState startingState;

    private void Start()
    {
        startingState = states[0];

        EnterState(startingState);
    }


    private void Update()
    {
        if (_currentState == null) return;

        Evaluate();

        _currentState.UpdateState();
    }

    private void EnterState(FSMState nextState)
    {
        if (nextState == null) return;

        _prevState = _currentState;

        _currentState = nextState;

        _currentState.EnterState();
    }

    private void Evaluate()
    {
        foreach(FSMState state in states)
        {
            if (!state.EvaluateState()) continue;

            EnterState(state);
        }
    }
}