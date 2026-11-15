using System;

namespace RipNRage.Engine
{
    /// <summary>Base of the shared asset objects — textures, sprites, meshes, clips (9.7: all that is left of the
    /// legacy object model; the scene is the World).</summary>
    public abstract class EngineObject
    {
        private static int _nextId = 1;
        public int instanceId { get; } = _nextId++;
        public string name { get; set; } = "";
        internal bool destroyed;
        /// <summary>True once Destroy() has run for this object.</summary>
        public bool IsDestroyed => destroyed;

        public override string ToString() => $"{GetType().Name}({name})";
    }
}
