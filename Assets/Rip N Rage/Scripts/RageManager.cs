using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum RageState {NORMAL, ENRAGED, DEPRESSED}
public class RageManager : MonoBehaviour
{
    public System.Action onRageMeterChange;
    public static RageManager instance;
    public RageState rageState;
    public float RageMeter
    {
        get { return _rageMeter; }
        set
        {
            _rageMeter = Mathf.Clamp(value, 0, 1);
            UpdateRageState();
            onRageMeterChange();
        }
    }

    private float _rageMeter;

    private const float depressionPerTick = 0.2f;
    private const float ragePerEnemyHit = 0.1f;
    private const float defaultRage = 0.5f;
    private const float timeToResetHitTarget = 1f;
    private const float timeToTickDepression = 1f;
    private const float rageModeDuration = 3f;
    private const float depressedModeDuration = 3f;
    private float resetHitTargetTimer;
    private float tickDepressionTimer;
    private bool didHitTarget;

    private void Awake()
    {
        instance = this;
    }

    private void Update()
    {
        if (rageState != RageState.NORMAL) return;

        if (!didHitTarget)
        {
            tickDepressionTimer += Time.deltaTime;

            if (tickDepressionTimer < timeToTickDepression) return;

            tickDepressionTimer = 0;

            RageMeter -= depressionPerTick;

            return;    
        }

        resetHitTargetTimer += Time.deltaTime;

        if (resetHitTargetTimer < timeToResetHitTarget) return;

        didHitTarget = false;
    }

    private IEnumerator StartNewRageMode(RageState _rageState, float duration)
    {
        rageState = _rageState;
        Debug.Log("Entering " + _rageState);

        yield return new WaitForSeconds(duration);
        
        Debug.Log("Leaving " + _rageState);

        rageState = RageState.NORMAL;
        RageMeter = _rageState == RageState.ENRAGED ? 0.3f : 0.8f;
    }

    private void UpdateRageState()
    {
        bool isEnraged = RageMeter >= 1;
        bool isDepressed = RageMeter <= 0;
        
        RageState _rageState = isEnraged ? RageState.ENRAGED : isDepressed ? RageState.DEPRESSED : RageState.NORMAL;

        if(_rageState == RageState.NORMAL) return;

        float _duration = _rageState == RageState.ENRAGED ? rageModeDuration : depressedModeDuration; 

        StartCoroutine(StartNewRageMode(_rageState, _duration));
    }

    public void OnHitTarget()
    {
        didHitTarget = true;
        resetHitTargetTimer = 0;
        RageMeter += ragePerEnemyHit;
    }
}
