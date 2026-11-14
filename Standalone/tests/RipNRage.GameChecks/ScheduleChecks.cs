using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine.Ecs;

/// <summary>
/// The system schedule, pinned. The scheduler orders systems by [UpdateAfter]/[UpdateBefore], breaks ties by type
/// name, and packs non-conflicting systems into parallel batches — where a system that declares no [Reads]/[Writes]
/// conflicts with everything. Command buffers play back after each batch, so <b>which batch a system lands in decides
/// what it sees of the frame so far</b>. None of that is visible in a diff of the system you just added, so this check
/// makes it visible: any addition, rename or attribute change moves the layout and fails here, with both layouts
/// printed. If the change is intended, update <see cref="Expected"/> in the same commit and say so in the roadmap.
/// </summary>
internal static class ScheduleChecks
{
    // one line per batch; systems in one batch run in parallel (today every batch is one system: nothing declares
    // Reads/Writes except the few marked, and those never meet an independent neighbour)
    private static readonly Dictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["Tick"] = new[]
        {
            "AnimationSystem", "RagdollSystem", "LifetimeSystem", "EcsInputSystem", "EnemyVisualSystem",
            "HearingSystem", "SightSystem", "AiBrainSystem", "SteeringSystem", "EnemyAttackSystem", "SpawnerSystem",
            "AltFireSystem", "BashFinisherSystem", "GrappleFinisherSystem", "PointBlankFinisherSystem",
            "WeaponFireSystem", "PlayerLookSystem", "SpinSystem",
        },
        ["FixedTick"] = new[]
        {
            "PhysicsSyncSystem", "RagdollSystem", "JointSystem", "KinematicMoverSystem", "CameraShakeSystem",
            "PlayerMoveSystem", "CharacterMotorSystem", "PhysicsStepSystem", "PhysicsWriteBackSystem", "TriggerSystem",
            "SliderPatrolSystem",
        },
        ["LateTick"] = new[]
        {
            "AudioSystem", "EffectSystem", "PhysicsInterpolationSystem", "PlayerViewSystem", "CameraShakeSystem",
            "PlayerViewFollowSystem", "WeaponSwaySystem", "TransformSystem",
        },
    };

    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        foreach (var phase in Expected.Keys)
        {
            var actual = w.DescribeBatches(phase).Select(b => string.Join(" + ", b.Select(t => t.Name))).ToArray();
            var expected = Expected[phase];
            bool same = actual.SequenceEqual(expected);
            if (!same)
            {
                Console.Error.WriteLine($"--- {phase} schedule changed. expected:");
                foreach (var l in expected) Console.Error.WriteLine("    " + l);
                Console.Error.WriteLine("--- actual:");
                foreach (var l in actual) Console.Error.WriteLine("    " + l);
            }
            True(same, $"the {phase} schedule is the pinned one ({expected.Length} batches) — a system was added, renamed or re-attributed: if intended, update ScheduleChecks.Expected and note it in the roadmap");
        }
    }
}
