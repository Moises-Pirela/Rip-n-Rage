using System;
using System.Collections.Generic;
using System.Linq;

namespace RipNRage.Engine.Ecs
{
    public sealed partial class World
    {
        private interface IEventQueue
        {
            int Count { get; }
            void Dispatch(World w, List<SystemEntry> consumers);
            void Clear();
        }

        private sealed class EventQueue<T> : IEventQueue where T : struct, IPostProcessEvent
        {
            private T[] _items = new T[16];
            private int _count;
            private readonly object _lock = new object();
            public int Count => _count;

            public void Add(in T e)
            {
                lock (_lock) // systems in a parallel batch may post concurrently
                {
                    if (_count == _items.Length) Array.Resize(ref _items, _count * 2);
                    _items[_count++] = e;
                }
            }

            public void Dispatch(World w, List<SystemEntry> consumers)
            {
                var span = new ReadOnlySpan<T>(_items, 0, _count);
                foreach (var s in consumers)
                {
                    w.BeginIteration();
                    try { ((IConsumes<T>)s.System).Consume(s.Context, span); }
                    finally { w.EndIteration(); }
                    w.PlaybackSystem(s);
                }
            }

            public void Clear() { Array.Clear(_items, 0, _count); _count = 0; }
        }

        private readonly Dictionary<Type, IEventQueue> _eventQueues = new Dictionary<Type, IEventQueue>();
        private readonly Dictionary<Type, List<SystemEntry>> _consumers = new Dictionary<Type, List<SystemEntry>>();
        private readonly object _eventsLock = new object();

        /// <summary>Posts a gameplay event; every <see cref="IConsumes{T}"/> system receives it at the end of this frame.</summary>
        public void Post<T>(in T e) where T : struct, IPostProcessEvent
        {
            IEventQueue q;
            lock (_eventsLock)
                if (!_eventQueues.TryGetValue(typeof(T), out q)) _eventQueues[typeof(T)] = q = new EventQueue<T>();
            ((EventQueue<T>)q).Add(e);
        }

        public int PendingEvents<T>() where T : struct, IPostProcessEvent =>
            _eventQueues.TryGetValue(typeof(T), out var q) ? q.Count : 0;

        private void BuildConsumers()
        {
            _consumers.Clear();
            var byEvent = new Dictionary<Type, List<SystemEntry>>();
            foreach (var s in _systems)
                foreach (var i in s.Type.GetInterfaces())
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumes<>))
                    {
                        var evt = i.GetGenericArguments()[0];
                        if (!byEvent.TryGetValue(evt, out var list)) byEvent[evt] = list = new List<SystemEntry>();
                        list.Add(s);
                    }
            foreach (var kv in byEvent) _consumers[kv.Key] = TopoSort(kv.Value);
        }

        /// <summary>
        /// End of frame: each event type's list goes to its consumers (in dependency order), then is cleared.
        /// Events posted by consumers are delivered in a further round of the same frame (bounded).
        /// </summary>
        private void DispatchPostProcessEvents(float dt)
        {
            if (_scheduleDirty) RebuildSchedule();
            for (int round = 0; round < 8; round++)
            {
                IEventQueue[] queues;
                Type[] types;
                lock (_eventsLock) { types = _eventQueues.Where(k => k.Value.Count > 0).Select(k => k.Key).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray(); }
                if (types.Length == 0) return;
                // swap out the lists so consumers posting new events do not mutate the span they are reading
                queues = new IEventQueue[types.Length];
                lock (_eventsLock)
                    for (int i = 0; i < types.Length; i++)
                    {
                        queues[i] = _eventQueues[types[i]];
                        _eventQueues[types[i]] = (IEventQueue)Activator.CreateInstance(queues[i].GetType());
                    }
                for (int i = 0; i < types.Length; i++)
                {
                    if (_consumers.TryGetValue(types[i], out var list))
                    {
                        foreach (var s in list) { s.Context.DeltaTime = dt; s.Context.Frame = _frame; }
                        queues[i].Dispatch(this, list);
                    }
                    queues[i].Clear();
                }
                Sync();
            }
        }
    }
}
