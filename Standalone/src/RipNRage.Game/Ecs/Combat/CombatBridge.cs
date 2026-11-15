using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>
    /// MIGRATION BRIDGE (shrinks through layers 5–8, gone in layer 9): delivers the combat events to the
    /// GameObject-side listeners that are not ported yet. A <see cref="HitEvent"/> becomes the target
    /// <see cref="EntityState"/>'s change notifications (HUD bars, hit flash, the player's damage shake). Deaths need
    /// no bridge: enemies' are <see cref="RipNRage.Game.Ecs.AI.EnemyDeathSystem"/>'s (6.6), the player's
    /// <see cref="RipNRage.Game.Ecs.PlayerDeathSystem"/>'s (8.3). Entities are
    /// mapped to their GameObjects through <see cref="EcsObjects"/>, the directory the trigger bridge already keeps.
    /// </summary>
    public sealed class CombatBridgeSystem : SystemBase, IConsumes<HitEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            foreach (ref readonly var e in events)
            {
                var state = StateOf(e.Target);
                if (state != null) state.NotifyHit(in e);

                // the player's hit feedback on a wounded enemy: the crosshair hit marker, rage, and the style
                // meter — which needs Killed, known only here, not at fire time. (Enemy fire never targets enemies.)
                var w = ctx.World;
                if (w.IsAlive(e.Target) && w.Has<RipNRage.Game.Ecs.AI.Enemy>(e.Target))
                {
                    EnragedInjector<HudController>.GetInstance()?.CurrentCrosshair?.OnHit();
                }
            }
        }

        private static EntityState StateOf(Entity e)
        {
            var go = EcsObjects.Of(e);
            return go != null ? go.GetComponent<EntityState>() : null;
        }
    }
}
