using System;
using RipNRage.Engine.Ecs.Transforms;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>
    /// Where the water is, for everything that has to float on it, swim in it or be drawn under it. One surface,
    /// one function: the game points this at its water volumes as the level loads, the physics asks it how deep a
    /// sample is, and the renderer shades to the same height.
    ///
    /// <para>Flat while <see cref="HeightOf"/> is a constant; when the sea gets waves it becomes a sum of them and
    /// nothing above here changes. Engine-side rather than game-side because the solver needs it every step.</para>
    /// </summary>
    public static class WaterSurface
    {
        /// <summary>Height of the water at a world x/z — or <see cref="None"/> where there is no water at all.</summary>
        public static Func<float, float, float> HeightOf;

        /// <summary>"No water here": lower than any level goes.</summary>
        public const float None = -1e9f;

        /// <summary>Kilograms per cubic metre. Sea water; fresh is 1000.</summary>
        public static float Density = 1025f;

        public static bool Exists => HeightOf != null;

        public static float Height(float x, float z) => HeightOf?.Invoke(x, z) ?? None;

        /// <summary>How far below the surface a point is (negative above it; 0 where there is no water).</summary>
        public static float DepthAt(Vector3 p)
        {
            if (HeightOf == null) return 0f;
            float h = HeightOf(p.x, p.z);
            return h <= None ? 0f : h - p.y;
        }

        /// <summary>Forgets the surface (a level ending, a test finishing).</summary>
        public static void Clear() => HeightOf = null;
    }

    /// <summary>
    /// What floats, and how. The body is sampled over its own shape: every sample that is under water pushes up
    /// with the weight of the water it displaces, and drags against the water it is moving through. Partial
    /// submersion, roll, pitch and the heave of a wave all fall out of that — a hull half out of the water is
    /// simply one with half its samples in it.
    ///
    /// <para><see cref="Volume"/> is what the body <em>displaces</em>, which is not the volume of what it is made
    /// of: a hull is a shell around air, and that air is most of why it floats. Leave it at 0 and the shape's own
    /// volume is used, which is right for a crate and wrong for a ship.</para>
    /// </summary>
    public struct Buoyancy : IComponent
    {
        /// <summary>Displaced volume in m³ (0 = the collider's own volume).</summary>
        public float Volume;
        /// <summary>
        /// The displacement box — the hull's envelope, in the entity's frame — when it is not the collider: a body
        /// made of parts (a hull, a cabin, rails) floats on the space its hull encloses, not on its parts. Zero size =
        /// the entity's own <see cref="ColliderShape"/>.
        /// </summary>
        public Vector3 Center, Size;
        /// <summary>Samples per axis over the shape's box (2 = 8 samples, 3 = 27). More is smoother and dearer.</summary>
        public int Resolution;
        /// <summary>Viscous drag: N·s/m for every m² the hull shows the flow. The small term, and what settles a
        /// drifting hull.</summary>
        public float LinearDrag;
        /// <summary>Pressure drag coefficient (the C<sub>d</sub> of ½ρC<sub>d</sub>Av²): the big term, and what
        /// gives a hull a top speed instead of accelerating for ever. A streamlined hull is around 0.2–0.4.</summary>
        public float QuadraticDrag;
        /// <summary>Resistance to spinning in the water — what stops a hull rolling for ever.</summary>
        public float AngularDrag;
        /// <summary>
        /// How hard the water damps rise and fall, as a fraction of critical damping (0 = bobs for ever,
        /// 1 = settles without a single overshoot).
        ///
        /// <para>A fraction rather than a coefficient because the spring it damps is the body's own: a floating
        /// body heaves on a stiffness of ρgA, and what reads as "water" is a damping ratio of a few tenths of
        /// critical whatever the hull's size. Straight viscous drag is a fraction of a percent of critical for
        /// anything heavy, so a hull damped that way bobs for a minute.</para>
        /// </summary>
        public float HeaveDamping;
        /// <summary>
        /// The same for pitch and roll: how hard the water damps a hull rocking, as a fraction of critical for its own
        /// righting stiffness (ρg times the second moment of its waterplane). What stops a planing hull porpoising —
        /// trimming bow-up, losing lift, dropping its stern, lifting its propeller out of the water and doing it again.
        /// </summary>
        public float RockDamping;
        /// <summary>
        /// Lift coefficient of the hull's bottom as a planing surface (0 = never planes: a displacement hull that
        /// ploughs through the water at hull speed for ever).
        ///
        /// <para>Planing is why a speedboat does 25 m/s and a barge does 5. The bottom meets the water at a small
        /// angle and the water turned aside by it pushes the hull up: ½ρC<sub>l</sub>Av²sinα, which grows with
        /// speed rather than with depth. That is the whole trick — as the hull lifts, less of it is in the water,
        /// drag collapses, and it goes faster still. Lift tied to how submerged the hull was could never do it:
        /// rising would kill the very force doing the lifting.</para>
        /// </summary>
        public float PlaningLift;
        /// <summary>
        /// The angle the hull's bottom makes with its keel, in degrees — the deadrise a real planing hull is built
        /// with, and the running trim its weight gives it. A box has neither, and a bottom perfectly parallel to
        /// the water turns none of it aside and so never planes at all.
        /// </summary>
        public float PlaningTrim;
        /// <summary>
        /// Where the planing lift acts, as a fraction of the half-length aft of the middle. Aft of the centre of mass
        /// is what makes planing stable — lift the bow and more lift behind the centre puts it back down — but too far
        /// aft and it drives the bow under. Real planing hulls carry it roughly 0.35–0.45 of the length from the
        /// transom — but a box hull is not a planing hull: half-way aft is the setting that runs a box steadily (it
        /// trims about 3° bow-down at speed), and moving it forward on a box buries the bow or launches the hull.
        /// Realistic running trim comes from a hull's shape, which is what the convex-brush hulls are for.
        /// </summary>
        public float PlaningCentre;
        // ---- state, for whoever wants to know (an engine that must not drive in the air; the VFX) ----
        /// <summary>0 = out of the water, 1 = under it.</summary>
        public float Submerged;
        public bool InWater;
    }

    public sealed class BuoyancyConfig : ComponentConfig<Buoyancy>
    {
        [InspectTip("displaced volume in m³ (0 = the collider's own: right for a crate, wrong for a hull, which is a shell round air)")]
        public float volume;
        [InspectRange(1, 4, 1), InspectTip("samples per axis: 2 = 8 samples, 3 = 27")]
        public int resolution = 2;
        [InspectTip("viscous drag: N·s/m per m² the hull shows the flow")]
        public float linearDrag = 20f;
        [InspectTip("pressure drag coefficient (½ρCdAv²): a streamlined hull is 0.2-0.4")]
        public float quadraticDrag = 0.3f;
        [InspectTip("what stops a hull rolling for ever")]
        public float angularDrag = 0.9f;
        [InspectRange(0, 1, 0.05f), InspectTip("how hard the water damps rise and fall, as a fraction of critical (0 = bobs for ever, 1 = no overshoot)")]
        public float heaveDamping = 0.35f;
        [InspectRange(0, 1, 0.05f), InspectTip("how hard the water damps pitch and roll, as a fraction of critical (what stops a fast hull porpoising)")]
        public float rockDamping = 0.35f;
        [InspectTip("lift coefficient of the hull bottom as a planing surface (0 = a displacement hull, ploughing along at hull speed)")]
        public float planingLift;
        [InspectRange(0, 15, 0.5f), InspectTip("the bottom's built-in angle to the keel (deadrise plus running trim); a flat box planes on nothing")]
        public float planingTrim = 4f;
        [InspectRange(0, 1, 0.05f), InspectTip("where the planing lift acts, as a fraction of the half-length aft of the middle (aft is stable; too far aft buries the bow)")]
        public float planingCentre = 0.5f;
        [InspectTip("the displacement box (centre and size, the entity's frame) when it is not the collider - a hull of parts floats on its envelope; 0 0 0 = the collider")]
        public float[] center = { 0, 0, 0 }, size = { 0, 0, 0 };
        public override Buoyancy Bake() => new Buoyancy
        {
            Center = PhysicsConfigUtil.Vec(center), Size = PhysicsConfigUtil.Vec(size),
            Volume = volume, Resolution = Mathf.Clamp(resolution, 1, 4),
            LinearDrag = linearDrag, QuadraticDrag = quadraticDrag, AngularDrag = angularDrag,
            HeaveDamping = heaveDamping, RockDamping = rockDamping, PlaningLift = planingLift, PlaningTrim = planingTrim, PlaningCentre = planingCentre,
        };
    }

    /// <summary>
    /// Floats everything with a <see cref="Buoyancy"/>, a fixed step at a time: sample the shape, push up on what
    /// is under water by the weight it displaces, drag it, and lift it if it is moving fast enough along the
    /// surface. Runs before the solver steps, so the forces it adds are in this step's solve.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    [Reads(typeof(ColliderShape))] // the displacement, when the component does not name a box of its own
    [Exclusive] // adds forces straight onto solver bodies
    public sealed class BuoyancySystem : SystemBase, IFixedTick
    {
        private Query _q;
        private PhysicsSyncSystem _sync;
        public override void OnCreate() =>
            _q = World.Query().With<Buoyancy>().WithReadOnly<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            if (!WaterSurface.Exists) return;
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            if (_sync == null) return;
            float gravity = MathF.Abs(PhysicsApi.gravity.y);
            if (gravity <= 0f) gravity = 9.81f;

            foreach (var chunk in _q)
            {
                var floats = chunk.Get<Buoyancy>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    var body = _sync.BodyOf(ents[i]);
                    if (body == null || body.isKinematic) { floats[i].InWater = false; floats[i].Submerged = 0f; continue; }
                    // what displaces water: the box the component names, or failing that the collider
                    Vector3 boxCentre, half;
                    if (floats[i].Size.x > 0f && floats[i].Size.y > 0f && floats[i].Size.z > 0f) { boxCentre = floats[i].Center; half = floats[i].Size * 0.5f; }
                    else if (World.Has<ColliderShape>(ents[i])) { var cs = World.Get<ColliderShape>(ents[i]); boxCentre = cs.Center; half = HalfExtents(in cs); }
                    else { floats[i].InWater = false; floats[i].Submerged = 0f; continue; } // nothing to float on
                    Float(ref floats[i], body, boxCentre, half, gravity, ctx.DeltaTime);
                }
            }
        }

        private static void Float(ref Buoyancy b, RipNRage.Engine.SolverBody body, Vector3 boxCentre, Vector3 half, float gravity, float dt)
        {
            int n = Mathf.Clamp(b.Resolution, 1, 4);
            int count = n * n * n;
            float shapeVolume = MathF.Max(8f * half.x * half.y * half.z, 1e-4f);
            float displaced = b.Volume > 0f ? b.Volume : shapeVolume;
            // what one sample is worth: its share of the displacement, and of the area that drags
            float sampleVolume = displaced / count;

            var rotation = body.rotation;
            // the box is placed from the entity's origin, not from the centre of mass: the two differ for any shape
            // off its centre, and for every body made of parts. (Measuring from the centre of mass, as this first
            // did, put an off-centre box's samples twice its offset away.)
            Vector3 origin = body.position - rotation * body.comOffsetLocal;
            float wet = 0f; // the sum of every sample's fill: how much of the body is under, continuously
            // a sample stands for a cell of the shape this tall; its centre is in the middle of it
            float cell = MathF.Max(2f * half.y / n, 1e-3f);

            for (int ix = 0; ix < n; ix++)
            for (int iy = 0; iy < n; iy++)
            for (int iz = 0; iz < n; iz++)
            {
                // sample centres, evenly spread through the shape's box
                Vector3 local = new Vector3(
                    half.x * (2f * (ix + 0.5f) / n - 1f),
                    half.y * (2f * (iy + 0.5f) / n - 1f),
                    half.z * (2f * (iz + 0.5f) / n - 1f));
                Vector3 point = origin + rotation * (boxCentre + local);

                // How much of this sample's cell is under. `depth` is measured at the cell's centre, so the cell is
                // wet from half a cell above the surface to fully under half a cell below it - and the force must
                // follow that the whole way. A first version skipped any sample whose *centre* was dry, which cut
                // the lift from half a cell's worth to nothing in one step as the centre crossed the surface: a
                // light hull that floated with a row of samples near the waterline chattered across that edge for
                // ever and never came to rest, and every load measured on it was an average over the chatter.
                float depth = WaterSurface.DepthAt(point);
                float fill = Mathf.Clamp01(depth / cell + 0.5f);
                if (fill <= 0f) continue;
                wet += fill;

                // up, by the weight of the water this sample displaces, at the place it displaces it: partial
                // submersion, trim and the righting of a heeled hull all fall out of where these land
                float up = WaterSurface.Density * gravity * sampleVolume * fill;
                body.AddForceAtPosition(new Vector3(0f, up, 0f), point);
            }

            // continuous too, for the same reason: the drag and the heave damping scale by it, and a count of whole
            // samples would switch them on and off like a relay as the hull bobbed
            b.Submerged = count > 0 ? wet / count : 0f;
            b.InWater = wet > 0f;

            if (!b.InWater) return;

            // Drag is the body's, not the samples'. A hull is dragged by the water it pushes aside - the area it
            // shows the flow - and the samples inside it are not wetted surface at all: charging each of them
            // drag over-counts it several times over, which is a hull that cannot get out of its own way.
            Vector3 vel = body.linearVelocity;
            float speed = vel.magnitude;
            if (speed > 1e-3f)
            {
                Vector3 dir = vel / speed;
                // the area presented to the flow: the box's three faces, weighted by how square-on each one is
                Vector3 facing = Quaternion.Inverse(body.rotation) * dir;
                float area = MathF.Abs(facing.x) * (4f * half.y * half.z)
                           + MathF.Abs(facing.y) * (4f * half.x * half.z)
                           + MathF.Abs(facing.z) * (4f * half.x * half.y);
                float force = (0.5f * WaterSurface.Density * b.QuadraticDrag * area * speed * speed
                               + b.LinearDrag * area * speed) * b.Submerged;
                // never more than it takes to stop this step: drag slows a hull, it does not reverse it
                float most = body.mass * speed / MathF.Max(dt, 1e-4f);
                body.AddForce(-dir * MathF.Min(force, most), ForceMode.Force);
            }

            // Heave: the water damps rise and fall. The spring is the body's own - a waterplane of area A pushes
            // back with ρgA for every metre it is pressed down - so the damping that matters is a fraction of
            // critical for *that* spring, which is the same few tenths whatever the hull weighs.
            // The stiffness of those springs is the sampled buoyancy's: every metre of waterplane carries the weight of
            // the water the samples stand for, which is the displacement spread through the shape's box.
            float displacementRatio = displaced / shapeVolume;
            float waterSpring = WaterSurface.Density * gravity * displacementRatio;
            if (b.HeaveDamping > 0f)
            {
                float stiffness = waterSpring * 4f * half.x * half.z; // ρg × waterplane area
                float critical = 2f * MathF.Sqrt(MathF.Max(stiffness * body.mass, 0f));
                float vy = body.linearVelocity.y;
                // the full fraction whenever the hull is in the water: it is a property of floating, not of how deep
                // (scaling it by the wetted fraction, as it first did, damped a hull riding high a tenth as hard)
                float damp = Mathf.Clamp01(b.HeaveDamping) * critical * vy;
                float most = body.mass * MathF.Abs(vy) / MathF.Max(dt, 1e-4f);
                body.AddForce(new Vector3(0f, -MathF.Sign(damp) * MathF.Min(MathF.Abs(damp), most), 0f), ForceMode.Force);
            }
            if (b.RockDamping > 0f)
            {
                float length = 2f * half.z, beam = 2f * half.x;
                // second moments of the waterplane: about the beam (pitch) and about the keel (roll)
                Rock(body, rotation * Vector3.right, waterSpring * beam * length * length * length / 12f, b.RockDamping, dt);
                Rock(body, rotation * Vector3.forward, waterSpring * length * beam * beam * beam / 12f, b.RockDamping, dt);
            }

            // spin dies in water
            if (b.AngularDrag > 0f)
                body.AddTorque(-body.angularVelocity * (b.AngularDrag * b.Submerged * body.mass), ForceMode.Force);

            // Planing. The hull's bottom meets the water at a small angle, and the water it turns aside pushes it
            // up: ½ρClA·v²·sinα, normal to the bottom. It grows with speed, not with depth, so once it has lifted
            // the hull the lift does not go away - which is exactly how a boat gets out of its own wake.
            if (b.PlaningLift > 0f && speed > 2f)
            {
                Vector3 up = body.rotation * Vector3.up;
                Vector3 bottom = origin + rotation * (boxCentre + new Vector3(0f, -half.y, 0f));
                // the bottom has to be in the water to plane on it (a little above still catches the surface)
                if (WaterSurface.DepthAt(bottom) > -0.25f)
                {
                    Vector3 dir = vel / speed;
                    // angle of attack: how far the bottom is tilted against the way it is going
                    float sinAlpha = Mathf.Clamp(-Vector3.Dot(up, dir) + MathF.Sin(b.PlaningTrim * Mathf.Deg2Rad), 0f, 0.35f); // past ~20° it stalls, not planes
                    if (sinAlpha > 1e-3f)
                    {
                        float bottomArea = 4f * half.x * half.z;
                        float lift = 0.5f * WaterSurface.Density * b.PlaningLift * bottomArea * speed * speed * sinAlpha;
                        // a hull is lifted by the water, not fired out of it
                        float most = body.mass * gravity * 3f;
                        // The lift acts aft of the hull's middle (PlaningCentre), which is what makes planing stable:
                        // lift the bow and the angle of attack grows, and more lift behind the centre of mass pushes
                        // the bow back down. Ahead of the middle a hull somersaults. With no rock damping even the
                        // middle reared it up; the water's damping of pitch (RockDamping) is what makes it settle.
                        Vector3 centreOfPressure = bottom - (body.rotation * Vector3.forward) * (half.z * Mathf.Clamp01(b.PlaningCentre));
                        body.AddForceAtPosition(up * MathF.Min(lift, most), centreOfPressure);
                    }
                }
            }
        }

        /// <summary>Damp rotation about <paramref name="axis"/> at a fraction of critical for a righting stiffness.</summary>
        private static void Rock(RipNRage.Engine.SolverBody body, Vector3 axis, float stiffness, float fraction, float dt)
        {
            float inverse = Vector3.Dot(axis, body.ApplyInverseInertia(axis));
            if (inverse <= 0f || stiffness <= 0f) return;
            float inertia = 1f / inverse;
            float w = Vector3.Dot(body.angularVelocity, axis);
            float critical = 2f * MathF.Sqrt(stiffness * inertia);
            float torque = Mathf.Clamp01(fraction) * critical * w;
            float most = inertia * MathF.Abs(w) / MathF.Max(dt, 1e-4f); // damping slows a roll, it never reverses one
            body.AddTorque(-axis * (MathF.Sign(torque) * MathF.Min(MathF.Abs(torque), most)), ForceMode.Force);
        }

        /// <summary>The box a shape samples over.</summary>
        private static Vector3 HalfExtents(in ColliderShape shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Sphere: return new Vector3(shape.Radius, shape.Radius, shape.Radius);
                case ShapeKind.Capsule: return new Vector3(shape.Radius, MathF.Max(shape.Height * 0.5f, shape.Radius), shape.Radius);
                default: return shape.Size * 0.5f;
            }
        }
    }
}
