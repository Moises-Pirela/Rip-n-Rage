using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// A GameObject that wants to hear about overlaps of its ECS trigger volume. The physics
    /// <see cref="TriggerSystem"/> posts <see cref="TriggerEnterEvent"/> / <see cref="TriggerExitEvent"/>; the
    /// <see cref="GameTriggerBridge"/> resolves both ends to GameObjects and calls these.
    ///
    /// <para>This is the contract the deleted engine-side <c>ITriggerListener</c> had, so the listeners that were
    /// written against it (checkpoints, water, pickups, the player's own volume) work unchanged. It goes away with
    /// the GameObjects in layer 9, when the listeners become <see cref="IConsumes{T}"/> systems.</para>
    /// </summary>
    public interface ITriggerListener
    {
        void OnTriggerEnter(GameObject other);
        void OnTriggerExit(GameObject other);
    }

    /// <summary>
    /// Entity ↔ GameObject directory for the migration bridges. Anything that owns both halves registers here so the
    /// event bridges can turn an <see cref="Entity"/> from a physics event back into the GameObject the unported
    /// scripts expect. Entries are dropped when either half dies.
    /// </summary>
    public static class EcsObjects
    {
        private static readonly Dictionary<Entity, GameObject> ByEntity = new Dictionary<Entity, GameObject>();

        public static void Register(Entity e, GameObject go)
        {
            if (e.IsNull || go == null) return;
            ByEntity[e] = go;
        }

        public static void Unregister(Entity e)
        {
            if (!e.IsNull) ByEntity.Remove(e);
        }

        /// <summary>The GameObject standing in for <paramref name="e"/>, or null if nothing claims it.</summary>
        public static GameObject Of(Entity e)
        {
            if (e.IsNull) return null;
            if (!ByEntity.TryGetValue(e, out var go)) return null;
            if (go == null || go.IsDestroyed) { ByEntity.Remove(e); return null; }
            return go;
        }

        public static void Clear() => ByEntity.Clear();
    }

    /// <summary>Owns a trigger volume entity on behalf of a GameObject and keeps the two linked.</summary>
    public sealed class TriggerVolume : MonoBehaviour
    {
        public Entity entity { get; private set; }

        /// <summary>Links <paramref name="go"/> to an existing trigger entity (one volume per GameObject).</summary>
        public static TriggerVolume Attach(GameObject go, Entity e)
        {
            var v = go.AddComponent<TriggerVolume>();
            v.entity = e;
            EcsObjects.Register(e, go);
            return v;
        }

        protected override void OnDestroy()
        {
            EcsObjects.Unregister(entity);
            var w = Application.Current?.World;
            if (w != null && w.IsAlive(entity)) w.Commands.DestroyEntity(entity);
        }
    }

    /// <summary>
    /// Keeps a <see cref="TriggerVolume"/>'s entity on top of the GameObject that moves (an ammo pickup bobs up and
    /// down). Writing <see cref="LocalTransform"/> on a body-less collider makes the physics sync re-bake its world
    /// shape, so the volume follows exactly.
    /// </summary>
    public sealed class TriggerFollower : MonoBehaviour
    {
        private TriggerVolume _volume;

        protected override void LateUpdate()
        {
            _volume ??= GetComponent<TriggerVolume>();
            if (_volume == null) return;
            var w = Application.Current?.World;
            if (w == null || !w.IsAlive(_volume.entity) || !w.Has<LocalTransform>(_volume.entity)) return;
            ref var lt = ref w.Get<LocalTransform>(_volume.entity);
            lt.Position = transform.position;
            lt.Rotation = transform.rotation;
        }
    }

    /// <summary>
    /// Turns the physics trigger events back into the <see cref="ITriggerListener"/> calls the unported gameplay is
    /// written against: checkpoints save, the water volume drowns and fogs the view, ammo pickups are collected.
    ///
    /// <para>Both ends are resolved through <see cref="EcsObjects"/>. An event whose <b>trigger</b> side has no
    /// GameObject is ignored (nothing is listening); an event whose <b>other</b> side has none is still delivered
    /// with a null argument only if the listener tolerates it — in practice every listener filters by tag, so
    /// unclaimed entities (loose crates, ragdoll pieces) are simply dropped here.</para>
    /// </summary>
    [UpdateAfter(typeof(TriggerSystem))]
    public sealed class GameTriggerBridge : SystemBase, IConsumes<TriggerEnterEvent>, IConsumes<TriggerExitEvent>
    {
        public void Consume(SystemContext ctx, ReadOnlySpan<TriggerEnterEvent> events)
        {
            foreach (var e in events) Dispatch(e.Trigger, e.Other, enter: true);
        }

        public void Consume(SystemContext ctx, ReadOnlySpan<TriggerExitEvent> events)
        {
            foreach (var e in events) Dispatch(e.Trigger, e.Other, enter: false);
        }

        private static void Dispatch(Entity trigger, Entity other, bool enter)
        {
            var triggerGo = EcsObjects.Of(trigger);
            var otherGo = EcsObjects.Of(other);
            // nothing on either side is listening, or the overlapping entity has no GameObject a listener could use
            if (triggerGo == null || otherGo == null) return;

            // both sides hear about the overlap, as Unity's OnTriggerEnter did: the volume learns who walked in
            // (a checkpoint saves, a pickup is collected) and the walker learns what it walked into.
            Notify(triggerGo, otherGo, enter);
            Notify(otherGo, triggerGo, enter);
        }

        private static void Notify(GameObject self, GameObject other, bool enter)
        {
            foreach (var l in self.GetComponents<ITriggerListener>())
            {
                if (l is Behaviour b && (!b.enabled || b.IsDestroyed)) continue;
                if (enter) l.OnTriggerEnter(other);
                else l.OnTriggerExit(other);
            }
        }
    }
}
