using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 8: game flow. 8.2 — the <see cref="Flow"/> singleton: the rules as pure functions (every phase, every
/// request, the death clock, the time scale), then <see cref="FlowSystem"/> over a world (the claim, the pause
/// round trip, whose death counts, the refusal while dying). The death clock runs on real time, which the harness
/// cannot advance (<c>Time.unscaledDeltaTime</c> is the engine's), so Dying → Dead is pinned on the pure step here
/// and end to end by the <c>die</c> gate.
/// </summary>
internal static class Layer8Checks
{
    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        Rules(True, Equal);
        System(True, Equal);
        Time.timeScale = 1f; // the system writes the engine static: leave it as the next checks expect
    }

    private static void Rules(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var f = default(Flow);
        True(f.Phase == FlowPhase.Playing && !f.CheckpointReached, "a fresh flow (the scene reset's default) is Playing, no checkpoint");
        Equal(1f, Flows.TimeScale(in f), "and runs at full speed");

        // pause / resume
        var p = Flows.Request(in f, true);
        True(p.Phase == FlowPhase.Paused, "a pause request from Playing pauses");
        Equal(0f, Flows.TimeScale(in p), "and freezes the world");
        True(Flows.Request(in p, true).Phase == FlowPhase.Paused, "a second pause request changes nothing");
        True(Flows.Request(in f, false).Phase == FlowPhase.Playing, "a resume request while playing changes nothing");
        var r = Flows.Request(in p, false);
        True(r.Phase == FlowPhase.Playing, "a resume request from Paused resumes");
        Equal(1f, Flows.TimeScale(in r), "at full speed again");

        // death
        var d = Flows.PlayerDied(in f);
        True(d.Phase == FlowPhase.Dying, "the player's death starts Dying");
        Equal(0f, d.DeathClock, "with the death clock at zero");
        True(Flows.PlayerDied(in p).Phase == FlowPhase.Dying, "a death while paused (a blow in flight) starts Dying too");
        True(Flows.Request(in d, true).Phase == FlowPhase.Dying, "a pause request while dying is refused");
        True(Flows.Request(in d, false).Phase == FlowPhase.Dying, "and so is a resume request");

        // the clock: real time, through Dying and Dead
        True(Flows.Step(in f, 1f).DeathClock == 0f, "the death clock does not run while playing");
        True(Flows.Step(in p, 1f).DeathClock == 0f, "nor while paused");
        var s = Flows.Step(in d, 1f);
        Equal(1f, s.DeathClock, "the death clock runs while dying");
        True(s.Phase == FlowPhase.Dying, "short of the delay the run is still dying");
        s = Flows.Step(in s, Flows.DeathMenuDelay - 1f);
        True(s.Phase == FlowPhase.Dead, "at the delay the run is Dead: the death screen's choice opens");
        Equal(Flows.DeathMenuDelay, s.DeathClock, "exactly at the delay");
        var dead = Flows.Step(in s, 0.5f);
        Equal(Flows.DeathMenuDelay + 0.5f, dead.DeathClock, "the clock keeps running while dead (the screen's fades read it)");
        True(dead.Phase == FlowPhase.Dead, "and Dead stays Dead");
        True(Flows.Request(in dead, true).Phase == FlowPhase.Dead, "a pause request while dead is refused");
        var again = Flows.PlayerDied(in dead);
        True(again.Phase == FlowPhase.Dead && again.DeathClock == dead.DeathClock, "a second death changes nothing, the clock included");

        // the death's slow motion
        var slow = d; slow.SlowMotion = 0.2f;
        Equal(0.2f, Flows.TimeScale(in slow), "the death's slow motion is the time scale");
        var frozen = p; frozen.SlowMotion = 0.2f;
        Equal(0f, Flows.TimeScale(in frozen), "a pause freezes over any slow motion");
        Equal(0.2f, Flows.TimeScale(Flows.Request(in frozen, false)), "and resuming gives the slow motion back, with nothing saved");
    }

    private static void System(Action<bool, string> True, Action<float, float, string> Equal)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        True(w.HasSingleton<Flow>() && w.GetSingleton<Flow>().Phase == FlowPhase.Playing, "the system claims a Playing flow when it is created");

        w.Post(new PauseRequest { Pause = true });
        w.Update(1f / 60f);
        True(w.GetSingleton<Flow>().Phase == FlowPhase.Paused, "a posted pause request pauses");
        Equal(0f, Time.timeScale, "and the system freezes the engine time scale in the same frame");
        w.Post(new PauseRequest { Pause = false });
        w.Update(0f);
        True(w.GetSingleton<Flow>().Phase == FlowPhase.Playing, "a posted resume request resumes");
        Equal(1f, Time.timeScale, "at full speed");

        Entity Mortal(bool player)
        {
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(Vector3.zero));
            w.Add(e, new Health { Current = 50f, Max = 50f });
            if (player) w.Add(e, new PlayerTag());
            return e;
        }

        var enemy = Mortal(false);
        w.Update(1f / 60f);
        w.Post(new DamageEvent { Target = enemy, Fatal = true });
        w.Update(1f / 60f);
        True(w.Has<Dead>(enemy) && w.GetSingleton<Flow>().Phase == FlowPhase.Playing, "an enemy's death does not touch the flow");

        var player = Mortal(true);
        w.Update(1f / 60f);
        w.Post(new DamageEvent { Target = player, Fatal = true });
        w.Update(1f / 60f);
        True(w.GetSingleton<Flow>().Phase == FlowPhase.Dying, "the player's DeathEvent starts Dying");
        w.Post(new PauseRequest { Pause = true });
        w.Update(1f / 60f);
        True(w.GetSingleton<Flow>().Phase == FlowPhase.Dying, "a pause request while dying is refused by the system");
        Equal(1f, Time.timeScale, "and the world is not frozen");

        // the scene reset puts the carrier back at its default: a new level starts Playing with no checkpoint
        ref var flow = ref w.Singleton<Flow>();
        flow.CheckpointReached = true;
        w.DestroyAllEntities();
        var fresh = w.GetSingleton<Flow>();
        True(fresh.Phase == FlowPhase.Playing && !fresh.CheckpointReached && fresh.DeathClock == 0f,
            "the scene reset gives a fresh flow: Playing, no checkpoint, no death clock");
    }
}
