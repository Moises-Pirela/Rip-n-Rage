using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>
    /// What a landed blow feels like on the scene side (9.4: was the migration bridge onto the EntityState facades,
    /// deaths left it in 8.3): a blow on the player kicks the camera; the player wounding an enemy flashes the
    /// crosshair's hit marker — which needs Killed, known only here, not at fire time.
    /// </summary>
    [Exclusive] // the camera kick and the HUD's crosshair
    public sealed class HitFeedbackSystem : SystemBase, IConsumes<HitEvent>
    {
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (e.Target == PlayerLink.PlayerEntity && !PlayerLink.IsDead) PlayerCamera.Kick(0.5f);
                else if (w.IsAlive(e.Target) && w.Has<AI.Enemy>(e.Target)) HudScreen.Current?.Crosshair?.OnHit();
            }
        }
    }
}
