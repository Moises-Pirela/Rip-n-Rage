using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>The enemy definitions, by kind — shared immutable data, the weapons / projectiles pattern.</summary>
    public static class Enemies
    {
        public static EnemyData[] All = System.Array.Empty<EnemyData>();

        public static int KindOf(EnemyData data) => System.Array.IndexOf(All, data);

        /// <summary>Pulses the enemy's sprite in <paramref name="color"/> (attack wind-up, alert).</summary>
        public static void Telegraph(World w, Entity enemy, float duration, Color color)
        {
            if (!w.IsAlive(enemy) || !w.Has<EnemyVisual>(enemy)) return;
            ref var v = ref w.Get<EnemyVisual>(enemy);
            v.TelegraphLeft = v.TelegraphDuration = Mathf.Max(0.01f, duration);
            v.TelegraphColor = color;
        }

        public static void SetAnim(World w, Entity enemy, AnimationState state)
        {
            if (w.IsAlive(enemy) && w.Has<EnemyVisual>(enemy)) w.Get<EnemyVisual>(enemy).State = state;
        }
    }

    /// <summary>
    /// The enemy's look, on its entity: which 8-sided sheet is playing (by <see cref="State"/>), plus the hit flash,
    /// the telegraph pulse and the death dissolve that the legacy AnimationStateController / EnemyHitFlash pair drove
    /// on the GameObject sprite. The sprite itself is the <see cref="Sprite"/> child entity (offset to put the feet
    /// on the ground), whose <see cref="SpriteRender"/> / <see cref="SpriteFx"/> this component's system writes.
    /// </summary>
    public struct EnemyVisual : IComponent
    {
        public AnimationState State;
        public Entity Sprite;
        public float StateTime;
        public float Flash;
        public float TelegraphLeft, TelegraphDuration;
        public Color TelegraphColor;
        /// <summary>Seconds since death (&lt; 0 = alive): after 2.5 s the sprite dissolves over 1.6 s, then the corpse goes.</summary>
        public float DeadTime;
        internal AnimationState LastState;
    }

    /// <summary>
    /// Plays the sheets and the effects: frame from the state clock, row from the angle between the enemy's facing
    /// and the active camera, flash / telegraph into <see cref="SpriteFx"/>, and the death dissolve — which also owns
    /// the corpse: when it completes, the enemy entity (and its following GameObject) is removed. A hit flashes via
    /// <see cref="HitEvent"/>; a death starts the clock via <see cref="DeathEvent"/>. Runs alone (writes child entities).
    /// Which sheets: the <see cref="Enemy"/>'s definition.
    /// </summary>
    public sealed class EnemyVisualSystem : SystemBase, ITick, IConsumes<HitEvent>, IConsumes<DeathEvent>
    {
        private const float FlashDuration = 0.12f, DissolveDelay = 2.5f, DissolveDuration = 1.6f;

        private readonly Dictionary<Texture2D, DirectionalSpriteSheet> _sheets = new Dictionary<Texture2D, DirectionalSpriteSheet>();
        private Query _q, _cameras;
        private AssetHandle<Material> _dissolve;

        public override void OnCreate()
        {
            _q = World.Query().With<EnemyVisual>().WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().Build();
            _cameras = ActiveCamera.BuildQuery(World);
        }

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            bool hasCam = ActiveCamera.TryGet(_cameras, out var cam, out _);

            foreach (var chunk in _q)
            {
                var vis = chunk.Get<EnemyVisual>(); var kinds = chunk.Get<Enemy>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var v = ref vis[i];

                    // the corpse clock runs even while the ragdoll has the sprite hidden: it ends the enemy
                    if (v.DeadTime >= 0f)
                    {
                        v.DeadTime += dt;
                        float t = (v.DeadTime - DissolveDelay) / DissolveDuration;
                        if (t >= 1f) { RemoveCorpse(ctx, ents[i]); continue; }
                        if (t >= 0f && World.IsAlive(v.Sprite))
                        {
                            if (_dissolve.IsNone) _dissolve = RenderAssets.Materials.Intern(Content.Materials.EnemyDissolve, "enemy_dissolve");
                            World.Get<SpriteRender>(v.Sprite).Material = _dissolve;
                            World.Get<SpriteFx>(v.Sprite).Dissolve = Mathf.Clamp01(t);
                        }
                    }

                    if (!World.IsAlive(v.Sprite) || World.Has<Hidden>(v.Sprite)) continue;

                    // the sheet: frame from the state clock, row from facing vs the camera
                    if (v.State != v.LastState) { v.LastState = v.State; v.StateTime = 0f; }
                    v.StateTime += dt;
                    var sheet = SheetFor(kinds[i].Kind, v.State);
                    if (sheet != null)
                    {
                        bool loop = v.State == AnimationState.MOVING || v.State == AnimationState.IDLE || v.State == AnimationState.ATTACKING;
                        int frame = sheet.FrameAt(v.StateTime, loop);
                        int side = hasCam ? sheet.SideFor(ltw[i].Position, ltw[i].Forward, cam.Position) : 0;
                        World.Get<SpriteRender>(v.Sprite).Sprite = RenderAssets.Sprites.Intern(sheet.GetSprite(side, frame));
                    }

                    // flash beats telegraph, exactly as the legacy flash did
                    ref var fx = ref World.Get<SpriteFx>(v.Sprite);
                    if (v.Flash > 0f)
                    {
                        v.Flash -= dt / FlashDuration;
                        fx.FlashColor = new Color(1f, 1f, 1f, 0f); // alpha 0 = white hit flash
                        fx.Flash = Mathf.Clamp01(v.Flash) * 0.85f;
                    }
                    else if (v.TelegraphLeft > 0f)
                    {
                        v.TelegraphLeft -= dt;
                        float gone = 1f - v.TelegraphLeft / v.TelegraphDuration;
                        fx.FlashColor = v.TelegraphColor;
                        fx.Flash = 0.35f + 0.45f * gone * (0.6f + 0.4f * Mathf.Sin(Time.time * 28f));
                    }
                    else if (fx.Flash != 0f) fx.Flash = 0f;
                }
            }
        }

        /// <summary>The enemy and its GameObject half go together (the GO still carries the unported scripts).</summary>
        private static void RemoveCorpse(SystemContext ctx, Entity enemy)
        {
            var go = EcsObjects.Of(enemy);
            if (go != null) GameObject.Destroy(go);
            ctx.Commands.DestroyEntity(enemy); // the sprite child follows through the hierarchy
        }

        private DirectionalSpriteSheet SheetFor(int kind, AnimationState state)
        {
            var data = Enemies.All[kind].enemyAnimationData;
            var sheet = state switch
            {
                AnimationState.MOVING => data.MoveAnimation,
                AnimationState.ATTACKING => data.AttackAnimation,
                AnimationState.DYING => data.DeathAnimation,
                AnimationState.DEAD => data.DeadAnimation,
                _ => data.IdleAnimation,
            };
            if (sheet.TextureSheet == null) return null;
            if (!_sheets.TryGetValue(sheet.TextureSheet, out var built))
                _sheets[sheet.TextureSheet] = built = new DirectionalSpriteSheet(sheet.TextureSheet, sheet.AnimationFrames, sheet.Sides, sheet.AnimationFramesPerSecond);
            return built;
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            foreach (ref readonly var e in events)
                if (World.IsAlive(e.Target) && World.Has<EnemyVisual>(e.Target))
                    World.Get<EnemyVisual>(e.Target).Flash = 1f;
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            foreach (ref readonly var e in events)
                if (World.IsAlive(e.Entity) && World.Has<EnemyVisual>(e.Entity))
                    World.Get<EnemyVisual>(e.Entity).DeadTime = 0f;
        }
    }
}
