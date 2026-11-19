using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>
/// Floating, against Archimedes rather than against a number somebody liked the look of: a body settles where the
/// water it displaces weighs what the body weighs. Every check here is that law, read off the simulation.
/// </summary>
internal static class BuoyancyChecks
{
    private const float Dt = 1f / 60f;

    public static void Run(Action<bool, string> True)
    {
        try
        {
            WaterSurface.HeightOf = (x, z) => 0f; // a flat sea at y = 0
            Draft(True);
            Righting(True);
            Engines(True);
            Boarding(True);
        }
        finally { WaterSurface.Clear(); }
    }

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;
        return w;
    }

    /// <summary>A cube floats at the fraction of its depth that its density is of the water's.</summary>
    private static void Draft(Action<bool, string> True)
    {
        using var w = NewWorld();
        // a 2 m cube, half the density of the sea: it should sit with half its height under
        const float side = 2f, half = side * 0.5f;
        float volume = side * side * side;
        float mass = volume * WaterSurface.Density * 0.5f;

        var cube = w.CreateEntity();
        w.Add(cube, LocalTransform.At(new Vector3(0, 6, 0)));
        w.Add(cube, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(side, side, side) });
        w.Add(cube, new BodyConfig { mass = mass }.Bake());
        w.Add(cube, new BodyVelocity());
        w.Add(cube, new BuoyancyConfig { resolution = 3, linearDrag = 20f, quadraticDrag = 0.6f, angularDrag = 2f }.Bake());
        w.Sync();

        for (int i = 0; i < 1200; i++) w.Update(Dt);

        float y = w.Get<LocalTransform>(cube).Position.y;
        // half the density, half the cube under water: its centre rests on the surface
        True(Mathf.Abs(y) < 0.12f, $"a cube of half the sea's density floats half under (centre at y={y:0.000}, wanted 0)");
        True(w.Get<BodyVelocity>(cube).Linear.magnitude < 0.2f, "and it comes to rest instead of bobbing for ever");
        True(Mathf.Abs(w.Get<Buoyancy>(cube).Submerged - 0.5f) < 0.2f,
             $"it reads itself as half submerged ({w.Get<Buoyancy>(cube).Submerged:0.00})");

        // a denser-than-water body sinks; a much lighter one rides high
        var stone = w.CreateEntity();
        w.Add(stone, LocalTransform.At(new Vector3(20, 2, 0)));
        w.Add(stone, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(1, 1, 1) });
        w.Add(stone, new BodyConfig { mass = 1f * WaterSurface.Density * 3f }.Bake());
        w.Add(stone, new BodyVelocity());
        w.Add(stone, new BuoyancyConfig { resolution = 2 }.Bake());

        var cork = w.CreateEntity();
        w.Add(cork, LocalTransform.At(new Vector3(40, 2, 0)));
        w.Add(cork, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(1, 1, 1) });
        w.Add(cork, new BodyConfig { mass = 1f * WaterSurface.Density * 0.2f }.Bake());
        w.Add(cork, new BodyVelocity());
        w.Add(cork, new BuoyancyConfig { resolution = 2, linearDrag = 20f, quadraticDrag = 0.6f }.Bake());
        w.Sync();
        for (int i = 0; i < 600; i++) w.Update(Dt);

        True(w.Get<LocalTransform>(stone).Position.y < -5f, "three times the sea's density sinks");
        float corkY = w.Get<LocalTransform>(cork).Position.y;
        True(corkY > 0.15f, $"a fifth of it rides high, most of it out of the water (y={corkY:0.00})");

        // the hull case: displacement is not the volume of the shell. The same 2 m box told it displaces 8× more
        // carries 8× the mass at the same draft - which is why a steel ship floats.
        var hull = w.CreateEntity();
        w.Add(hull, LocalTransform.At(new Vector3(60, 6, 0)));
        w.Add(hull, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(side, side, side) });
        w.Add(hull, new BodyConfig { mass = mass * 8f }.Bake());
        w.Add(hull, new BodyVelocity());
        w.Add(hull, new BuoyancyConfig { volume = volume * 8f, resolution = 3, linearDrag = 20f, quadraticDrag = 0.6f, angularDrag = 2f }.Bake());
        w.Sync();
        for (int i = 0; i < 1200; i++) w.Update(Dt);
        float hullY = w.Get<LocalTransform>(hull).Position.y;
        True(Mathf.Abs(hullY) < 0.15f,
             $"a hull that displaces more than it is made of floats on the displacement, not the shell (y={hullY:0.000})");
    }

    /// <summary>Heeled over, a floating body rights itself: the dry side pushes up harder than the wet side.</summary>
    private static void Righting(Action<bool, string> True)
    {
        using var w = NewWorld();
        var raft = w.CreateEntity();
        w.Add(raft, LocalTransform.At(new Vector3(100, 0.2f, 0), Quaternion.Euler(0f, 0f, 35f)));
        w.Add(raft, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(4f, 0.6f, 2f) });
        w.Add(raft, new BodyConfig { mass = 4f * 0.6f * 2f * WaterSurface.Density * 0.4f }.Bake());
        w.Add(raft, new BodyVelocity());
        w.Add(raft, new BuoyancyConfig { resolution = 3, linearDrag = 20f, quadraticDrag = 0.6f, angularDrag = 3f }.Bake());
        w.Sync();

        for (int i = 0; i < 1500; i++) w.Update(Dt);

        float roll = Mathf.Abs(Mathf.DeltaAngle(w.Get<LocalTransform>(raft).Rotation.eulerAngles.z, 0f));
        True(roll < 10f, $"a raft heeled 35° rights itself ({roll:0.0}° left)");
    }

    /// <summary>A propeller pushes while it is in the water, and does nothing in the air.</summary>
    private static void Engines(Action<bool, string> True)
    {
        using var w = NewWorld();
        var boat = w.CreateEntity();
        w.Add(boat, LocalTransform.At(new Vector3(200, 0f, 0)));
        w.Add(boat, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(2f, 1.2f, 5f) }); // beam, draft, length: forward is +z
        w.Add(boat, new BodyConfig { mass = 1200f, angularDamping = 0.4f }.Bake());
        w.Add(boat, new BodyVelocity());
        // 1200 kg displacing 2.2 m³: it floats at about half its draft, which is where a propeller 0.5 m
        // under the hull's centre is actually in the water
        w.Add(boat, new BuoyancyConfig { volume = 2.2f, resolution = 3, linearDrag = 20f, quadraticDrag = 0.12f, angularDrag = 3f, planingLift = 0.5f, planingTrim = 4f }.Bake());
        w.Add(boat, new VesselEngineConfig { maxThrust = 9000f, sternOffset = new[] { 0f, -0.35f, -2.5f }, steerAngle = 28f }.Bake());
        w.Add(boat, new VesselControl { Throttle = 1f });
        w.Sync();

        // run it up to speed, then watch it: a planing hull that porpoises trims up, loses lift, drops its stern and
        // lifts its propeller out of the water, over and over
        for (int i = 0; i < 600; i++) w.Update(Dt);
        float trimLow = 90f, trimHigh = -90f; int dry = 0;
        for (int i = 0; i < 300; i++)
        {
            w.Update(Dt);
            float t0 = Mathf.Asin(Mathf.Clamp((w.Get<LocalTransform>(boat).Rotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
            trimLow = Mathf.Min(trimLow, t0); trimHigh = Mathf.Max(trimHigh, t0);
            if (!w.Get<VesselEngine>(boat).Biting) dry++;
        }
        float speed = w.Get<BodyVelocity>(boat).Linear.magnitude;
        True(speed > 8f, $"full throttle drives the hull along (speed={speed:0.0} m/s)");
        True(speed < 40f, $"and drag holds it to a top speed instead of accelerating for ever (speed={speed:0.0} m/s)");
        True(trimHigh - trimLow < 1f, $"it runs steady instead of porpoising (trim between {trimLow:0.0}° and {trimHigh:0.0}° over five seconds)");
        True(dry == 0, $"and its propeller stays in the water the whole time (out for {dry} of 300 steps)");
        True(Mathf.Abs(trimLow) < 8f && Mathf.Abs(trimHigh) < 8f, $"at a trim that neither rears up nor buries the bow ({trimLow:0.0}°; a box runs slightly bow-down - see PlaningCentre)");
        True(w.Get<Buoyancy>(boat).Submerged < 0.75f, "and rides up as it goes, instead of ploughing");

        // a hull out of the water has nothing to push against
        var flying = w.CreateEntity();
        w.Add(flying, LocalTransform.At(new Vector3(300, 40f, 0)));
        w.Add(flying, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(2f, 1.2f, 5f) });
        w.Add(flying, new BodyConfig { mass = 1200f, useGravity = false }.Bake());
        w.Add(flying, new BodyVelocity());
        w.Add(flying, new BuoyancyConfig { volume = 2.2f, resolution = 2 }.Bake());
        w.Add(flying, new VesselEngineConfig { maxThrust = 9000f, sternOffset = new[] { 0f, -0.35f, -2.5f } }.Bake());
        w.Add(flying, new VesselControl { Throttle = 1f });
        w.Sync();
        for (int i = 0; i < 120; i++) w.Update(Dt);
        True(w.Get<BodyVelocity>(flying).Linear.magnitude < 0.5f,
             "a hull off a wave crest has nothing to push against: no thrust in the air");

        // the helm turns it
        w.Get<VesselControl>(boat).Steer = 1f;
        float headingBefore = Heading(w, boat);
        for (int i = 0; i < 300; i++) w.Update(Dt);
        float turned = Mathf.Abs(Mathf.DeltaAngle(Heading(w, boat), headingBefore));
        True(turned > 15f, $"the helm turns the hull ({turned:0.0}° of heading)");
    }

    /// <summary>Weight is weight: a raft floats lower with people on it, and is shoved by a run across it.</summary>
    private static void Boarding(Action<bool, string> True)
    {
        using var w = NewWorld();
        // a raft that floats with plenty to spare
        var raft = w.CreateEntity();
        w.Add(raft, LocalTransform.At(new Vector3(400, 0f, 0)));
        w.Add(raft, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(6f, 0.5f, 6f) });
        w.Add(raft, new BodyConfig { mass = 600f, angularDamping = 0.6f }.Bake());
        w.Add(raft, new BodyVelocity());
        w.Add(raft, new BuoyancyConfig { volume = 6f, resolution = 3, angularDrag = 4f, heaveDamping = 0.9f }.Bake());
        w.Sync();
        // a floating body never stops moving entirely, so the waterline is a mean, not a single reading
        float Waterline()
        {
            double sum = 0; const int samples = 600;
            for (int i = 0; i < samples; i++) { w.Update(Dt); sum += w.Get<LocalTransform>(raft).Position.y; }
            return (float)(sum / samples);
        }

        for (int i = 0; i < 1200; i++) w.Update(Dt);
        float empty = Waterline();

        // four of them aboard
        var crew = new Entity[4];
        var spots = new[] { new Vector3(-1.5f, 0, -1.5f), new Vector3(1.5f, 0, -1.5f), new Vector3(-1.5f, 0, 1.5f), new Vector3(1.5f, 0, 1.5f) };
        for (int i = 0; i < crew.Length; i++)
        {
            var c = w.CreateEntity();
            w.Add(c, LocalTransform.At(new Vector3(400, 1.4f, 0) + spots[i]));
            w.Add(c, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
            w.Add(c, new BodyConfig { mass = 80f }.Bake());
            w.Add(c, new BodyVelocity());
            w.Add(c, new CharacterMotorConfig { rideHeight = 0.5f, pressesOnGround = true }.Bake());
            crew[i] = c;
        }
        w.Sync();
        for (int i = 0; i < 1800; i++) w.Update(Dt);
        float loaded = Waterline();

        float sank = empty - loaded;
        True(w.Get<CharacterMotor>(crew[0]).IsGrounded, "the crew are standing on the raft, not in it");
        // all of their weight reaches it: 320 kg on 6 m³ of displacement spread over 36 m² of box is 320 / (1025 × 12) m
        float expected = 4f * 80f / (WaterSurface.Density * 12f);
        True(Mathf.Abs(sank - expected) < 0.002f, $"a raft floats lower with four aboard by their whole weight ({sank * 1000f:0.0} mm, expected {expected * 1000f:0.0} mm)");

        // and a run across it pushes back
        var before = w.Get<BodyVelocity>(raft).Linear;
        for (int i = 0; i < 120; i++)
        {
            foreach (var c in crew) w.Get<CharacterMotor>(c).Desired = new Vector3(0f, 0f, 4f);
            w.Update(Dt);
        }
        float shove = (w.Get<BodyVelocity>(raft).Linear - before).z;
        True(shove < -0.05f, $"four of them running one way shoves the raft the other ({shove:0.000} m/s)");
    }

    private static float Heading(World w, Entity e)
    {
        var f = w.Get<LocalTransform>(e).Rotation * Vector3.forward;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }
}
