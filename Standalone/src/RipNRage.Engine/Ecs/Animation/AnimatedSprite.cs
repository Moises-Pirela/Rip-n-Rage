using System;
using System.Collections.Generic;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Ecs.Animation
{
    /// <summary>
    /// A handle on a sprite entity (<see cref="SpriteRender"/>, optionally a <see cref="SpriteAnimator"/> once a
    /// controller is set) for the code that is not systems yet: the weapon scripts own the first-person viewmodel
    /// through these, and the animation editor its preview. The entity lives in the world like any other — parented
    /// with <see cref="Transforms.Parent"/>, rendered by <see cref="EcsRenderProducer"/>, animated by
    /// <see cref="AnimationSystem"/> — the handle only wraps the component access behind the legacy Animator's
    /// vocabulary (controller, SetBool / SetTrigger / Play, speed, the editor's read-back).
    ///
    /// <para>Structural work goes through the command buffer, so right after creation the components are not readable
    /// until the next sync point; every accessor checks and degrades to a no-op / default, as the bridge it replaces
    /// did.</para>
    /// </summary>
    public sealed class AnimatedSprite
    {
        private static readonly Dictionary<int, string> HashNames = new Dictionary<int, string>();

        public Entity Entity { get; private set; }
        private readonly World _w;
        private AnimatorController _controller;
        private Action<string> _onEvent;

        /// <summary>
        /// Creates the sprite entity. <paramref name="parent"/> makes the pose local to another entity (the viewmodel
        /// hangs off the camera); without it the entity is world-space and the owner sets the pose itself.
        /// </summary>
        public AnimatedSprite(World world, Sprite sprite, RenderLayer layer, int sortingOrder = 0, bool fullBright = false,
                              bool billboard = false, Color? color = null, bool lit = false,
                              Entity parent = default, Vector3? localPosition = null, Vector3? localScale = null)
        {
            _w = world;
            var cmd = world.Commands;
            var e = cmd.CreateEntity();
            cmd.Add(e, new LocalTransform
            {
                Position = localPosition ?? Vector3.zero,
                Rotation = Quaternion.identity,
                Scale = localScale ?? Vector3.one,
            });
            if (!parent.IsNull) cmd.Add(e, new Parent { Value = parent });
            cmd.Add(e, new SpriteRender
            {
                Sprite = sprite != null ? RenderAssets.Sprites.Intern(sprite) : default,
                Color = color ?? Color.white, Layer = layer, SortingOrder = sortingOrder,
                FullBright = fullBright, Billboard = billboard, Lit = lit, CastsShadow = layer != RenderLayer.FirstPerson,
            });
            Entity = e;
        }

        public bool Alive => _w.IsAlive(Entity);

        /// <summary>Clip events of this entity, by function name (the legacy clip-event contract).</summary>
        public Action<string> OnEvent
        {
            get => _onEvent;
            set
            {
                _onEvent = value;
                if (value != null) AnimationEventRouter.Register(Entity, value);
                else AnimationEventRouter.Unregister(Entity);
            }
        }

        // ---------------------------------------------------------------- pose

        /// <summary>Local position (relative to the parent entity, or the world).</summary>
        public Vector3 LocalPosition
        {
            get => Alive && _w.Has<LocalTransform>(Entity) ? _w.Get<LocalTransform>(Entity).Position : Vector3.zero;
            set { if (Alive && _w.Has<LocalTransform>(Entity)) _w.Get<LocalTransform>(Entity).Position = value; }
        }

        public Vector3 LocalScale
        {
            get => Alive && _w.Has<LocalTransform>(Entity) ? _w.Get<LocalTransform>(Entity).Scale : Vector3.one;
            set { if (Alive && _w.Has<LocalTransform>(Entity)) _w.Get<LocalTransform>(Entity).Scale = value; }
        }

        /// <summary>The whole pose at once (a world-space owner following something that is not an entity).</summary>
        public void SetPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!Alive || !_w.Has<LocalTransform>(Entity)) return;
            ref var lt = ref _w.Get<LocalTransform>(Entity);
            lt.Position = position; lt.Rotation = rotation; lt.Scale = scale;
        }

        // ---------------------------------------------------------------- visibility

        /// <summary>Shows / hides the sprite (the legacy GameObject.SetActive on the viewmodel parts).</summary>
        public void SetActive(bool active)
        {
            if (!Alive) return;
            if (active) _w.Commands.Remove<Hidden>(Entity);
            else _w.Commands.Add(Entity, new Hidden());
        }

        public bool activeSelf => Alive && !_w.Has<Hidden>(Entity);

        /// <summary>Render settings the editor rig changes (layer, billboard, lighting).</summary>
        public void SetRender(RenderLayer layer, bool billboard, bool lit)
        {
            if (!Alive || !_w.Has<SpriteRender>(Entity)) return;
            ref var r = ref _w.Get<SpriteRender>(Entity);
            r.Layer = layer; r.Billboard = billboard; r.Lit = lit;
        }

        // ---------------------------------------------------------------- animator

        /// <summary>The playing controller (legacy Animator.runtimeAnimatorController): replaces the entity's SpriteAnimator.</summary>
        public AnimatorController runtimeAnimatorController
        {
            get => _controller;
            set
            {
                if (_controller == value) return;
                _controller = value;
                if (value == null) { _w.Commands.Remove<SpriteAnimator>(Entity); return; }
                var h = AnimationAssets.Controllers.Intern(value, value.name);
                _w.Commands.Add(Entity, new SpriteAnimator { Controller = h, State = -1, PendingState = -1, Speed = 1f });
            }
        }

        public static int StringToHash(string name) { int h = name.GetHashCode(); HashNames[h] = name; return h; }
        private static string NameOf(int id) => HashNames.TryGetValue(id, out var n) ? n : id.ToString();

        private bool Animated => _controller != null && Alive && _w.Has<SpriteAnimator>(Entity);
        public void SetBool(int id, bool v) => SetBool(NameOf(id), v);
        public void SetTrigger(int id) => SetTrigger(NameOf(id));
        public void SetBool(string name, bool v) { if (Animated) _w.Get<SpriteAnimator>(Entity).SetBool(name, v); }
        public void SetTrigger(string name) { if (Animated) _w.Get<SpriteAnimator>(Entity).SetTrigger(name); }
        public void SetFloat(string name, float v) { if (Animated) _w.Get<SpriteAnimator>(Entity).SetFloat(name, v); }
        public void Play(string state) { if (Animated) _w.Get<SpriteAnimator>(Entity).Play(state); }

        // ---------------------------------------------------------------- read-back (the animation editor's preview)

        private SpriteAnimator? Anim => Animated ? _w.Get<SpriteAnimator>(Entity) : (SpriteAnimator?)null;
        /// <summary>Playback speed multiplier; 0 pauses.</summary>
        public float speed { get => Anim?.Speed ?? 1f; set { if (Animated) _w.Get<SpriteAnimator>(Entity).Speed = value; } }
        public AnimatorState currentState => Anim is SpriteAnimator a && _controller != null && a.State >= 0 && a.State < _controller.states.Count ? _controller.states[a.State] : null;
        public float currentTime => Anim?.Time ?? 0f;
        public string currentStateName => currentState?.name;
        public bool GetBool(string name) => Anim?.GetBool(name) ?? false;
        public float GetFloat(string name) => Anim?.GetFloat(name) ?? 0f;
        public bool IsTriggerSet(string name) => Anim?.IsTriggerSet(name) ?? false;

        /// <summary>Destroys the entity (children parented to it go with it).</summary>
        public void Destroy()
        {
            AnimationEventRouter.Unregister(Entity);
            if (Alive) _w.Commands.DestroyEntity(Entity);
            Entity = Entity.Null;
        }
    }

    /// <summary>
    /// Delivers <see cref="AnimationEvent"/>s to the <see cref="AnimatedSprite.OnEvent"/> callbacks. An engine system
    /// on purpose — the animation editor hosts AnimatedSprites without registering any game assembly's systems.
    /// </summary>
    public sealed class AnimationEventRouter : SystemBase, IConsumes<AnimationEvent>
    {
        private static readonly Dictionary<Entity, Action<string>> Sinks = new Dictionary<Entity, Action<string>>();
        internal static void Register(Entity e, Action<string> sink) { if (!e.IsNull) Sinks[e] = sink; }
        internal static void Unregister(Entity e) => Sinks.Remove(e);

        public void Consume(SystemContext ctx, ReadOnlySpan<AnimationEvent> events)
        {
            foreach (var e in events)
                if (Sinks.TryGetValue(e.Entity, out var sink)) sink(e.Function);
        }
    }
}
