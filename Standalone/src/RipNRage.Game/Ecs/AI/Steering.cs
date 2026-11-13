using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// Which way the enemy faces, as gameplay data. The capsule is symmetric and the sprite a billboard, so the body
    /// never turns — a <c>LocalTransform</c> rotation write would read as a teleport to the physics sync every tick.
    /// Steering turns this; the sprite row, the sight cone and the following GameObject read it.
    /// </summary>
    public struct Heading : IComponent
    {
        /// <summary>Degrees about +y, the Unity convention (0 = +z).</summary>
        public float Yaw;

        public Vector3 Forward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }

    /// <summary>The body side of the intent, as a pure function: intent in, wanted velocity and facing out.</summary>
    public static class Steering
    {
        /// <summary>How far ahead the ledge guard looks for ground, and how far down.</summary>
        public const float LookAhead = 1.5f, Drop = 3f;

        /// <summary>
        /// Walk towards the move point at the definition's speed (or stop, when the ground ahead is missing), turn
        /// to face the way we go — or, standing, whatever the attack aims at — at the definition's turn speed.
        /// </summary>
        public static void Step(ref CharacterMotor motor, ref Heading heading, in AiIntent intent, EnemyData def, Vector3 self, bool groundAhead, float dt)
        {
            motor.Desired = Vector3.zero;
            Vector3 face = Vector3.zero;
            if (intent.Move)
            {
                Vector3 dir = intent.MoveTo - self; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f)
                {
                    dir.Normalize();
                    face = dir;
                    if (groundAhead) motor.Desired = dir * def.movementSpeed;
                }
            }
            else if (intent.Attack)
            {
                face = intent.FacePoint - self; face.y = 0f;
            }
            if (face.sqrMagnitude > 1e-4f)
                heading.Yaw = Mathf.MoveTowardsAngle(heading.Yaw, Heading.YawOf(face), def.turnSpeed * dt);
        }

        /// <summary>The ledge guard: is there level ground a step ahead in <paramref name="direction"/> (flattened), within <paramref name="reach"/> below the body centre?</summary>
        public static bool GroundAhead(Vector3 self, Vector3 direction, float reach)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return true;
            return PhysicsApi.Raycast(self + direction.normalized * LookAhead, Vector3.down, reach, Noises.WallMask);
        }
    }

    /// <summary>
    /// Moves every enemy body on its brain's intent: the motor gets the wanted velocity, the heading turns, and the
    /// sheet shows walking or standing (never over an attack or a death the GameObject half is playing). A dead
    /// enemy's body is braked. Runs alone (the ledge guard is a physics query).
    /// </summary>
    [UpdateAfter(typeof(AiBrainSystem))]
    public sealed class SteeringSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<CharacterMotor>().With<Heading>().With<EnemyVisual>()
                .WithReadOnly<AiIntent>().WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().Build();

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            foreach (var chunk in _q)
            {
                var motors = chunk.Get<CharacterMotor>(); var headings = chunk.Get<Heading>(); var visuals = chunk.Get<EnemyVisual>();
                var intents = chunk.Get<AiIntent>(); var kinds = chunk.Get<Enemy>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var m = ref motors[i];
                    if (World.Has<Dead>(ents[i])) { m.Desired = Vector3.zero; continue; }
                    // a hit holds them for the definition's stagger (the legacy StopAgent, which has done nothing since 2.L)
                    if (World.Has<EnemyWeapon>(ents[i]) && World.Get<EnemyWeapon>(ents[i]).StaggerLeft > 0f)
                    {
                        m.Desired = Vector3.zero;
                        continue;
                    }
                    var def = Enemies.All[kinds[i].Kind];
                    Vector3 self = ltw[i].Position;
                    float reach = 0.5f * AIState.SpriteHeight + m.RideHeight + Steering.Drop; // centre → feet → a drop below
                    bool ground = !intents[i].Move || Steering.GroundAhead(self, intents[i].MoveTo - self, reach);
                    Steering.Step(ref m, ref headings[i], in intents[i], def, self, ground, dt);

                    ref var v = ref visuals[i];
                    if (v.State == AnimationState.IDLE || v.State == AnimationState.MOVING)
                        v.State = m.Desired.sqrMagnitude > 0f ? AnimationState.MOVING : AnimationState.IDLE;
                }
            }
        }
    }
}
