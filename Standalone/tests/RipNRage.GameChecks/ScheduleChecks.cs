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
            // NavCarveSystem (11.5) is exclusive - it writes the grid, not components - so it is a batch of its own;
            // it only has to be ahead of NavPathSystem, which asks for it by [UpdateAfter]
            "AnimationSystem + LifetimeSystem", "RagdollSystem", "EcsInputSystem", "WorldspawnSystem", "NavCarveSystem",
            "EnemyVisualSystem + HearingSystem",
            "SightSystem", "AiBrainSystem", "NavPathSystem", "PathFollowSystem", "SteeringSystem", "EnemyAttackSystem", "SpawnerSystem", "BobSystem",
            "AltFireSystem", "BashFinisherSystem", "GrappleFinisherSystem", "PointBlankFinisherSystem",
            "WeaponFireSystem", "FallSystem", "FlowSystem", "DeathCamSystem", "MenuSwingSystem",
            "MuzzleFlashSystem + NearMissSystem + PlayerLookSystem + RageCalmSystem", "RageSystem", "RageFxSystem", "SpinSystem", "WeaponSelectSystem",
        },
        ["FixedTick"] = new[]
        {
            // NavBakeSystem (11.1) is a batch of its own, after PhysicsSyncSystem: it reads the broadphase the
            // sync system has just filled, and writes engine-side shared data rather than components
            //
            // The two movers come first, before the sync: a deck's velocity and the pose of everything riding it
            // are this step's inputs, so they must be written before the sync pushes them at the solver.
            "PlatformMotionSystem", "CarrySystem",
            // buoyancy and the engines add forces onto the bodies before the solver steps them
            "PhysicsSyncSystem", "RagdollSystem", "BuoyancySystem", "JointSystem", "KinematicMoverSystem",
            "VesselEngineSystem", "CameraShakeSystem",
            "NavBakeSystem", "PlayerMoveSystem", "CharacterMotorSystem", "PhysicsStepSystem", "PhysicsWriteBackSystem", "TriggerSystem",
            "SliderPatrolSystem",
        },
        ["LateTick"] = new[]
        {
            "AudioSystem", "EffectSystem", "PhysicsInterpolationSystem", "RodSystem", "PlayerViewSystem", "CameraShakeSystem",
            // the ocean is re-centred on the eye after the transforms have placed it, and before the underwater
            // test reads where the eye is
            "WeaponSwaySystem", "TransformSystem", "OceanSurfaceSystem", "UnderwaterSystem",
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
