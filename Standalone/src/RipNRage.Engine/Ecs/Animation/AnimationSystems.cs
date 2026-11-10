using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Ecs.Rendering;

namespace RipNRage.Engine.Ecs.Animation
{
    /// <summary>Animator parameters (floats; bools are 0 / 1) by the controller's parameter index.</summary>
    [InlineArray(SpriteAnimator.MaxParameters)]
    public struct AnimatorParams { private float _e0; }

    /// <summary>
    /// Plays an <see cref="AnimatorController"/> (states, transitions, clips from animations/*.json) on the entity's
    /// <see cref="SpriteRender"/>: the <see cref="AnimationSystem"/> advances time, takes transitions, writes the
    /// sprite, applies clip "enabled" keys to <see cref="SpriteRender.Disabled"/>, and posts clip events as
    /// <see cref="AnimationEvent"/>s. Entities tagged <see cref="Hidden"/> are paused.
    /// Plain data: parameters live in a fixed block indexed by <see cref="AnimationAssets.ParameterIndex"/>.
    /// Gameplay sets parameters / plays states through the instance methods (call them on the component by ref).
    /// </summary>
    public struct SpriteAnimator : IComponent
    {
        public const int MaxParameters = 8;
        public AssetHandle<AnimatorController> Controller;
        /// <summary>Index of the current state in the controller (-1: not started; the system enters the default state).</summary>
        public int State;
        public float Time, Speed;
        public AnimatorParams Params;
        /// <summary>Set triggers, one bit per parameter index (consumed by the transition that used them).</summary>
        public uint Triggers;
        /// <summary>A state the gameplay asked for (-1 = none): entered at the next update, time 0.</summary>
        public int PendingState;
        internal int ControllerVersion;

        private int Index(string name) => AnimationAssets.ParameterIndex(Controller, name);

        public void SetFloat(string name, float v) { int i = Index(name); if (i >= 0) Params[i] = v; }
        public float GetFloat(string name) { int i = Index(name); return i >= 0 ? Params[i] : 0f; }
        public void SetBool(string name, bool v) => SetFloat(name, v ? 1f : 0f);
        public bool GetBool(string name) => GetFloat(name) != 0f;
        public void SetInteger(string name, int v) => SetFloat(name, v);
        public void SetTrigger(string name) { int i = Index(name); if (i >= 0) Triggers |= 1u << i; }
        public void ResetTrigger(string name) { int i = Index(name); if (i >= 0) Triggers &= ~(1u << i); }
        public bool IsTriggerSet(string name) { int i = Index(name); return i >= 0 && (Triggers & (1u << i)) != 0; }

        /// <summary>Jumps to a state by name at the next update (Animator.Play).</summary>
        public void Play(string stateName)
        {
            var c = AnimationAssets.Controllers.Resolve(Controller);
            int i = c != null ? c.states.FindIndex(s => s.name == stateName) : -1;
            if (i >= 0) PendingState = i;
        }

        public string StateName => AnimationAssets.Controllers.Resolve(Controller) is AnimatorController c && State >= 0 && State < c.states.Count ? c.states[State].name : null;

        /// <summary>Normalised time in the current clip (≥ 1 once a non-looping clip ended).</summary>
        public float NormalizedTime
        {
            get
            {
                var c = AnimationAssets.Controllers.Resolve(Controller);
                var clip = c != null && State >= 0 && State < c.states.Count ? c.GetClip(c.states[State].clipName) : null;
                return clip != null && clip.length > 0f ? Time / clip.length : 1f;
            }
        }
    }

    public sealed class SpriteAnimatorConfig : ComponentConfig<SpriteAnimator>
    {
        /// <summary>Controller name (animations/controllers/*.json or code-registered).</summary>
        public string controller;
        public float speed = 1f;
        public string startState;
        public override SpriteAnimator Bake()
        {
            if (string.IsNullOrEmpty(controller)) throw new ArgumentException("a SpriteAnimator needs a controller name", nameof(controller));
            var h = AnimationAssets.Controllers.GetOrLoad(controller, n => AnimationLibrary.GetController(n) ?? throw new KeyNotFoundException($"animator controller '{n}' not found"));
            var a = new SpriteAnimator { Controller = h, State = -1, PendingState = -1, Speed = speed };
            if (!string.IsNullOrEmpty(startState)) a.Play(startState);
            return a;
        }
    }

