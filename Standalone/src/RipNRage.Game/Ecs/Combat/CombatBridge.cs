using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>
    /// MIGRATION BRIDGE (shrinks through layers 5–8, gone in layer 9): delivers the combat events to the
    /// GameObject-side listeners that are not ported yet. A <see cref="HitEvent"/> becomes the target
    /// <see cref="EntityState"/>'s change notifications (HUD bars, hit flash, the player's damage shake); a
    /// <see cref="DeathEvent"/> becomes its <c>Die()</c> — the death sequence, the enemy death VFX, the ragdoll
    /// slicer. Entities are mapped to their GameObjects through <see cref="EcsObjects"/>, the directory the trigger
    /// bridge already keeps.
    /// </summary>
    public sealed class CombatBridgeSystem : SystemBase, IConsumes<HitEvent>, IConsumes<DeathEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            foreach (ref readonly var e in events)
            {
                var state = StateOf(e.Target);
                if (state != null) state.NotifyHit(in e);
            }
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            foreach (ref readonly var e in events)
            {
                var state = StateOf(e.Entity);
                if (state != null) state.NotifyDied(in e);
            }
        }

        private static EntityState StateOf(Entity e)
        {
            var go = EcsObjects.Of(e);
            return go != null ? go.GetComponent<EntityState>() : null;
        }
    }
}
