// Standalone addition: dynamic sprite destruction. When the enemy dies its billboard sprite is sliced along the
// EnemyData.skeleton (skeletons/<name>.json, see the skeleton editor) into rigid-body pieces jointed like a
// ragdoll: a bullet kill throws the part that was hit, an explosion tears every joint and scatters the pieces.
// The original renderer is hidden; the entity's own death timeline (dissolve, removal) keeps running unseen.
public class EnemyDestruction : MonoBehaviour
{
    /// <summary>Multiplier on the killing blow's impulse.</summary>
    public float impulseScale = 1f;
    /// <summary>Seconds the ragdoll stays (0 = the skeleton's lifetime).</summary>
    public float lifetime = 0f;
    public SpriteRagdoll Ragdoll { get; private set; }

    private AIState _state;
    private AnimationStateController _anim;
    private NavMeshAgent _agent;

    protected override void Start()
    {
        _state = GetComponent<AIState>();
        _anim = GetComponent<AnimationStateController>();
        _agent = GetComponent<NavMeshAgent>();
        if (_state != null) _state.OnDieCallback += OnDie;
    }

    private void OnDie()
    {
        if (Ragdoll != null || _state == null || _state.data == null || string.IsNullOrEmpty(_state.data.skeleton)) return;
        var skeleton = SkeletonLibrary.Get(_state.data.skeleton);
        var sr = _anim != null ? _anim.SpriteRenderer : GetComponentInChildren<SpriteRenderer>();
        if (skeleton == null || sr == null || sr.sprite == null) return;

        var options = RagdollOptions.Default;
        options.lifetime = lifetime;
        options.initialVelocity = _agent != null ? _agent.velocity * 0.5f : Vector3.zero;
        if (DamageInfo.Available)
        {
            options.impulse = DamageInfo.Direction * (DamageInfo.Impulse * impulseScale);
            options.impulsePoint = DamageInfo.Point;
            options.gib = DamageInfo.Explosive;
            options.scatterImpulse = DamageInfo.Explosive ? Mathf.Max(4f, DamageInfo.Impulse * 0.15f) : 0f;
        }
        Ragdoll = SpriteRagdoll.Spawn(skeleton, sr.sprite, sr.transform.position, sr.transform.lossyScale, options, sr);
        if (Ragdoll == null) return;
        if (options.gib && DamageInfo.Available && DamageInfo.ExplosionRadius > 0f)
            Ragdoll.AddExplosion(DamageInfo.Impulse * impulseScale, DamageInfo.Point, DamageInfo.ExplosionRadius, 0.6f);
        // the pieces are the corpse now
        sr.enabled = false;
        if (_anim != null) _anim.enabled = false;
    }
}
