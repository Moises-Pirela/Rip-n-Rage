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

                // the player's hit feedback (every shooter is the player until layer 6 arms the AI): the crosshair
                // hit marker, rage, and the style meter — which needs Killed, known only here, not at fire time
                if (state is AIState)
                {
                    EnragedInjector<HudController>.GetInstance()?.CurrentCrosshair?.OnHit();
                    PlayerState.Current?.AddToRage(0.1f);
                    var w = ctx.World;
                    if (w.IsAlive(PlayerLink.PlayerEntity) && w.Has<Weapon>(PlayerLink.PlayerEntity))
                        StyleMeter.Current?.RegisterHit(Weapons.Data(w.Get<Weapon>(PlayerLink.PlayerEntity).Slot).name, e.Headshot, e.Killed);
                }
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
