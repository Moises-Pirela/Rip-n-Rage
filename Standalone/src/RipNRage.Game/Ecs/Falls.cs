using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content.Level;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>The level's kill height: below it the player has fallen out of the level (set by the level build).</summary>
    /// <summary>What the last fall was: off an edge, through geometry (a collision bug), or out of nowhere.</summary>
    public enum FallKind { None, Edge, ThroughGeometry, Unknown }

    /// <summary>
    /// The player's fall watch (8.4; replaces the <c>FallRespawn</c> MonoBehaviour): where they last stood, where they
    /// first dropped clearly below it, and the verdict on the last fall. Falls are always lethal (the arcade respawn
    /// was dropped at review, Q2); the classification stays, so a collision bug cannot hide behind a walk off an edge.
    /// </summary>
    public struct FallWatch : IComponent
    {
        public Vector3 LastGrounded, DropPosition;
        /// <summary>Real time of the drop (for the report's "reached the kill height N s later").</summary>
        public float DropTime;
        public bool DropRecorded;
        /// <summary>Falls this life (the overlay's FALLS counter and the gates' probe).</summary>
        public int Count;
        public FallKind LastKind;
    }

    /// <summary>The fall watch's rules, pure: what one tick of standing / falling records.</summary>
    public static class Falls
    {
        /// <summary>How far below the level last stood on the player must be before the drop spot is recorded.</summary>
        public const float DropMargin = 0.5f;

        public static FallWatch Track(in FallWatch f, Vector3 position, bool grounded, float now)
        {
            var n = f;
            if (grounded) { n.LastGrounded = position; n.DropRecorded = false; }
            else if (!n.DropRecorded && position.y < n.LastGrounded.y - DropMargin)
            {
                // the spot where the player first is clearly below the level they last stood on: for a walk or jump
                // off an edge that is beyond the edge, for a fall through the floor it is inside / under the floor
                n.DropPosition = position; n.DropTime = now; n.DropRecorded = true;
            }
            return n;
        }

        public static string Label(FallKind k) => k switch
        {
            FallKind.Edge => "EDGE", FallKind.ThroughGeometry => "THROUGH GEOMETRY", FallKind.Unknown => "UNKNOWN", _ => "-",
        };
    }

    /// <summary>
    /// Watches the player against the level's kill height (<c>Worldspawn.KillY</c>). Below it: the fall is classified — the spot of
    /// the drop is probed straight down, floor at the level last stood on means the player went <b>through</b>
    /// geometry, none means <b>off an edge</b> — logged, and the player is killed by a fatal
    /// <see cref="DamageEvent"/> (the one door, 8.1). A dead player is not watched.
    /// </summary>
    [Reads(typeof(PlayerMotor))] // the swim state: a swimmer is not falling
    [Exclusive] // the physics raycast, the log, the death screen's cause line and the HUD message
    public sealed class FallSystem : SystemBase, ITick
    {
        private const int GroundMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision);

        private Query _players;

        public override void OnCreate()
        {
            _players = World.Query().With<FallWatch>().WithReadOnly<LocalTransform>().WithReadOnly<CharacterMotor>()
                .WithReadOnly<PlayerTag>().Without<Dead>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            float killY = ctx.World.GetSingleton<RipNRage.Engine.Ecs.Levels.Worldspawn>().KillY; // the level's environment (10.5)
            float now = Time.unscaledTime;
            foreach (var chunk in _players)
            {
                var watches = chunk.Get<FallWatch>(); var xforms = chunk.Get<LocalTransform>();
                var motors = chunk.Get<CharacterMotor>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    Vector3 pos = xforms[i].Position;
                    watches[i] = Falls.Track(in watches[i], pos, motors[i].IsGrounded, now);
                    if (pos.y >= killY) continue;
                    // a swimmer is not falling: in water a level means to be swum in, the kill height is not the
                    // rule - running out of breath is (PlayerMoveSystem.UpdateSwim). Optional, because a headless
                    // test player is a body and a fall watch, with no locomotion on it at all.
                    if (ctx.World.Has<PlayerMotor>(ents[i]) && ctx.World.Get<PlayerMotor>(ents[i]).IsSwimming) continue;
                    if (ctx.World.Has<Health>(ents[i]) && ctx.World.Get<Health>(ents[i]).Current <= 0f) continue; // the fatal blow is in flight

                    ref var f = ref watches[i];
                    f.Count++;
                    f.LastKind = Classify(in f, killY, now);
                    bool edge = f.LastKind == FallKind.Edge;
                    GameSaveSystem.OnMessage?.Invoke(edge ? "You fell into the harbour." : "You fell out of the world.");
                    DeathScreen.NextCause = edge ? "YOU FELL INTO THE HARBOUR" : "YOU FELL OUT OF THE WORLD";
                    // the body stops where it is: the velocity goes, and so does this frame's input — the fixed steps
                    // after this tick would otherwise move the player again before the death (end of frame) blocks it
                    PlayerLink.ResetVelocity();
                    ctx.World.Singleton<RipNRage.Engine.Ecs.Input.InputState>() = default;
                    ctx.Post(new DamageEvent { Target = ents[i], Fatal = true, Point = pos, Direction = Vector3.down });
                }
            }
        }

        private static FallKind Classify(in FallWatch f, float killY, float now)
        {
            FallKind kind;
            string report;
            if (!f.DropRecorded)
            {
                kind = FallKind.Unknown;
                report = $"Fall #{f.Count}: fell below y={killY:0.0} without ever standing on ground (spawned or teleported in the air?). Last grounded at {F(f.LastGrounded)}.";
            }
            else
            {
                Vector3 origin = new Vector3(f.DropPosition.x, f.LastGrounded.y + 2f, f.DropPosition.z);
                float fallTime = now - f.DropTime;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f + 1.5f, GroundMask))
                {
                    kind = FallKind.ThroughGeometry;
                    report = $"Fall #{f.Count}: FELL THROUGH GEOMETRY — dropped through {hit.entity} (top at y={hit.point.y:0.00}) at {F(f.DropPosition)}, {fallTime:0.0}s before reaching the kill height. Last grounded at {F(f.LastGrounded)}. This is a collision bug: please report it with these numbers.";
                }
                else
                {
                    kind = FallKind.Edge;
                    report = $"Fall #{f.Count}: went off an edge — at {F(f.DropPosition)} there is no floor at the level last stood on ({F(f.LastGrounded)}); reached the kill height y={killY:0.0} {fallTime:0.0}s later.";
                }
            }
            Debug.LogWarning(report);
            return kind;
        }

        private static string F(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        /// <summary>The player's fall watch, for the overlay and the probes (outside the ECS).</summary>
        public static FallWatch Current
        {
            get
            {
                var w = Application.Current?.World;
                var e = PlayerLink.PlayerEntity;
                return w != null && !e.IsNull && w.IsAlive(e) && w.Has<FallWatch>(e) ? w.Get<FallWatch>(e) : default;
            }
        }
    }
}
