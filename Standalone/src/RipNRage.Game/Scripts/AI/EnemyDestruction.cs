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

    protected override void Start()
    {
        _state = GetComponent<AIState>();
        if (_state != null) _state.OnDieCallback += OnDie;
    }

    /// <summary>The ragdoll's piece entities (empty until the enemy died with a skeleton).</summary>
    public System.Collections.Generic.List<RipNRage.Engine.Ecs.Entity> Pieces { get; private set; } = new System.Collections.Generic.List<RipNRage.Engine.Ecs.Entity>();

    private void OnDie()
    {
        if (Pieces.Count > 0 || _state == null || _state.data == null || string.IsNullOrEmpty(_state.data.skeleton)) return;
        var skeleton = SkeletonLibrary.Get(_state.data.skeleton);
        // the look lives on the entity's sprite child now (6.1)
        var w = Application.Current.World;
        if (!w.IsAlive(_state.entity) || !w.Has<RipNRage.Game.Ecs.AI.EnemyVisual>(_state.entity)) return;
        var spriteEntity = w.Get<RipNRage.Game.Ecs.AI.EnemyVisual>(_state.entity).Sprite;
        if (skeleton == null || !w.IsAlive(spriteEntity)) return;
        var render = w.Get<RipNRage.Engine.Ecs.Rendering.SpriteRender>(spriteEntity);
        var sprite = RipNRage.Engine.Ecs.Rendering.RenderAssets.Sprites.Resolve(render.Sprite);
        var pose = w.Get<RipNRage.Engine.Ecs.Transforms.LocalToWorld>(spriteEntity);
        if (sprite == null) return;

        var options = RipNRage.Engine.Ecs.Destruction.RagdollOptions.Default;
        options.lifetime = lifetime;
        options.lit = render.Lit; options.castsShadow = render.CastsShadow; options.color = render.Color;
        options.material = render.Material;
        if (DamageInfo.Available)
        {
            options.impulse = DamageInfo.Direction * (DamageInfo.Impulse * impulseScale);
            options.impulsePoint = DamageInfo.Point;
            options.gib = DamageInfo.Explosive;
            options.scatterImpulse = DamageInfo.Explosive ? Mathf.Max(4f, DamageInfo.Impulse * 0.15f) : 0f;
        }
        Vector3 viewer = RipNRage.Game.Ecs.PlayerCamera.Has ? RipNRage.Game.Ecs.PlayerCamera.Position : pose.Position + Vector3.back;
        Pieces = RipNRage.Engine.Ecs.Destruction.SpriteRagdolls.Spawn(w, skeleton, sprite, pose.Position, pose.Scale, viewer, options);
        if (Pieces.Count == 0) return;
        // the pieces are the corpse now: the sprite child goes quiet (the dissolve clock still removes the enemy)
        // this runs inside the death event dispatch: structural changes go through the buffer
        w.Commands.Add(spriteEntity, new RipNRage.Engine.Ecs.Rendering.Hidden());
    }
}
