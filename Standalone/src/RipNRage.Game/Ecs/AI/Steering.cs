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

        /// <summary>Metres within which another body is steered around.</summary>
        public const float AvoidRadius = 2.5f;

        /// <summary>One neighbour as the job sees it: where it is and where it wants to go.</summary>
        public struct Neighbour { public Vector3 Position, Desired; public Entity Entity; }

        /// <summary>
        /// Local avoidance, RVO-lite: for every neighbour within <see cref="AvoidRadius"/> that this body is closing
        /// on, add half the separating velocity (each side does half, so two symmetric bodies do not mirror each
        /// other for ever) plus a sideways step. The side is the perpendicular of the line between them, and that
        /// line is antisymmetric — the other body's perpendicular is the opposite vector — so two bodies head-on
        /// step to opposite sides with no tie-break at all (a first version added a sign by placement seed and so
        /// flipped it back: both stepped the same way). A path corner is never abandoned for it: the result is
        /// <paramref name="desired"/> perturbed, capped at <paramref name="speed"/>. Pure, over a snapshot taken
        /// before the job, so the chunks split any way at all and the result is the same.
        /// </summary>
        public static Vector3 Avoid(Vector3 self, Vector3 desired, float speed, Entity me, Neighbour[] others, int count)
        {
            if (speed <= 0f || count <= 1) return desired;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                ref readonly var n = ref others[i];
                if (n.Entity == me) continue;
                Vector3 r = n.Position - self; r.y = 0f;
                float d = r.magnitude;
                if (d < 1e-3f || d > AvoidRadius) continue;
                Vector3 toward = r / d;
                Vector3 relative = desired - n.Desired; relative.y = 0f;
                bool closing = Vector3.Dot(relative, toward) > 0f || d < 1f;
                if (!closing) continue;
                float weight = 1f - d / AvoidRadius;                        // 0 at the edge, 1 on top of each other
                Vector3 side = new Vector3(-toward.z, 0f, toward.x);        // antisymmetric in the pair: opposite sides for free
                push += (-toward * 0.5f + side * 0.5f) * weight * speed;
            }
            if (push == Vector3.zero) return desired;
            var v = desired + push;
            if (v.sqrMagnitude > speed * speed) v = v.normalized * speed;
            return v;
        }

        /// <summary>The ledge guard: is there level ground a step ahead in <paramref name="direction"/> (flattened), within <paramref name="reach"/> below the body centre?</summary>
        public static bool GroundAhead(Vector3 self, Vector3 direction, float reach)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return true;
            return PhysicsApi.RaycastConcurrent(self + direction.normalized * LookAhead, Vector3.down, reach, Noises.WallMask); // worker-safe: SteeringSystem is a chunk job
        }
    }

    /// <summary>
    /// Moves every enemy body on its brain's intent: the motor gets the wanted velocity, the heading turns, and the
    /// sheet shows walking or standing (never over an attack or a death the visual system is playing). A dead
    /// enemy's body is braked. A chunk job: each row reads its own components (and the shared definitions) and
    /// writes its own; the ledge guard is the worker-safe raycast. Still a batch of its own in the schedule —
    /// it reads what the brain just wrote.
    /// </summary>
    [UpdateAfter(typeof(AiBrainSystem))]
    [Reads(typeof(Dead), typeof(EnemyWeapon), typeof(PathFollow))]
    public sealed class SteeringSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<CharacterMotor>().With<Heading>().With<EnemyVisual>()
                .WithReadOnly<AiIntent>().WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().Build();

        private Steering.Neighbour[] _snapshot = new Steering.Neighbour[16];
        private int _count;

        /// <summary>Everyone's position and last wanted velocity, taken serially before the job: what the chunks read and never write.</summary>
        private void Snapshot()
        {
            _count = 0;
            foreach (var chunk in _q)
            {
                var motors = chunk.Get<CharacterMotor>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || World.Has<Dead>(ents[i])) continue;
                    if (_count == _snapshot.Length) System.Array.Resize(ref _snapshot, _snapshot.Length * 2);
                    _snapshot[_count++] = new Steering.Neighbour { Position = ltw[i].Position, Desired = motors[i].Desired, Entity = ents[i] };
                }
            }
        }

        private struct SteerJob : IChunkJob
        {
            public World World; public float Dt; public Steering.Neighbour[] Others; public int Count;
            public void Execute(in Chunk chunk, int worker)
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
                    float reach = 0.5f * Enemies.Height + m.RideHeight + Steering.Drop; // centre → feet → a drop below
                    // the ledge guard is for a body walking blind. On a path it stands down: every link the search
                    // used is inside the body's step and drop rules, so the ground ahead is known to be there.
                    bool onPath = World.Has<PathFollow>(ents[i]) && World.Get<PathFollow>(ents[i]).Count > 0;
                    bool ground = onPath || !intents[i].Move || Steering.GroundAhead(self, intents[i].MoveTo - self, reach);
                    Steering.Step(ref m, ref headings[i], in intents[i], def, self, ground, Dt);
                    if (m.Desired.sqrMagnitude > 0f && Count > 1)
                        m.Desired = Steering.Avoid(self, m.Desired, def.movementSpeed, ents[i], Others, Count);

                    ref var v = ref visuals[i];
                    if (v.State == AnimationState.IDLE || v.State == AnimationState.MOVING)
                        v.State = m.Desired.sqrMagnitude > 0f ? AnimationState.MOVING : AnimationState.IDLE;
                }
            }
        }

        public void Tick(SystemContext ctx)
        {
            Snapshot();
            var job = new SteerJob { World = World, Dt = ctx.DeltaTime, Others = _snapshot, Count = _count };
            _q.ScheduleParallel(ref job, 32);
        }
    }
}
