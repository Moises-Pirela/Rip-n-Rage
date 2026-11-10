using System.Collections;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Vfx;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIAgent.cs (NavMeshAgent -> engine steering agent).
// Standalone additions for readability: an attack is telegraphed (orange sprite pulse + aim glow + charge
// sound for EnemyData.attackWindup), then fires a burst of EnemyData.burstCount shots with a muzzle flash and
// a shot sound each; being hit staggers the enemy (stops, delays the shot) and spotting the player triggers an
// alert reaction (yellow flash + sting) before the behaviour tree engages.
public class AIAgent : MonoBehaviour, IEnemyAI
{
    public bool CanFire { get; set; }
    

    public AnimationStateController animationStateController;

    public Prefab projectile;

    public Transform projectileSpawnPosition;
    
    protected Vector3 TargetLocation;
    
    [SerializeField] public EnemyData data;

    public AIState aiState;

    private EnemyHitFlash _flash;
    private bool _attacking;
    private float _staggerUntil;
    private float _lastHealth;
    private bool _alerted;
    private float _alertUntil;

    /// <summary>True while a hit reaction holds the enemy in place.</summary>
    public bool IsStaggered => Time.time < _staggerUntil;

    protected override void Start()
    {
        animationStateController.AnimationData = data.enemyAnimationData;
        CanFire = true;
        aiState = GetComponent<AIState>();
        aiState.OnDieCallback += Die;
        _flash = GetComponent<EnemyHitFlash>();
        _lastHealth = aiState.Health;
        aiState.OnHealthChanged += OnHealthChanged;
    }

    protected override void Update()
    {
        if (aiState.HealthState == EntityHealthState.DEAD) return;;

        if (IsStaggered) StopAgent();

    }

    /// <summary>Hit reaction: flinch (the sprite flash is EnemyHitFlash's), stop and delay the next shot.</summary>
    private void OnHealthChanged()
    {
        if (aiState.Health < _lastHealth && aiState.HealthState != EntityHealthState.DEAD)
        {
            _staggerUntil = Time.time + data.staggerTime;
            StopAgent();
        }
        _lastHealth = aiState.Health;
    }

    /// <summary>The vision sensor saw the player: a short, readable reaction before the tree starts chasing / shooting.</summary>
    public void OnAlert()
    {
        if (_alerted || aiState.HealthState == EntityHealthState.DEAD) return;
        _alerted = true;
        _alertUntil = Time.time + data.alertDelay;
        _flash?.Telegraph(0.3f, new Color(1f, 0.95f, 0.3f, 1f));
        RipNRage.Engine.Ecs.Audio.Sfx.Play(ProceduralSounds.EnemyAlert, 0.8f);
        GameVfx.Spawn("enemy_alert", transform.position + Vector3.up * 1.6f, Vector3.up);
    }

    /// <summary>True once the alert reaction has played out (the tree waits for this before attacking).</summary>
    public bool ReadyToEngage => !_alerted || Time.time >= _alertUntil;

    public void Move(Vector3 targetLocation)
    {
        this.TargetLocation = targetLocation;
        if (IsStaggered || _attacking) return;
        

        animationStateController.AnimationState = AnimationState.MOVING;
    }

    public void Attack(Vector3 targetLocation)
    {
        TargetLocation = targetLocation; // keep aiming at where the player is now, even mid-attack
        if (!CanFire || _attacking || !ReadyToEngage) return;

        CanFire = false;
        StopAgent();
        StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        _attacking = true;
        animationStateController.AnimationState = AnimationState.ATTACKING;

        // 1. telegraph: orange pulse, glow at the muzzle, charge sound
        _flash?.Telegraph(data.attackWindup, new Color(1f, 0.55f, 0.15f, 1f));
        RipNRage.Engine.Ecs.Audio.Sfx.Play(ProceduralSounds.EnemyAim, 0.7f);
        if (projectileSpawnPosition != null) GameVfx.Spawn("enemy_aim", projectileSpawnPosition.position, (TargetLocation - projectileSpawnPosition.position).normalized);
        float t = 0f;
        while (t < data.attackWindup)
        {
            if (aiState.HealthState == EntityHealthState.DEAD) { _attacking = false; yield break; }
            if (!IsStaggered) t += Time.deltaTime; // a hit during the wind-up delays the shot
            yield return null;
        }

        // 2. burst
        int shots = Mathf.Max(1, data.burstCount);
        for (int i = 0; i < shots; i++)
        {
            if (aiState.HealthState == EntityHealthState.DEAD) { _attacking = false; yield break; }
            while (IsStaggered) yield return null;
            SpawnProjectile(TargetLocation);
            RipNRage.Engine.Ecs.Audio.Sfx.Play(ProceduralSounds.EnemyShot, 0.9f);
            if (projectileSpawnPosition != null) GameVfx.Spawn("enemy_muzzle", projectileSpawnPosition.position, (TargetLocation - projectileSpawnPosition.position).normalized);
            if (i < shots - 1) yield return new WaitForSeconds(data.burstInterval);
        }

        // 3. recover
        yield return new WaitForSeconds(data.fireRate);
        _attacking = false;
        CanFire = true;
    }

    public void SpawnProjectile(Vector3 targetLocation)
    {
        var direction = targetLocation - projectileSpawnPosition.position;
        GameObject projectile = Instantiate(this.projectile, projectileSpawnPosition.position, Quaternion.LookRotation(direction));
        
        projectile.GetComponent<Projectile>().SetupProjectile(GetComponent<AIState>());
    }

    public IEnumerator AllowRefireCoroutine()
    {
        yield return new WaitForSeconds(data.fireRate);

        CanFire = true;
    }

    public void Die()
    {
        animationStateController.AnimationState = AnimationState.DYING;
        StartCoroutine(SetDeadCoroutine());
    }
    public bool HasReachedLocation()
    {
        return true; // no steering until layer 6: the tree sees every move as done
    }
    
    private void StopAgent()
    {
    }

    private void AllowAgentToMove()
    {
        if (IsStaggered || _attacking) return;
    }

    private IEnumerator SetDeadCoroutine()
    {
        float waitTime = (float)data.enemyAnimationData.DeathAnimation.AnimationFrames /
                         data.enemyAnimationData.DeathAnimation.AnimationFramesPerSecond;
        
        yield return new WaitForSeconds(0.25f);
        
        animationStateController.AnimationState = AnimationState.DEAD;

    }
}
