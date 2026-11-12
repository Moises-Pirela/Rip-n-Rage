using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;

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

        // ---- a level restart must leave the claim standing. DestroyAllEntities kills the carrier with everything
        // else; a writer system claims its singleton in OnCreate, which does not run again, so a claim dropped here
        // was re-created from inside the next Tick — a structural change mid-phase, which threw ("--script saveload"
        // died on the reload after a checkpoint load). The carriers come back at once instead, values at default.
        w.Singleton<InputState>().Move = new Vector2(1f, 1f);
        w.DestroyAllEntities();
        True(w.HasSingleton<InputState>() && w.GetSingleton<InputState>().Move == Vector2.zero,
            "a claimed singleton survives DestroyAllEntities as a fresh carrier");
        w.Update(1f / 60f);
        True(w.HasSingleton<InputState>(), "and the frame after a restart runs without a structural change");

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

        ViewRig(w, Step, True);
    }

    /// <summary>Step 4.3a: the camera rig — the lens choice, the head bob and the kick / trauma shake.</summary>
    private static void ViewRig(World w, Action<int, float> Step, Action<bool, string> True)
    {
        // ---- the rig as the player builds it: body -> pivot (bob) -> camera (lens + shake)
        var body = w.CreateEntity();
        w.Add(body, LocalTransform.At(new Vector3(100, 2, 0)));

        var pivot = w.CreateEntity();
        w.Add(pivot, LocalTransform.At(new Vector3(0, 0.5f, 0)));
        w.Add(pivot, new Parent { Value = body });
        w.Add(pivot, new ViewBob { Frequency = 15f, WaveLength = 0.1f, BasePosition = new Vector3(0, 0.5f, 0) });

        var camera = w.CreateEntity();
        w.Add(camera, LocalTransform.Identity);
        w.Add(camera, new Parent { Value = pivot });
        w.Add(camera, new CameraLensConfig { fov = 77f, priority = 0 }.Bake());
        var shake = CameraShake.Default;
        shake.Seed = 3f; // fixed seed: the noise has to be reproducible here
        w.Add(camera, shake);
        Step(1, 1f / 60f);

        // the camera's world pose is the chain, so the renderer sees the player's eye point
        True(Mathf.Abs(w.Get<LocalToWorld>(camera).Position.y - 2.5f) < 1E-04f &&
             Mathf.Abs(w.Get<LocalToWorld>(camera).Position.x - 100f) < 1E-04f,
            "the camera entity's world pose is body + pivot + camera");

        // ---- the active camera is the highest-priority lens (what EcsRenderProducer renders from)
        var cameras = ActiveCamera.BuildQuery(w);
        True(ActiveCamera.TryGet(cameras, out var pose, out var lens) && lens.FieldOfView == 77f,
            "the active camera is found through its lens");
        var spare = w.CreateEntity();
        w.Add(spare, LocalTransform.At(new Vector3(-50, 9, 0)));
        w.Add(spare, new CameraLensConfig { fov = 30f, priority = 5 }.Bake());
        Step(1, 1f / 60f);
        True(ActiveCamera.TryGet(cameras, out pose, out lens) && lens.FieldOfView == 30f && pose.Position.x == -50f,
            "a higher-priority lens takes over");
        w.DestroyEntity(spare);
        Step(1, 1f / 60f);

        // ---- head bob: only on the ground, only while moving, around the rest height + the crouch offset
        float resting = w.Get<LocalTransform>(pivot).Position.y;
        w.Get<ViewBob>(pivot).Active = false;
        w.Get<ViewBob>(pivot).Moving = true;
        Step(10, 1f / 60f);
        True(w.Get<LocalTransform>(pivot).Position.y == resting, "the bob leaves the camera alone in mid-air");

        w.Get<ViewBob>(pivot).Active = true;
        Step(6, 1f / 60f);
        float bobbed = w.Get<LocalTransform>(pivot).Position.y;
        True(Mathf.Abs(bobbed - 0.5f) > 1E-03f && Mathf.Abs(bobbed - 0.5f) <= 0.1f + 1E-04f,
            $"walking on the ground bobs the camera within the wave length (y={bobbed:0.0000})");

        w.Get<ViewBob>(pivot).Moving = false;
        Step(120, 1f / 60f);
        True(Mathf.Abs(w.Get<LocalTransform>(pivot).Position.y - 0.5f) < 1E-03f, "standing still settles it back to rest");

        w.Get<ViewBob>(pivot).HeightOffset = -0.4f; // the slide's camera drop
        Step(120, 1f / 60f);
        True(Mathf.Abs(w.Get<LocalTransform>(pivot).Position.y - 0.1f) < 1E-03f, "a crouch offset lowers the rest height");
        w.Get<ViewBob>(pivot).HeightOffset = 0f;
        Step(120, 1f / 60f);

        // ---- the kick: taken up over a few steps, then decaying back to straight ahead
        w.Get<CameraShake>(camera).Kick(3f, 0f, 0f); // pitch only, so the result is deterministic
        Step(3, 1f / 60f);
        float pitch = w.Get<LocalTransform>(camera).Rotation.eulerAngles.x;
        pitch = pitch > 180f ? pitch - 360f : pitch;
        True(pitch < -0.05f, $"a weapon kick pitches the camera up (pitch={pitch:0.000})");
        Step(240, 1f / 60f);
        float settled = w.Get<LocalTransform>(camera).Rotation.eulerAngles.x;
        settled = settled > 180f ? settled - 360f : settled;
        True(Mathf.Abs(settled) < 0.05f, $"and it returns to centre ({settled:0.000})");

        // ---- trauma: shakes rotation and position, decays to nothing, and never exceeds its bounds
        w.Get<CameraShake>(camera).AddTrauma(1f);
        True(w.Get<CameraShake>(camera).Trauma == 1f, "trauma saturates at 1");
        bool moved = false;
        for (int i = 0; i < 20; i++)
        {
            Step(1, 1f / 60f);
            var lt = w.Get<LocalTransform>(camera);
            if (lt.Position.sqrMagnitude > 1E-06f) moved = true;
            True(Mathf.Abs(lt.Position.x) <= 0.1f + 1E-04f && Mathf.Abs(lt.Position.y) <= 0.1f + 1E-04f,
                "the shake offset stays inside maxShakeOffset");
        }
        True(moved, "full trauma shakes the camera position");
        Step(240, 1f / 60f);
        True(w.Get<CameraShake>(camera).Trauma == 0f && w.Get<LocalTransform>(camera).Position == Vector3.zero,
            "trauma decays away and the camera returns to its base position");

        // ---- a distant explosion is quieter than a close one, and one outside the radius does nothing at all
        ref var sh = ref w.Get<CameraShake>(camera);
        Vector3 eye = w.Get<LocalToWorld>(camera).Position;
        sh.ShakeAt(eye, eye + new Vector3(0, 0, 2f), 10f, 1f);
        float near = sh.Trauma;
        sh.Trauma = 0f;
        sh.ShakeAt(eye, eye + new Vector3(0, 0, 8f), 10f, 1f);
        float far = sh.Trauma;
        sh.Trauma = 0f;
        sh.ShakeAt(eye, eye + new Vector3(0, 0, 30f), 10f, 1f);
        True(near > far && far > 0f && sh.Trauma == 0f,
            $"explosion trauma falls off with distance (near={near:0.00}, far={far:0.00}, outside=0)");

        // ---- Clear takes the camera back for the death sequence
        sh.AddTrauma(0.8f);
        sh.Kick(5f, 0f, 0f);
        sh.Clear();
        Step(1, 1f / 60f);
        True(sh.Trauma == 0f && w.Get<LocalTransform>(camera).Rotation == Quaternion.identity,
            "Clear drops the kick and the trauma at once");

        // ---- 4.3b: the sprite handle the viewmodel and the animation editor use
        var vmSprite = new RipNRage.Engine.Ecs.Animation.AnimatedSprite(w, null, RipNRage.Engine.Graphics.RenderLayer.FirstPerson,
            parent: camera, localPosition: new Vector3(0f, -0.65f, 2.36f), localScale: Vector3.one * 0.1f);
        Step(1, 1f / 60f);
        True(w.IsAlive(vmSprite.Entity) && w.Has<RipNRage.Engine.Ecs.Rendering.SpriteRender>(vmSprite.Entity),
            "an AnimatedSprite owns a live sprite entity");
        True(Mathf.Abs(w.Get<LocalToWorld>(vmSprite.Entity).Position.y - (2.5f - 0.65f)) < 1E-03f,
            "parented to the camera, its world pose rides the view chain");
        vmSprite.SetActive(false);
        Step(1, 1f / 60f);
        True(!vmSprite.activeSelf, "SetActive(false) hides it (the jab hides the weapon)");
        vmSprite.SetActive(true);
        vmSprite.LocalPosition = new Vector3(0.1f, -0.5f, 2f); // Equip moves the weapon to its data
        Step(1, 1f / 60f);
        True(vmSprite.activeSelf && vmSprite.LocalPosition.x == 0.1f, "and the equip pose is a plain local write");
        // A handle built outside a system is usable at once — it is created immediately, not reserved in the command
        // buffer. This is the muzzle-flash bug: the viewmodel build hid the flash on the line after creating it, the
        // entity was not alive yet, the checked accessor dropped the call, and the flash stayed lit over the gun for
        // the whole game (which read as "the weapon is stuck on the firing frame").
        var flash = new RipNRage.Engine.Ecs.Animation.AnimatedSprite(w, null, RipNRage.Engine.Graphics.RenderLayer.FirstPerson,
            parent: vmSprite.Entity, localPosition: new Vector3(-0.5f, 4.5f, 0.05f));
        True(flash.Alive, "an AnimatedSprite is alive immediately after construction");
        flash.SetActive(false);
        True(!flash.activeSelf, "so hiding it on the next line takes effect (the muzzle flash starts hidden)");
        flash.LocalPosition = new Vector3(1f, 2f, 3f);
        True(flash.LocalPosition == new Vector3(1f, 2f, 3f), "and so does a pose written before the first sync point");
        Step(1, 1f / 60f);
        True(!flash.activeSelf, "and it is still hidden after the world syncs");

        vmSprite.Destroy();
        Step(1, 1f / 60f);
        True(!w.IsAlive(vmSprite.Entity) || vmSprite.Entity.IsNull, "Destroy removes the entity");
    }
}
