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
    // one line per batch; "A + B" run in parallel: their declarations (query-derived + [Reads]/[Writes]) are disjoint
    // and neither feeds the other within the frame. An [Exclusive] system (physics, audio, input, VFX, GameObject
    // drivers) is always a batch of its own.
    private static readonly Dictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["Tick"] = new[]
        {
            "AnimationSystem + LifetimeSystem", "RagdollSystem", "EcsInputSystem", "EnemyVisualSystem + HearingSystem",
            "SightSystem", "AiBrainSystem", "SteeringSystem", "EnemyAttackSystem", "SpawnerSystem", "BobSystem",
            "AltFireSystem", "BashFinisherSystem", "GrappleFinisherSystem", "PointBlankFinisherSystem",
            "WeaponFireSystem", "FallSystem", "FlowSystem", "DeathCamSystem", "MenuSwingSystem",
            "MuzzleFlashSystem + NearMissSystem + PlayerLookSystem + RageCalmSystem", "RageSystem", "RageFxSystem", "SkySpinSystem", "SpinSystem",
        },
        ["FixedTick"] = new[]
        {
            "PhysicsSyncSystem", "RagdollSystem", "JointSystem", "KinematicMoverSystem", "CameraShakeSystem",
            "PlayerMoveSystem", "CharacterMotorSystem", "PhysicsStepSystem", "PhysicsWriteBackSystem", "TriggerSystem",
            "SliderPatrolSystem",
        },
        ["LateTick"] = new[]
        {
            "AudioSystem", "EffectSystem", "PhysicsInterpolationSystem", "RodSystem", "PlayerViewSystem", "CameraShakeSystem",
            "PlayerViewFollowSystem", "WeaponSwaySystem", "TransformSystem", "UnderwaterSystem",
        },
    };

    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        var same = new Dictionary<string, bool>();
        foreach (var phase in Expected.Keys) // every phase is printed before the first assertion fails, so one run shows the whole new layout
        {
            var actual = w.DescribeBatches(phase).Select(b => string.Join(" + ", b.Select(t => t.Name))).ToArray();
            var expected = Expected[phase];
            same[phase] = actual.SequenceEqual(expected);
            if (Environment.GetEnvironmentVariable("ECS_SCHEDULE_PRINT") == "1") // read the new layout off a passing run
            {
                Console.Error.WriteLine($"--- {phase} actual:");
                foreach (var l in actual) Console.Error.WriteLine("    " + l);
            }
            if (same[phase]) continue;
            Console.Error.WriteLine($"--- {phase} schedule changed. expected:");
            foreach (var l in expected) Console.Error.WriteLine("    " + l);
            Console.Error.WriteLine($"--- {phase} actual:");
            foreach (var l in actual) Console.Error.WriteLine("    " + l);
        }
        foreach (var phase in Expected.Keys)
            True(same[phase], $"the {phase} schedule is the pinned one ({Expected[phase].Length} batches) — a system was added, renamed or re-attributed: if intended, update ScheduleChecks.Expected and note it in the roadmap");
    }
}
