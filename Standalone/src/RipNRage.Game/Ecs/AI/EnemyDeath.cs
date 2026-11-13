using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Destruction;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content.Vfx;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// An enemy's death effects, off the event payload: the kind's death VFX, and the sprite sliced along the
    /// definition's skeleton into ragdoll pieces thrown by the killing blow (torn apart by an explosive one).
    /// Replaces the EnemyDestruction MonoBehaviour and the DamageInfo static hand-off — the <see cref="DeathEvent"/>
    /// carries the blow's circumstances. The pieces are the corpse; the sprite child goes quiet while
    /// <see cref="EnemyVisualSystem"/>'s dissolve clock still removes the enemy.
    /// </summary>
    public sealed class EnemyDeathSystem : SystemBase, IConsumes<DeathEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Entity) || !w.Has<Enemy>(e.Entity) || !w.Has<EnemyVisual>(e.Entity)) continue;
                var def = Enemies.All[w.Get<Enemy>(e.Entity).Kind];
                GameVfx.Death(def.name, w.Get<LocalToWorld>(e.Entity).Position + Vector3.up * 0.4f);
                Slice(ctx, in e, def);
            }
        }

        /// <summary>The ragdoll, cut from the sprite the look is showing, thrown as the event says.</summary>
        private static void Slice(SystemContext ctx, in DeathEvent e, EnemyData def)
        {
            if (string.IsNullOrEmpty(def.skeleton)) return;
            var w = ctx.World;
            var skeleton = SkeletonLibrary.Get(def.skeleton);
            var spriteEntity = w.Get<EnemyVisual>(e.Entity).Sprite;
            if (skeleton == null || !w.IsAlive(spriteEntity)) return;
            var render = w.Get<SpriteRender>(spriteEntity);
            var sprite = RenderAssets.Sprites.Resolve(render.Sprite);
            if (sprite == null) return;
            var pose = w.Get<LocalToWorld>(spriteEntity);

            var options = RagdollOptions.Default;
            options.lit = render.Lit; options.castsShadow = render.CastsShadow; options.color = render.Color;
            options.material = render.Material;
            if (e.Impulse > 0f || e.Explosive)
            {
                options.impulse = e.Direction * e.Impulse;
                options.impulsePoint = e.Point;
                options.gib = e.Explosive;
                options.scatterImpulse = e.Explosive ? Mathf.Max(4f, e.Impulse * 0.15f) : 0f;
            }
            Vector3 viewer = PlayerCamera.Has ? PlayerCamera.Position : pose.Position + Vector3.back;
            var pieces = SpriteRagdolls.Spawn(w, skeleton, sprite, pose.Position, pose.Scale, viewer, options);
            if (pieces.Count == 0) return;
            // this runs inside the death event dispatch: structural changes go through the buffer
            ctx.Commands.Add(spriteEntity, new Hidden());
        }
    }
}
