using System.Collections;
using RipNRage.Game.Content;
using RipNRage.Game.Content.Vfx;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIAgent.cs (NavMeshAgent -> engine steering agent).
// Standalone additions for readability: an attack is telegraphed (orange sprite pulse + aim glow + charge
// sound for EnemyData.attackWindup), then fires a burst of EnemyData.burstCount shots with a muzzle flash and
// a shot sound each; being hit staggers the enemy (stops, delays the shot) and spotting the player triggers an
// alert reaction (yellow flash + sting) before the state tree engages.
// MIGRATION (6.2 / 6.4): the behaviour tree that called Move / Attack is gone; the entity's AiBrainSystem writes an
// AiIntent, SteeringSystem walks and turns the body on it, and Update below acts on the attack half. 6.5 turns the
// attack into components and deletes this class.
public class AIAgent : MonoBehaviour
{
    public bool CanFire { get; set; }
    



    public Transform projectileSpawnPosition;
    
    protected Vector3 TargetLocation;
    
    [SerializeField] public EnemyData data;

    public AIState aiState;

    private bool _attacking;
    private float _staggerUntil;
    private float _lastHealth;
    private bool _alerted;
    private float _alertUntil;

    /// <summary>True while a hit reaction holds the enemy in place.</summary>
    public bool IsStaggered => Time.time < _staggerUntil;

    protected override void Start()
    {
        CanFire = true;
        aiState = GetComponent<AIState>();
        aiState.OnDieCallback += Die;
        _lastHealth = aiState.Health;
        aiState.OnHealthChanged += OnHealthChanged;
    }

    protected override void Update()
    {
        if (aiState.HealthState == EntityHealthState.DEAD) return;

        if (IsStaggered) StopAgent();

        var w = Application.Current.World;
        if (!w.IsAlive(aiState.entity) || !w.Has<RipNRage.Game.Ecs.AI.AiIntent>(aiState.entity)) return;
        if (!_alerted && !w.Get<RipNRage.Game.Ecs.AI.Perception>(aiState.entity).Target.IsNull) OnAlert(); // the senses found someone
        var intent = w.Get<RipNRage.Game.Ecs.AI.AiIntent>(aiState.entity);
        if (intent.Attack) Attack(intent.FacePoint); // movement is SteeringSystem's (6.4)
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

    /// <summary>The senses found the player: a short, readable reaction before the tree starts chasing / shooting.</summary>
    public void OnAlert()
    {
        if (_alerted || aiState.HealthState == EntityHealthState.DEAD) return;
        _alerted = true;
        _alertUntil = Time.time + data.alertDelay;
        RipNRage.Game.Ecs.AI.Enemies.Telegraph(Application.Current.World, aiState.entity, 0.3f, new Color(1f, 0.95f, 0.3f, 1f));
        RipNRage.Engine.Ecs.Audio.Sfx.Play(ProceduralSounds.EnemyAlert, 0.8f);
        GameVfx.Spawn("enemy_alert", transform.position + Vector3.up * 1.6f, Vector3.up);
    }

    /// <summary>True once the alert reaction has played out (the tree waits for this before attacking).</summary>
    public bool ReadyToEngage => !_alerted || Time.time >= _alertUntil;

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
        SetAnim(AnimationState.ATTACKING);

        // 1. telegraph: orange pulse, glow at the muzzle, charge sound
        RipNRage.Game.Ecs.AI.Enemies.Telegraph(Application.Current.World, aiState.entity, data.attackWindup, new Color(1f, 0.55f, 0.15f, 1f));
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
        SetAnim(AnimationState.IDLE); // hands the sheet back to the steering (walking / standing)
    }

    private void SetAnim(AnimationState state) =>
        RipNRage.Game.Ecs.AI.Enemies.SetAnim(Application.Current.World, aiState.entity, state);

    public void SpawnProjectile(Vector3 targetLocation)
    {
        var direction = targetLocation - projectileSpawnPosition.position;
        var self = GetComponent<AIState>();
        var w = Application.Current.World;
        var shooter = self != null ? self.entity : RipNRage.Engine.Ecs.Entity.Null;
        RipNRage.Game.Ecs.Combat.Projectiles.Launch(w, RipNRage.Game.Ecs.Combat.Projectiles.EnemyBullet, projectileSpawnPosition.position, direction, shooter);
        RipNRage.Game.Ecs.AI.Noises.Emit(w.Commands, projectileSpawnPosition.position, shooter, data.noise); // the others hear it
    }

    public IEnumerator AllowRefireCoroutine()
    {
        yield return new WaitForSeconds(data.fireRate);

        CanFire = true;
    }

    public void Die()
    {
        SetAnim(AnimationState.DYING);
        StartCoroutine(SetDeadCoroutine());
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
        
        SetAnim(AnimationState.DEAD);

    }
}
