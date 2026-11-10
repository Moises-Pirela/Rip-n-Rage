using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>A component no system claims: proves reading an absent singleton creates nothing.</summary>
public struct UnclaimedSingleton : IComponent { public int Value; }

[DisableAutoCreation]
public sealed class Layer4TriggerLog : SystemBase, IConsumes<TriggerEnterEvent>, IConsumes<TriggerExitEvent>
{
    public readonly List<(Entity trigger, Entity other)> Enters = new List<(Entity, Entity)>();
    public readonly List<(Entity trigger, Entity other)> Exits = new List<(Entity, Entity)>();
    public void Consume(SystemContext ctx, ReadOnlySpan<TriggerEnterEvent> e) { foreach (var x in e) Enters.Add((x.Trigger, x.Other)); }
    public void Consume(SystemContext ctx, ReadOnlySpan<TriggerExitEvent> e) { foreach (var x in e) Exits.Add((x.Trigger, x.Other)); }
}

/// <summary>Layer 4: the input singleton, and the pose / teleport contract gameplay depends on.</summary>
internal static class Layer4Checks
{
    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        var log = w.AddSystem(new Layer4TriggerLog());
        void Step(int n, float dt = 1f / 60f) { for (int i = 0; i < n; i++) w.Update(dt); }

        // ---- singletons: one carrier entity, reachable by reference. EcsInputSystem claims InputState in its
        // OnCreate, so it is already there; a type nobody claims stays absent and reads as default.
        True(w.HasSingleton<InputState>(), "the input system claims its singleton when it is created");
        True(!w.HasSingleton<UnclaimedSingleton>() && w.GetSingleton<UnclaimedSingleton>().Value == 0,
            "an unclaimed singleton reads as default without being created");
        w.Singleton<InputState>().Move = new Vector2(0.25f, -0.5f);
        True(w.HasSingleton<InputState>() && w.GetSingleton<InputState>().Move == new Vector2(0.25f, -0.5f),
            "Singleton<T>() returns a reference: writes through it are visible to the next read");
        True(w.SingletonEntity<InputState>() == w.SingletonEntity<InputState>(), "the carrier entity is stable");

        // ---- the jump latch survives frames that run no fixed step (rendering outruns the fixed clock)
        w.Singleton<InputState>().JumpQueued = true;
        True(w.Singleton<InputState>().ConsumeJump() && !w.Singleton<InputState>().JumpQueued,
            "a queued jump is taken exactly once");
        True(!w.Singleton<InputState>().ConsumeJump(), "and is not taken twice");

        // ---- the floor everything below stands on
        var floor = w.CreateEntity();
        w.Add(floor, LocalTransform.At(new Vector3(0, -0.5f, 0)));
        w.Add(floor, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(60, 1, 60), IsStatic = true });

        // ---- an interpolated body publishes its real pose every step.
        // Characters and the player interpolate; before layer 4 the solver only wrote the owner pose for
        // non-interpolated bodies, so LocalTransform read between fixed steps was stuck at the spawn point and
        // gameplay (the fall safety net, the enemies' target lookups) saw a player that never moved.
        var ball = w.CreateEntity();
        w.Add(ball, LocalTransform.At(new Vector3(0, 6, 0)));
        w.Add(ball, new ColliderShape { Kind = ShapeKind.Sphere, Radius = 0.5f });
        w.Add(ball, new BodyConfig { mass = 10f, interpolate = true }.Bake());
        w.Add(ball, new BodyVelocity());
        Step(20);
        float y = w.Get<LocalTransform>(ball).Position.y;
        True(y < 5.6f, $"an interpolated body's LocalTransform follows the simulation (y={y:0.000} after 20 steps)");

        // ---- a teleport written between fixed steps is not swallowed by the interpolation blend
        w.Get<LocalTransform>(ball).Position = new Vector3(12, 4, 0);
        Step(1);
        Vector3 after = w.Get<LocalTransform>(ball).Position;
        True(MathF.Abs(after.x - 12f) < 0.05f, $"a teleport of an interpolated body sticks (x={after.x:0.00}, expected 12)");

        // ---- a character teleported mid-fall lands where it was put, not where it was
        var ch = w.CreateEntity();
        w.Add(ch, LocalTransform.At(new Vector3(-20, 3, 0)));
        w.Add(ch, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
        w.Add(ch, new BodyConfig { mass = 80f, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
        w.Add(ch, new BodyVelocity());
        w.Add(ch, new CharacterMotorConfig { rideHeight = 0.5f }.Bake());
        Step(60);
        w.Get<LocalTransform>(ch).Position = new Vector3(20, 3, 0);
        w.Get<BodyVelocity>(ch).Linear = Vector3.zero;
        Step(60);
        Vector3 p = w.Get<LocalTransform>(ch).Position;
        True(MathF.Abs(p.x - 20f) < 0.2f && w.Get<CharacterMotor>(ch).IsGrounded,
            $"a respawned character stands where it was put ({p.x:0.00}, grounded={w.Get<CharacterMotor>(ch).IsGrounded})");

        // ---- gravity scale is a level the motor re-applies, so a wall-run glide can be driven live
        w.Get<CharacterMotor>(ch).PendingJump = 8f;
        Step(4);
        w.Get<CharacterMotor>(ch).GravityScale = 0f;
        Step(10);
        float vyGlide = w.Get<BodyVelocity>(ch).Linear.y;
        Step(1);
        True(MathF.Abs(w.Get<BodyVelocity>(ch).Linear.y - vyGlide) < 0.05f,
            $"GravityScale 0 holds the vertical speed (vy={vyGlide:0.00})");
        w.Get<CharacterMotor>(ch).GravityScale = 1f;
        Step(10);
        True(w.Get<BodyVelocity>(ch).Linear.y < vyGlide - 0.5f, "and restoring it makes the character fall again");

        // ---- a trigger volume that moves keeps sensing: the ammo pickups bob up and down
        var walker = w.CreateEntity();
        w.Add(walker, LocalTransform.At(new Vector3(40, 1, 0)));
        w.Add(walker, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
        w.Add(walker, new BodyConfig { mass = 80f, freezeRotation = true, canSleep = false }.Bake());
        w.Add(walker, new BodyVelocity());
        var volume = w.CreateEntity();
        w.Add(volume, LocalTransform.At(new Vector3(46, 1, 0)));
        w.Add(volume, new ColliderShape { Kind = ShapeKind.Sphere, Radius = 1.5f, IsStatic = true });
        w.Add(volume, new Trigger());
        Step(5);
        True(log.Enters.Count == 0, "a trigger six metres away senses nothing");
        // move the volume onto the walker (a body-less collider re-bakes when its LocalTransform is written)
        w.Get<LocalTransform>(volume).Position = new Vector3(40, 1, 0);
        Step(3);
        True(log.Enters.Contains((volume, walker)), "a trigger moved onto a body reports the overlap");
        w.Get<LocalTransform>(volume).Position = new Vector3(46, 1, 0);
        Step(3);
        True(log.Exits.Contains((volume, walker)), "and reports the exit when it is moved away again");
    }
}
