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

    private AIState _state;
    private AnimationStateController _anim;

    protected override void Start()
    {
        _state = GetComponent<AIState>();
        _anim = GetComponent<AnimationStateController>();
        if (_state != null) _state.OnDieCallback += OnDie;
    }

    private void OnDie()
    {
        // ECS migration (layer 2): SpriteRagdoll was legacy rigid bodies + joints; it returns in layer 3 on ECS
        // bodies and joints (2.5). Until then the enemy's own death timeline plays without a ragdoll.
    }
}