    /// <summary>A clip event (the legacy SendMessage function name) reached by an entity's animation this frame.</summary>
    public struct AnimationEvent : IPostProcessEvent { public Entity Entity; public string Function; }

    public static class AnimationAssets
    {
        public static readonly AssetRegistry<AnimatorController> Controllers = new AssetRegistry<AnimatorController>();

        /// <summary>Parameter index by name (ordinal order of the controller's parameter names); -1 = unknown or beyond <see cref="SpriteAnimator.MaxParameters"/>.</summary>
        public static int ParameterIndex(AssetHandle<AnimatorController> h, string name)
        {
            var c = Controllers.Resolve(h);
            if (c == null || name == null) return -1;
            var order = Order(c);
            int i = order.IndexOf(name);
            return i < SpriteAnimator.MaxParameters ? i : -1;
        }

        private static readonly ConditionalWeakTable<AnimatorController, Tuple<int, List<string>>> _orders = new ConditionalWeakTable<AnimatorController, Tuple<int, List<string>>>();
        internal static List<string> Order(AnimatorController c)
        {
            if (_orders.TryGetValue(c, out var t) && t.Item1 == c.version) return t.Item2;
            var list = new List<string>(c.parameters.Keys);
            list.Sort(StringComparer.Ordinal);
            if (list.Count > SpriteAnimator.MaxParameters) Debug.LogWarning($"[animation] controller '{c.name}' has {list.Count} parameters; only {SpriteAnimator.MaxParameters} fit a SpriteAnimator");
            _orders.AddOrUpdate(c, Tuple.Create(c.version, list));
            return list;
        }
    }

