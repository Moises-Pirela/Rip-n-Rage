using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// Records structural changes and applies them, in recording order, at a sync point. Entities created through a
    /// buffer get a valid handle immediately (components can be added to it in the same buffer). Not thread-safe:
    /// one buffer per system — the scheduler gives every system its own and plays them back in system order, so the
    /// result is deterministic regardless of which worker ran what.
    /// </summary>
    public sealed class CommandBuffer
    {
        private enum Op : byte { Create, Destroy, Add, Remove }
        private struct Cmd { public Op Op; public Entity Entity; public IPayload Payload; public int Slot; }

        private interface IPayload { void Apply(World w, Entity e, int slot); void Clear(); }
        private sealed class Adds<T> : IPayload where T : struct, IComponent
        {
            public readonly List<T> Values = new List<T>();
            public void Apply(World w, Entity e, int slot) => w.AddNow(e, Values[slot]);
            public void Clear() => Values.Clear();
        }
        private sealed class Removes<T> : IPayload where T : struct, IComponent
        {
            public static readonly Removes<T> Instance = new Removes<T>();
            public void Apply(World w, Entity e, int slot) => w.RemoveNow<T>(e);
            public void Clear() { }
        }

        private readonly World _world;
        private readonly List<Cmd> _cmds = new List<Cmd>();
        private readonly Dictionary<Type, IPayload> _payloads = new Dictionary<Type, IPayload>();
        public int Count => _cmds.Count;

        internal CommandBuffer(World world) { _world = world; }

        public Entity CreateEntity()
        {
            var e = _world.Reserve();
            _cmds.Add(new Cmd { Op = Op.Create, Entity = e });
            return e;
        }

        public void DestroyEntity(Entity e) => _cmds.Add(new Cmd { Op = Op.Destroy, Entity = e });

        public void Add<T>(Entity e, in T value) where T : struct, IComponent
        {
            if (!_payloads.TryGetValue(typeof(T), out var p)) _payloads[typeof(T)] = p = new Adds<T>();
            var adds = (Adds<T>)p;
            adds.Values.Add(value);
            _cmds.Add(new Cmd { Op = Op.Add, Entity = e, Payload = adds, Slot = adds.Values.Count - 1 });
        }

        public void Remove<T>(Entity e) where T : struct, IComponent =>
            _cmds.Add(new Cmd { Op = Op.Remove, Entity = e, Payload = Removes<T>.Instance });

        /// <summary>Applies every command (only at a sync point). Lifecycle events are queued on the world.</summary>
        internal void Playback()
        {
            for (int i = 0; i < _cmds.Count; i++)
            {
                var c = _cmds[i];
                switch (c.Op)
                {
                    case Op.Create: _world.Materialize(c.Entity); _world.QueueCreated(c.Entity); break;
                    case Op.Destroy: _world.QueueDestroyed(c.Entity); break;
                    default: if (_world.IsAlive(c.Entity)) c.Payload.Apply(_world, c.Entity, c.Slot); break;
                }
            }
            _cmds.Clear();
            foreach (var p in _payloads.Values) p.Clear();
        }
    }
}
