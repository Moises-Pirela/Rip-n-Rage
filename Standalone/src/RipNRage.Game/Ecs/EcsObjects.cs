using System.Collections.Generic;
using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// Entity ↔ GameObject directory for the migration bridges (layer 9 deletes it with the GameObjects). Anything that
    /// owns both halves registers here so a bridge can turn an <see cref="Entity"/> from an event back into the
    /// GameObject the unported scripts expect — today the combat bridge, for the player's and the facades' hit
    /// feedback. (The trigger bridge that also used it is gone since 7.6: every trigger listener is a system.)
    /// Entries are dropped when either half dies.
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

    }
}
