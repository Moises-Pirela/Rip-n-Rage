using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 8: game flow. 8.2 / 8.3 — the <see cref="Flow"/> singleton and the death: the rules as pure functions (every phase, every
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
        FallRules(True, Equal);
        FallSystemOverAWorld(True, Equal);
        Time.timeScale = 1f; // the system writes the engine statics: leave them as the next checks expect
        RipNRage.Engine.Ecs.Input.InputOverride.Blocked = false;
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

        // the death's slow motion and the input block: functions of the phase and the clock (8.3)
        Equal(1f, Flows.TimeScale(in d), "at the death the world still runs at full speed");
        var half = Flows.Step(in d, DeathCams.FallDuration * 0.5f);
        Equal(Mathf.Lerp(1f, DeathCams.SlowMotion, 0.5f), Flows.TimeScale(in half), "half way down the fall the world is half way to the slow motion");
        Equal(DeathCams.SlowMotion, Flows.TimeScale(Flows.Step(in d, DeathCams.FallDuration)), "at the floor the world runs at the slow motion");
        Equal(DeathCams.SlowMotion, Flows.TimeScale(in dead), "and stays there while dead");
        True(!Flows.InputBlocked(in f), "the player has the controls while playing");
        True(Flows.InputBlocked(in p) && Flows.InputBlocked(in d) && Flows.InputBlocked(in dead), "and not while paused, dying or dead");

        // the camera's fall (the old coroutine's curve, as a function of progress)
        var cam = DeathCams.From(new Vector3(0f, 0.7f, 0f), Quaternion.Euler(10f, 90f, 0f), 1f);
        Equal(DeathCams.FloorHeight, cam.EndPosition.y, "the fall ends at floor height");
        Equal(24f, cam.EndRotation.eulerAngles.x, "pitched forward by the extra pitch (10 + 14)");
        Equal(DeathCams.RollAngle, cam.EndRotation.eulerAngles.z, "and rolled to the chosen side");
        True(Quaternion.Angle(DeathCams.From(Vector3.zero, Quaternion.identity, -1f).EndRotation, Quaternion.Euler(14f, 0f, -DeathCams.RollAngle)) < 0.01f,
            "the other side rolls the other way");
        DeathCams.Pose(in cam, 0f, out var p0, out var r0);
        True((p0 - cam.StartPosition).sqrMagnitude < 1e-8f && Quaternion.Angle(r0, cam.StartRotation) < 0.01f, "progress 0 is the pose at the death");
        DeathCams.Pose(in cam, 1f, out var p1, out var r1);
        True((p1 - cam.EndPosition).sqrMagnitude < 1e-8f && Quaternion.Angle(r1, cam.EndRotation) < 0.01f, "progress 1 is the floor, the thud over");
        DeathCams.Pose(in cam, 0.5f, out var ph, out _);
        Equal(Mathf.Lerp(0.7f, DeathCams.FloorHeight, 0.25f), ph.y, "the drop accelerates like gravity (a quarter of the way at half time)");
        DeathCams.Pose(in cam, 0.925f, out var pt, out _);
        True(pt.y > Mathf.Lerp(0.7f, DeathCams.FloorHeight, 0.925f * 0.925f), "the thud bounces the camera near the end");
        Equal(0f, DeathCams.Progress(-1f), "progress is clamped below");
        Equal(1f, DeathCams.Progress(5f), "and above");
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

        // 8.3: the player's death everywhere outside the flow — rig, gun, combo, viewmodel, the camera's fall
        Time.timeScale = 1f;
        var w2 = w; // same world: a fresh flow first
        w2.DestroyAllEntities();
        var pivot = w2.CreateEntity(); w2.Add(pivot, LocalTransform.At(new Vector3(0f, 0.7f, 0f)));
        w2.Add(pivot, new RipNRage.Engine.Ecs.View.ViewBob { Active = true });
        var camera = w2.CreateEntity(); w2.Add(camera, LocalTransform.At(Vector3.zero)); w2.Add(camera, new RipNRage.Engine.Ecs.View.CameraShake());
        var gunSprite = w2.CreateEntity(); var fistL = w2.CreateEntity(); var fistR = w2.CreateEntity(); var finisher = w2.CreateEntity();
        var hero = Mortal(true);
        w2.Add(hero, new PlayerViewRig { Pivot = pivot, Camera = camera });
        w2.Add(hero, new Weapon { Sprite = gunSprite });
        w2.Add(hero, new AltFire { LeftFist = fistL, RightFist = fistR, Finisher = finisher, Step = 2 });
        w2.Update(1f / 60f);
        int deathsBefore = PlayerDeathSystem.Deaths;
        w2.Post(new DamageEvent { Target = hero, Fatal = true });
        w2.Update(1f / 60f);
        True(PlayerDeathSystem.Deaths == deathsBefore + 1, "the player's death is counted once");
        True(w2.Get<PlayerViewRig>(hero).External && !w2.Get<RipNRage.Engine.Ecs.View.ViewBob>(pivot).Active, "the view rig is taken away: no look, no bob");
        True(w2.Get<Weapon>(hero).Holstered, "the gun is holstered");
        True(w2.Has<RipNRage.Engine.Ecs.Rendering.Hidden>(gunSprite) && w2.Has<RipNRage.Engine.Ecs.Rendering.Hidden>(fistL)
             && w2.Has<RipNRage.Engine.Ecs.Rendering.Hidden>(fistR), "the viewmodel is hidden");
        var combo = w2.Get<AltFire>(hero);
        True(!w2.IsAlive(finisher) && combo.Step == 0 && combo.Finisher.IsNull && combo.LeftFist == fistL,
            "the finisher is ended and the combo reset, the fists kept");
        True(w2.Has<DeathCam>(pivot), "the pivot gets its death camera");
        Equal(DeathCams.FloorHeight, w2.Get<DeathCam>(pivot).EndPosition.y, "falling from where the pivot was to the floor");
        True(RipNRage.Engine.Ecs.Input.InputOverride.Blocked, "the flow blocks the input for the dying player");
        w2.Singleton<Flow>().DeathClock = DeathCams.FallDuration; // the harness cannot run real time: set the clock
        w2.Update(1f / 60f);
        Equal(DeathCams.FloorHeight, w2.Get<LocalTransform>(pivot).Position.y, "the system poses the pivot off the flow's clock");
        Equal(DeathCams.SlowMotion, Time.timeScale, "and the world is at the slow motion");
        w2.Post(new DamageEvent { Target = hero, Fatal = true });
        w2.Update(1f / 60f);
        True(PlayerDeathSystem.Deaths == deathsBefore + 1, "a dead player does not die twice");

        // the scene reset puts the carrier back at its default: a new level starts Playing with no checkpoint
        ref var flow = ref w.Singleton<Flow>();
        flow.CheckpointReached = true;
        w.DestroyAllEntities();
        var fresh = w.GetSingleton<Flow>();
        True(fresh.Phase == FlowPhase.Playing && !fresh.CheckpointReached && fresh.DeathClock == 0f,
            "the scene reset gives a fresh flow: Playing, no checkpoint, no death clock");
    }

    // ---------------------------------------------------------------- 8.4: falls

    private static void FallRules(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var f = Falls.Track(default, new Vector3(1f, 2f, 3f), true, 0f);
        True(f.LastGrounded == new Vector3(1f, 2f, 3f) && !f.DropRecorded, "standing records where the player stood");
        var air = Falls.Track(in f, new Vector3(1f, 1.7f, 3f), false, 1f);
        True(!air.DropRecorded, "a hop or a step down is not a drop (within the margin)");
        var drop = Falls.Track(in air, new Vector3(4f, 1.4f, 3f), false, 2f);
        True(drop.DropRecorded && drop.DropPosition == new Vector3(4f, 1.4f, 3f), "half a metre under the last footing is the drop spot");
        Equal(2f, drop.DropTime, "timed");
        var later = Falls.Track(in drop, new Vector3(9f, -5f, 3f), false, 3f);
        True(later.DropPosition == drop.DropPosition, "the first drop spot is kept while the fall goes on");
        True(!Falls.Track(in later, new Vector3(9f, 0f, 3f), true, 4f).DropRecorded, "landing clears it");
        True(Falls.Label(FallKind.Edge) == "EDGE" && Falls.Label(FallKind.None) == "-", "the overlay's labels");
    }

    private static void FallSystemOverAWorld(Action<bool, string> True, Action<float, float, string> Equal)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        w.Singleton<KillHeight>().Y = -1.5f;
        var p = w.CreateEntity();
        w.Add(p, LocalTransform.At(new Vector3(0f, 1f, 0f)));
        w.Add(p, new RipNRage.Engine.Ecs.Physics.CharacterMotor());
        w.Add(p, new PlayerTag());
        w.Add(p, new FallWatch { LastGrounded = new Vector3(0f, -1.8f, 0f) }); // as if spawned just above the line
        w.Add(p, new Health { Current = 100f, Max = 100f });
        w.Add(p, new Armor { Current = 100f, Max = 100f, Resistance = 1f });
        w.Update(1f / 60f);
        True(!w.Has<Dead>(p) && w.Get<FallWatch>(p).Count == 0, "above the kill height nothing happens");

        int deaths = PlayerDeathSystem.Deaths;
        w.Get<LocalTransform>(p).Position = new Vector3(0f, -2f, 0f);
        w.Update(1f / 60f);
        var fw = w.Get<FallWatch>(p);
        True(fw.Count == 1 && fw.LastKind == FallKind.Unknown, "below it the fall is counted and classified (never stood: unknown)");
        True(w.Has<Dead>(p) && w.Get<Health>(p).Current == 0f && PlayerDeathSystem.Deaths == deaths + 1,
            "and the player dies by the one door, through full armour");
        w.Update(1f / 60f);
        True(w.Get<FallWatch>(p).Count == 1, "a dead player is not watched: one fall, one death");
        Time.timeScale = 1f;
        RipNRage.Engine.Ecs.Input.InputOverride.Blocked = false;
    }
}
