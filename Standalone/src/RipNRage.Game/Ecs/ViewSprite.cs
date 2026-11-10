using System.Collections.Generic;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// MIGRATION BRIDGE (delete in layer 4, when the viewmodel is a camera-child entity): a GameObject-anchored sprite
    /// that renders and animates through the ECS. It replaces the legacy SpriteRenderer + Animator pair on the
    /// first-person weapon / fists / one-off effects: it owns an entity with <see cref="SpriteRender"/> (+ a
    /// <see cref="SpriteAnimator"/> once a controller is set), copies the GameObject's world pose to it every frame,
    /// maps activation to <see cref="Hidden"/> and forwards the entity's <see cref="AnimationEvent"/>s to the
    /// GameObject as SendMessage (the clip-event contract the weapon scripts were written against).
    /// </summary>
    public sealed class ViewSprite : MonoBehaviour
    {
        internal static readonly Dictionary<Entity, ViewSprite> ByEntity = new Dictionary<Entity, ViewSprite>();
        private static readonly Dictionary<int, string> HashNames = new Dictionary<int, string>();

        public Entity entity { get; private set; }
        private AnimatorController _controller;

        private static World W => Application.Current.World;

        /// <summary>Adds a ViewSprite to <paramref name="go"/> and creates its entity (hidden until the GameObject is enabled).</summary>
        public static ViewSprite Attach(GameObject go, Sprite sprite, RenderLayer layer, int sortingOrder = 0, bool fullBright = false, bool billboard = false, Color? color = null, bool lit = false)
        {
            var v = go.AddComponent<ViewSprite>();
            var cmd = W.Commands;
            var e = cmd.CreateEntity();
            cmd.Add(e, new LocalTransform { Position = go.transform.position, Rotation = go.transform.rotation, Scale = go.transform.lossyScale });
            cmd.Add(e, new SpriteRender
            {
                Sprite = RenderAssets.Sprites.Intern(sprite), Color = color ?? Color.white, Layer = layer, SortingOrder = sortingOrder,
                FullBright = fullBright, Billboard = billboard, Lit = lit, CastsShadow = layer != RenderLayer.FirstPerson,
            });
            cmd.Add(e, new Hidden());
            v.entity = e;
            ByEntity[e] = v;
            return v;
        }

        /// <summary>The playing controller (legacy Animator.runtimeAnimatorController): replaces the entity's SpriteAnimator.</summary>
        public AnimatorController runtimeAnimatorController
        {
            get => _controller;
            set
            {
                if (_controller == value) return;
                _controller = value;
                if (value == null) { W.Commands.Remove<SpriteAnimator>(entity); return; }
                var h = AnimationAssets.Controllers.Intern(value, value.name);
                W.Commands.Add(entity, new SpriteAnimator { Controller = h, State = -1, PendingState = -1, Speed = 1f });
            }
        }

        public static int StringToHash(string name) { int h = name.GetHashCode(); HashNames[h] = name; return h; }
        private static string NameOf(int id) => HashNames.TryGetValue(id, out var n) ? n : id.ToString();

        private bool Animated(out World w) { w = W; return _controller != null && w.IsAlive(entity) && w.Has<SpriteAnimator>(entity); }
        public void SetBool(int id, bool v) => SetBool(NameOf(id), v);
        public void SetTrigger(int id) => SetTrigger(NameOf(id));
        public void SetBool(string name, bool v) { if (Animated(out var w)) w.Get<SpriteAnimator>(entity).SetBool(name, v); }
        public void SetTrigger(string name) { if (Animated(out var w)) w.Get<SpriteAnimator>(entity).SetTrigger(name); }
        public void Play(string state) { if (Animated(out var w)) w.Get<SpriteAnimator>(entity).Play(state); }

        // ---- read-back (the animation editor's controller preview) ----
        private SpriteAnimator? Anim => Animated(out var w) ? w.Get<SpriteAnimator>(entity) : (SpriteAnimator?)null;
        /// <summary>Playback speed multiplier; 0 pauses.</summary>
        public float speed { get => Anim?.Speed ?? 1f; set { if (Animated(out var w)) w.Get<SpriteAnimator>(entity).Speed = value; } }
        public AnimatorState currentState => Anim is SpriteAnimator a && _controller != null && a.State >= 0 && a.State < _controller.states.Count ? _controller.states[a.State] : null;
        public float currentTime => Anim?.Time ?? 0f;
        public string currentStateName => currentState?.name;
        public bool GetBool(string name) => Anim?.GetBool(name) ?? false;
        public float GetFloat(string name) => Anim?.GetFloat(name) ?? 0f;
        public void SetFloat(string name, float v) { if (Animated(out var w)) w.Get<SpriteAnimator>(entity).SetFloat(name, v); }
        public bool IsTriggerSet(string name) => Anim?.IsTriggerSet(name) ?? false;
        /// <summary>Render settings the editor rig changes (layer, billboard, lighting).</summary>
        public void SetRender(RenderLayer layer, bool billboard, bool lit)
        {
            var w = W;
            if (!w.IsAlive(entity) || !w.Has<SpriteRender>(entity)) return;
            ref var r = ref w.Get<SpriteRender>(entity);
            r.Layer = layer; r.Billboard = billboard; r.Lit = lit;
        }

        protected override void OnEnable() { if (W.IsAlive(entity)) W.Commands.Remove<Hidden>(entity); }
        protected override void OnDisable() { if (W.IsAlive(entity)) W.Commands.Add(entity, new Hidden()); }

        protected override void LateUpdate()
        {
            var w = W;
            if (!w.IsAlive(entity) || !w.Has<LocalTransform>(entity)) return;
            ref var lt = ref w.Get<LocalTransform>(entity);
            lt.Position = transform.position; lt.Rotation = transform.rotation; lt.Scale = transform.lossyScale;
        }

        protected override void OnDestroy()
        {
            ByEntity.Remove(entity);
            if (W.IsAlive(entity)) W.Commands.DestroyEntity(entity);
        }
    }

    /// <summary>Forwards clip events of ViewSprite entities to their GameObjects (SendMessage, as the legacy Animator did).</summary>
    public sealed class ViewSpriteEventSystem : SystemBase, IConsumes<AnimationEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<AnimationEvent> events)
        {
            foreach (var e in events)
                if (ViewSprite.ByEntity.TryGetValue(e.Entity, out var v) && v != null && !v.IsDestroyed) v.gameObject.SendMessage(e.Function);
        }
    }
}