    /// <summary>Advances every <see cref="SpriteAnimator"/> (Tick): the legacy Animator's semantics, on components.</summary>
    [Writes(typeof(SpriteAnimator), typeof(SpriteRender))]
    public sealed class AnimationSystem : SystemBase, ITick
    {
        private Query _q;
        // hidden entities do not animate (the legacy Animator did not run on inactive objects either)
        public override void OnCreate() => _q = World.Query().With<SpriteAnimator>().With<SpriteRender>().Without<Hidden>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var an = chunk.Get<SpriteAnimator>(); var sr = chunk.Get<SpriteRender>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) Step(ctx, ents[i], ref an[i], ref sr[i], ctx.DeltaTime);
            }
        }

        internal static void Step(SystemContext ctx, Entity e, ref SpriteAnimator a, ref SpriteRender r, float dt)
        {
            var c = AnimationAssets.Controllers.Resolve(a.Controller);
            if (c == null || c.states.Count == 0) return;
            if (a.ControllerVersion != c.version || a.State >= c.states.Count)
            {
                // the graph was rebuilt (hot reload): keep the state with the same name
                string name = a.State >= 0 && a.State < c.states.Count ? c.states[a.State].name : null;
                a.ControllerVersion = c.version;
                int again = name != null ? c.states.FindIndex(s => s.name == name) : -1;
                if (again != a.State) { a.State = again; a.Time = 0f; }
            }
            if (a.PendingState >= 0) { a.State = a.PendingState; a.PendingState = -1; a.Time = 0f; Apply(ctx, e, c, ref a, ref r, -1f, 0f); }
            if (a.State < 0)
            {
                a.State = c.defaultState != null ? c.states.IndexOf(c.defaultState) : 0;
                a.Time = 0f;
                Apply(ctx, e, c, ref a, ref r, -1f, 0f);
            }
            if (TryTransition(c, ref a)) Apply(ctx, e, c, ref a, ref r, -1f, a.Time);

            var state = c.states[a.State];
            var clip = c.GetClip(state.clipName);
            float prev = a.Time;
            a.Time += dt * MathF.Max(0f, a.Speed) * state.speed; // speed 0 pauses
            if (clip != null)
            {
                if (clip.loop && clip.length > 0f)
                {
                    if (a.Time >= clip.length)
                    {
                        FireEvents(ctx, e, clip, prev, clip.length + 1E-04f); // the tail of the loop, then wrap
                        a.Time = Mathf.Repeat(a.Time, clip.length);
                        prev = -1f;
                    }
                }
                else if (a.Time > clip.length) a.Time = clip.length;
            }
            Apply(ctx, e, c, ref a, ref r, prev, a.Time);
        }

        private static bool TryTransition(AnimatorController c, ref SpriteAnimator a)
        {
            var state = c.states[a.State];
            var clip = c.GetClip(state.clipName);
            float len = clip?.length ?? 0f;
            float normalized = len > 0f ? a.Time / len : 1f;
            foreach (var t in c.anyStateTransitions)
            {
                if (t.destination == state) continue;
                if (t.hasExitTime && normalized < t.exitTime) continue;
                if (!ConditionsMet(c, ref a, t)) continue;
                Take(c, ref a, t); return true;
            }
            foreach (var t in state.transitions)
            {
                if (t.hasExitTime && normalized < t.exitTime) continue;
                if (!ConditionsMet(c, ref a, t)) continue;
                Take(c, ref a, t); return true;
            }
            return false;
        }

        private static void Take(AnimatorController c, ref SpriteAnimator a, AnimatorTransition t)
        {
            foreach (var cond in t.conditions)
                if (c.parameters.TryGetValue(cond.parameter, out var type) && type == AnimatorParameterType.Trigger) a.ResetTrigger(cond.parameter);
            a.State = c.states.IndexOf(t.destination);
            a.Time = 0f;
        }

        private static bool ConditionsMet(AnimatorController c, ref SpriteAnimator a, AnimatorTransition t)
        {
            if (t.conditions.Count == 0) return t.hasExitTime;
            foreach (var cond in t.conditions)
            {
                bool isTrigger = c.parameters.TryGetValue(cond.parameter, out var type) && type == AnimatorParameterType.Trigger;
                float v = a.GetFloat(cond.parameter);
                switch (cond.mode)
                {
                    case AnimatorConditionMode.If: if (isTrigger ? !a.IsTriggerSet(cond.parameter) : v == 0f) return false; break;
                    case AnimatorConditionMode.IfNot: if (isTrigger ? a.IsTriggerSet(cond.parameter) : v != 0f) return false; break;
                    case AnimatorConditionMode.Greater: if (!(v > cond.threshold)) return false; break;
                    case AnimatorConditionMode.Less: if (!(v < cond.threshold)) return false; break;
                    case AnimatorConditionMode.Equals: if (v != cond.threshold) return false; break;
                    case AnimatorConditionMode.NotEqual: if (v == cond.threshold) return false; break;
                }
            }
            return true;
        }

        private static void Apply(SystemContext ctx, Entity e, AnimatorController c, ref SpriteAnimator a, ref SpriteRender r, float prev, float time)
        {
            var clip = c.GetClip(c.states[a.State].clipName);
            if (clip == null) return;
            var s = clip.SampleSprite(time);
            if (s != null) r.Sprite = RenderAssets.Sprites.Intern(s);
            var en = clip.SampleEnabled(time);
            if (en.HasValue) r.Disabled = !en.Value;
            FireEvents(ctx, e, clip, prev, time);
        }

        private static void FireEvents(SystemContext ctx, Entity e, SpriteAnimationClip clip, float from, float to)
        {
            if (clip.events.Count == 0) return;
            foreach (var (t, fn) in clip.events)
                if (t > from && t <= to) ctx.Post(new AnimationEvent { Entity = e, Function = fn });
        }
    }
}
