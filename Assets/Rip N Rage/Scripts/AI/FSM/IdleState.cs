using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "", menuName = "FSM/IdleState")]
public class IdleState : FSMState
{
    public override void UpdateState()
    {

    }

    public override bool EvaluateState()
    {
        return true;
    }
}