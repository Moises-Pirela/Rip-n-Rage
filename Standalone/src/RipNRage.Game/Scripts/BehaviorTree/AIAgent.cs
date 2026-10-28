using System.Collections;

// Ported from Assets/_AI_BehaviorTree/Scripts/AIAgent.cs (NavMeshAgent -> engine steering agent).
[RequireComponent(typeof(NavMeshAgent))]
public class AIAgent : MonoBehaviour, IEnemyAI
{
    public bool CanFire { get; set; }
    
    public NavMeshAgent navMeshAgent;

    public AnimationStateController animationStateController;

    public Prefab projectile;

    public Transform projectileSpawnPosition;
    
    protected Vector3 TargetLocation;
    
    [SerializeField] public EnemyData data;

    public AIState aiState;

    protected override void Start()
    {
        animationStateController.AnimationData = data.enemyAnimationData;
        CanFire = true;
        navMeshAgent.speed = data.movementSpeed;
        navMeshAgent.angularSpeed = data.turnSpeed;
        aiState = GetComponent<AIState>();
        aiState.OnDieCallback += Die;
    }

    protected override void Update()
    {
        if (aiState.HealthState == EntityHealthState.DEAD) return;;
        
        if (navMeshAgent.isStopped && animationStateController.AnimationState == AnimationState.MOVING)
        {
            animationStateController.AnimationState = AnimationState.IDLE;
        }
    }

    public void Move(Vector3 targetLocation)
    {
        this.TargetLocation = targetLocation;
        
        navMeshAgent.SetDestination(targetLocation);

        animationStateController.AnimationState = AnimationState.MOVING;
    }

    public void Attack(Vector3 targetLocation)
    {
        if (!CanFire) return;

        CanFire = false;
        StopAgent();
        SpawnProjectile(targetLocation);
        animationStateController.AnimationState = AnimationState.ATTACKING;
        StartCoroutine(AllowRefireCoroutine());
    }

    public void SpawnProjectile(Vector3 targetLocation)
    {
        var direction = targetLocation - transform.position; 
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
        float distanceToTargetLocation = Vector3.Distance(TargetLocation, transform.position);

        NavMeshPath path = navMeshAgent.path;

        float closestCornerDistance = 0;

        foreach (var corner in path.corners)
        {
            if (Vector3.Distance(corner, transform.position) <= closestCornerDistance)
            {
                closestCornerDistance = Vector3.Distance(corner, transform.position);
            }
        }

        if (navMeshAgent.remainingDistance + closestCornerDistance <= 5f)
        {
            StopAgent();
            return true;
        }

        AllowAgentToMove();

        return false;
    }
    
    private void StopAgent()
    {
        navMeshAgent.isStopped = true;
        navMeshAgent.velocity = Vector3.zero;
    }

    private void AllowAgentToMove()
    {
        navMeshAgent.isStopped = false;
        navMeshAgent.velocity = navMeshAgent.desiredVelocity;
    }

    private IEnumerator SetDeadCoroutine()
    {
        float waitTime = (float)data.enemyAnimationData.DeathAnimation.AnimationFrames /
                         data.enemyAnimationData.DeathAnimation.AnimationFramesPerSecond;
        
        yield return new WaitForSeconds(0.25f);
        
        animationStateController.AnimationState = AnimationState.DEAD;

    }
}
