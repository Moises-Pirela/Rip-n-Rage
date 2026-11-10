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

    /// <summary>The ragdoll's piece entities (empty until the enemy died with a skeleton).</summary>
    public System.Collections.Generic.List<RipNRage.Engine.Ecs.Entity> Pieces { get; private set; } = new System.Collections.Generic.List<RipNRage.Engine.Ecs.Entity>();

    private void OnDie()
    {
        if (Pieces.Count > 0 || _state == null || _state.data == null || string.IsNullOrEmpty(_state.data.skeleton)) return;
        var skeleton = SkeletonLibrary.Get(_state.data.skeleton);
        var sr = _anim != null ? _anim.SpriteRenderer : GetComponentInChildren<SpriteRenderer>();
        if (skeleton == null || sr == null || sr.sprite == null) return;

        var options = RipNRage.Engine.Ecs.Destruction.RagdollOptions.Default;
        options.lifetime = lifetime;
        options.lit = sr.lit; options.castsShadow = sr.castsShadow; options.color = sr.color;
        options.material = RipNRage.Engine.Ecs.Rendering.RenderAssets.Materials.Intern(sr.material);
        if (DamageInfo.Available)
        {
            options.impulse = DamageInfo.Direction * (DamageInfo.Impulse * impulseScale);
            options.impulsePoint = DamageInfo.Point;
            options.gib = DamageInfo.Explosive;
            options.scatterImpulse = DamageInfo.Explosive ? Mathf.Max(4f, DamageInfo.Impulse * 0.15f) : 0f;
        }
        var cam = Camera.main;
        Vector3 viewer = cam != null ? cam.transform.position : sr.transform.position + Vector3.back;
        Pieces = RipNRage.Engine.Ecs.Destruction.SpriteRagdolls.Spawn(Application.Current.World, skeleton, sr.sprite, sr.transform.position, sr.transform.lossyScale, viewer, options);
        if (Pieces.Count == 0) return;
        // the pieces are the corpse now
        sr.enabled = false;
        if (_anim != null) _anim.enabled = false;
    }
}
