using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics.Particles;

namespace RipNRage.Engine.Ecs.Effects
{
    /// <summary>Spawns a VFX library effect (vfx/*.json) at the end of the frame; optionally attached to an entity.</summary>
    public struct SpawnEffectEvent : IPostProcessEvent
    {
        public string Effect;
        public Vector3 Position, Forward;
        public float Scale;
        /// <summary>Entity the effect follows (Entity.Null = stays where spawned); <see cref="Offset"/> is in its local space.</summary>
        public Entity Follow;
        public Vector3 Offset;
        /// <summary>Trails: forward points backwards along the followed entity.</summary>
        public bool Backwards;
    }

    /// <summary>
    /// An effect that lives as long as the entity: spawned (following it) when the entity appears, stopped — its
    /// particles finish — when the entity or the component goes. Trails on projectiles, smoke on props, torches.
    /// </summary>
    public struct EffectEmitter : IComponent
    {
        public string Effect;
        public Vector3 Offset;
        public float Scale;
        public bool Backwards;
    }

    public sealed class EffectEmitterConfig : ComponentConfig<EffectEmitter>
    {
        public string effect;
        public float[] offset = { 0, 0, 0 };
        public float scale = 1f;
        public bool backwards;
        public override EffectEmitter Bake()
        {
            if (string.IsNullOrEmpty(effect)) throw new ArgumentException("an EffectEmitter needs an effect name", nameof(effect));
            return new EffectEmitter
            {
                Effect = effect, Scale = scale, Backwards = backwards,
                Offset = new Vector3(offset.Length > 0 ? offset[0] : 0f, offset.Length > 1 ? offset[1] : 0f, offset.Length > 2 ? offset[2] : 0f),
            };
        }
    }

    /// <summary>Script-side entry point: posts a <see cref="SpawnEffectEvent"/> to the application's world.</summary>
    public static class Vfx
    {
        public static void Spawn(string effect, Vector3 position, Vector3 forward, float scale = 1f, Entity follow = default, Vector3 offset = default, bool backwards = false)
        {
            var w = Application.Current?.World;
            if (w == null || string.IsNullOrEmpty(effect)) return;
            w.Post(new SpawnEffectEvent { Effect = effect, Position = position, Forward = forward, Scale = scale <= 0f ? 1f : scale, Follow = follow, Offset = offset, Backwards = backwards });
        }
    }

    /// <summary>Feeds the particle simulation (ParticleWorld) from the ECS: spawn events and entity-lifetime emitters.</summary>
    public sealed class EffectSystem : SystemBase, IConsumes<SpawnEffectEvent>, ILateTick, IOnEntityDestroyed, IOnSimEnd
    {
        private readonly Dictionary<Entity, ParticleEffectInstance> _emitters = new Dictionary<Entity, ParticleEffectInstance>();
        private readonly List<Entity> _gone = new List<Entity>();
        private Query _q;
        /// <summary>Effects spawned through the ECS (diagnostics, tests); unknown names are counted in <see cref="Unknown"/>.</summary>
        public int Spawned { get; private set; }
        public int Unknown { get; private set; }

        public override void OnCreate() => _q = World.Query().WithReadOnly<EffectEmitter>().WithReadOnly<LocalToWorld>().Build();

        public void Consume(SystemContext ctx, ReadOnlySpan<SpawnEffectEvent> events)
        {
            foreach (var e in events)
            {
                var def = VfxLibrary.Get(e.Effect);
                if (def == null) { Unknown++; continue; }
                Vector3 pos = e.Position, fwd = e.Forward.sqrMagnitude > 1E-8f ? e.Forward : Vector3.forward;
                if (!e.Follow.IsNull && World.IsAlive(e.Follow) && World.Has<LocalToWorld>(e.Follow))
                {
                    var ltw = World.Get<LocalToWorld>(e.Follow);
                    pos = ltw.Value.MultiplyPoint3x4(e.Offset);
                    fwd = ltw.Rotation * (e.Backwards ? Vector3.back : Vector3.forward);
                }
                var inst = ParticleWorld.Spawn(def, pos, fwd, e.Scale <= 0f ? 1f : e.Scale);
                if (inst == null) continue;
                if (!e.Follow.IsNull) inst.Follow(e.Follow, e.Offset, e.Backwards);
                Spawned++;
            }
        }

        public void LateTick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var em = chunk.Get<EffectEmitter>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || _emitters.ContainsKey(ents[i])) continue;
                    var def = VfxLibrary.Get(em[i].Effect);
                    if (def == null) { Unknown++; _emitters[ents[i]] = null; continue; }
                    var inst = ParticleWorld.Spawn(def, ltw[i].Value.MultiplyPoint3x4(em[i].Offset), ltw[i].Rotation * (em[i].Backwards ? Vector3.back : Vector3.forward), em[i].Scale <= 0f ? 1f : em[i].Scale);
                    inst?.Follow(ents[i], em[i].Offset, em[i].Backwards);
                    _emitters[ents[i]] = inst;
                    Spawned++;
                }
            }
            _gone.Clear();
            foreach (var kv in _emitters) if (!World.IsAlive(kv.Key) || !World.Has<EffectEmitter>(kv.Key)) _gone.Add(kv.Key);
            foreach (var e in _gone) Stop(e);
        }

        private void Stop(Entity e)
        {
            if (_emitters.TryGetValue(e, out var inst)) inst?.Stop();
            _emitters.Remove(e);
        }

        public void OnEntityDestroyed(SystemContext ctx, Entity e) => Stop(e);
        public void OnSimEnd(SystemContext ctx) { foreach (var i in _emitters.Values) i?.Stop(); _emitters.Clear(); }
    }
}
