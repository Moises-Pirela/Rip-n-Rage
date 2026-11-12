using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>Which enemy this is: a slot into <see cref="Enemies.All"/>, where the definition lives.</summary>
    public struct Enemy : IComponent
    {
        public int Kind;
    }

    /// <summary>
    /// The leaves of the state tree. Unaware: <see cref="Idle"/> (dwell) → <see cref="Wander"/> (pick a point, go).
    /// Engaged: <see cref="Chase"/> (out of range) / <see cref="Attack"/> (in range). Which half the enemy is in is
    /// decided every tick from whether it has a live <see cref="AiBrain.Target"/> — the parent branch gets the first
    /// word, which is what makes this a tree rather than a flat machine.
    /// </summary>
    public enum AiState : byte { Idle, Wander, Chase, Attack }

    /// <summary>
    /// The enemy's mind: one leaf, how long it has been there, who it is after and where it is going. Replaces the
    /// behaviour tree (a cloned node graph + blackboard per enemy) with a value the system steps.
    /// </summary>
    public struct AiBrain : IComponent
    {
        public AiState State;
        public float StateTime;
        /// <summary>The entity being hunted (<see cref="Entity.Null"/> = unaware). Set by the vision sensor.</summary>
        public Entity Target;
        public Vector3 MoveTo;
    }

    /// <summary>
    /// What the brain wants this tick — rewritten whole every tick, so a quiet brain reads as all-false. The body
    /// side (today <c>AIAgent</c>, in 6.4 / 6.5 the steering and attack systems) acts on it; the brain never calls anyone.
    /// </summary>
    public struct AiIntent : IComponent
    {
        public bool Move, Attack;
        public Vector3 MoveTo;
        /// <summary>Where to aim (the attack's target point).</summary>
        public Vector3 FacePoint;
    }

    /// <summary>The state tree as pure functions over the brain: data in, the next state and intent out.</summary>
    public static class AiBrains
    {
        /// <summary>
        /// One tick. Interrupts first, outermost branch down: a lost or dead target drops the enemy back to
        /// <see cref="AiState.Idle"/>, a live one puts it in <see cref="AiState.Chase"/> or <see cref="AiState.Attack"/>
        /// by distance; then the current leaf runs. <paramref name="targetPos"/> is read only when
        /// <paramref name="targetAlive"/>.
        /// </summary>
        public static void Step(ref AiBrain b, ref AiIntent intent, EnemyData def, Vector3 self, bool targetAlive, Vector3 targetPos, float dt)
        {
            intent = default;
            b.StateTime += dt;

            // the branch conditions (the tree's selectors), parent before child
            if (!targetAlive)
            {
                b.Target = Entity.Null;
                if (b.State == AiState.Chase || b.State == AiState.Attack) Enter(ref b, AiState.Idle);
            }
            else
            {
                b.MoveTo = targetPos; // the spotted node wrote the target's position into the blackboard every tick
                var want = Vector3.Distance(self, targetPos) <= def.attackRange ? AiState.Attack : AiState.Chase;
                if (b.State != want) Enter(ref b, want);
            }

            // the leaf
            switch (b.State)
            {
                case AiState.Idle:
                    if (b.StateTime > def.wanderWait) Enter(ref b, AiState.Wander);
                    break;
                case AiState.Wander:
                    // a point too close is rejected and the pick retried next tick, as the random-location node did
                    var point = self + Random.insideUnitSphere * def.wanderRadius;
                    if (Vector3.Distance(point, self) <= def.wanderMinDistance) break;
                    b.MoveTo = point;
                    intent.Move = true; intent.MoveTo = point;
                    Enter(ref b, AiState.Idle); // arrival is instant until 6.4 steers the body
                    break;
                case AiState.Chase:
                    intent.Move = true; intent.MoveTo = b.MoveTo;
                    break;
                case AiState.Attack:
                    intent.Attack = true; intent.FacePoint = b.MoveTo;
                    break;
            }
        }

        private static void Enter(ref AiBrain b, AiState state)
        {
            b.State = state;
            b.StateTime = 0f;
        }
    }

    /// <summary>Steps every live enemy's brain. A dead enemy's brain stops and its intent reads quiet.</summary>
    [Reads(typeof(Enemy), typeof(LocalToWorld), typeof(Dead)), Writes(typeof(AiBrain), typeof(AiIntent))]
    public sealed class AiBrainSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<AiBrain>().With<AiIntent>().WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().Build();

        public void Tick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            foreach (var chunk in _q)
            {
                var brains = chunk.Get<AiBrain>(); var intents = chunk.Get<AiIntent>();
                var kinds = chunk.Get<Enemy>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    if (World.Has<Dead>(ents[i])) { intents[i] = default; continue; }
                    ref var b = ref brains[i];
                    bool alive = World.IsAlive(b.Target) && !World.Has<Dead>(b.Target) && World.Has<LocalToWorld>(b.Target);
                    var targetPos = alive ? World.Get<LocalToWorld>(b.Target).Position : default;
                    AiBrains.Step(ref b, ref intents[i], Enemies.All[kinds[i].Kind], ltw[i].Position, alive, targetPos, dt);
                }
            }
        }
    }
}
