using System;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Ecs.Physics
{
    public enum BodyKind
    {
        /// <summary>Simulated: forces, contacts and joints move it.</summary>
        Dynamic,
        /// <summary>Infinite mass, moved by its velocity / transform, pushes dynamic bodies.</summary>
        Kinematic,
    }

    /// <summary>
    /// A rigid body (layer 2 data; simulated from step 2.4). Same parameters and defaults as the legacy
    /// <see cref="RipNRage.Engine.RigidBody"/>. An entity with a <see cref="ColliderShape"/> but no Body is static
    /// geometry. Velocities live in <see cref="BodyVelocity"/> so systems that only steer don't write the whole body.
    /// </summary>
    public struct Body : IComponent
    {
        public BodyKind Kind;
        public float Mass;
        public float LinearDamping, AngularDamping;
        public bool UseGravity;
        public float GravityScale;
        public bool FreezeRotation;
        public CollisionDetectionMode CollisionDetection;
        public bool Interpolate;
        public bool CanSleep;
        public float SleepThreshold, SleepDelay;
        public float MaxSpeed, MaxAngularSpeed;
    }

    public struct BodyVelocity : IComponent
    {
        public Vector3 Linear, Angular;
    }

    public enum ShapeKind { Box, Sphere, Capsule, Convex, Mesh }

    /// <summary>
    /// The collision shape in the entity's local space (scaled by <see cref="Transforms.LocalToWorld"/> like the legacy
    /// colliders). Convex / mesh data is shared and referenced by handle.
    /// </summary>
    public struct ColliderShape : IComponent
    {
        public ShapeKind Kind;
        public Vector3 Center;
        /// <summary>Box: full size. Unused by the other kinds.</summary>
        public Vector3 Size;
        /// <summary>Sphere / capsule radius.</summary>
        public float Radius;
        /// <summary>Capsule total height along local Y (including the caps).</summary>
        public float Height;
        public AssetHandle<ConvexColliderAsset> Hull;
        public AssetHandle<Mesh> Mesh;
        /// <summary>Static geometry is cached in the broadphase's static grid (level geometry, props that never move).</summary>
        public bool IsStatic;
    }

    /// <summary>Surface properties (the legacy <see cref="PhysicsMaterial"/> as plain data).</summary>
    public struct SurfaceMaterial : IComponent
    {
        public float StaticFriction, DynamicFriction, Restitution;
        public CombineMode FrictionCombine, RestitutionCombine;
    }

    /// <summary>Which pairs may collide: layer-mask bits, the layer matrix, and the Box2D-style group index.</summary>
    public struct CollisionFilter : IComponent
    {
        public int Layer;
        public int Mask;
        public bool UseLayerMatrix;
        /// <summary>Same negative group: never collide; same positive group: always collide; 0: no group.</summary>
        public int Group;
    }

    /// <summary>Tag: the collider reports overlaps instead of producing contacts.</summary>
    [SparseComponent] public struct Trigger : IComponent { }

    /// <summary>Tag set by the simulation while the body sleeps (toggles often: sparse).</summary>
    [SparseComponent] public struct Sleeping : IComponent { }

    /// <summary>A shared convex collider: the hull (queries, bounds) plus the faces / vertices it was built from (narrow-phase polytopes).</summary>
    public sealed class ConvexColliderAsset
    {
        public readonly ConvexHull Hull;
        public readonly Plane[] Faces;
        public readonly Vector3[] Vertices;
        public ConvexColliderAsset(Plane[] faces, Vector3[] vertices) { Faces = faces; Vertices = vertices; Hull = ConvexHull.FromPlanes(faces, vertices, true); }
    }

    /// <summary>Shape assets colliders reference. Meshes share <see cref="RenderAssets.Meshes"/> so a "box" is one asset.</summary>
    public static class PhysicsAssets
    {
        public static readonly AssetRegistry<ConvexColliderAsset> Hulls = new AssetRegistry<ConvexColliderAsset>();
        public static AssetRegistry<Mesh> Meshes => RenderAssets.Meshes;
    }

    // ---------------------------------------------------------------- configs (JSON prefabs)

    public sealed class BodyConfig : ComponentConfig<Body>
    {
        public BodyKind kind = BodyKind.Dynamic;
        public float mass = 1f;
        public float linearDamping = 0.05f, angularDamping = 0.1f;
        public bool useGravity = true;
        public float gravityScale = 1f;
        public bool freezeRotation;
        public CollisionDetectionMode collisionDetection = CollisionDetectionMode.Discrete;
        public bool interpolate;
        public bool canSleep = true;
        /// <summary>Null = the tunables.cfg defaults (<see cref="RigidBody.DefaultSleepThreshold"/> etc.) at bake time.</summary>
        public float? sleepThreshold, sleepDelay, maxSpeed, maxAngularSpeed;

        public override Body Bake()
        {
            if ((kind == BodyKind.Dynamic && !(mass > 0f)) || !float.IsFinite(mass))
                throw new ArgumentOutOfRangeException(nameof(mass), mass, "a dynamic body needs a finite mass > 0");
            return new Body
            {
                Kind = kind, Mass = mass, LinearDamping = linearDamping, AngularDamping = angularDamping,
                UseGravity = useGravity, GravityScale = gravityScale, FreezeRotation = freezeRotation,
                CollisionDetection = collisionDetection, Interpolate = interpolate, CanSleep = canSleep,
                SleepThreshold = sleepThreshold ?? RigidBody.DefaultSleepThreshold,
                SleepDelay = sleepDelay ?? RigidBody.DefaultSleepDelay,
                MaxSpeed = maxSpeed ?? RigidBody.DefaultMaxSpeed,
                MaxAngularSpeed = maxAngularSpeed ?? RigidBody.DefaultMaxAngularSpeed,
            };
        }
    }

    public sealed class BodyVelocityConfig : ComponentConfig<BodyVelocity>
    {
        public float[] linear = { 0, 0, 0 }, angular = { 0, 0, 0 };
        public override BodyVelocity Bake() => new BodyVelocity { Linear = PhysicsConfigUtil.Vec(linear), Angular = PhysicsConfigUtil.Vec(angular) };
    }

    public sealed class ColliderShapeConfig : ComponentConfig<ColliderShape>
    {
        public ShapeKind kind = ShapeKind.Box;
        public float[] center = { 0, 0, 0 };
        public float[] size = { 1, 1, 1 };
        public float radius = 0.5f, height = 2f;
        /// <summary>Mesh colliders: a <see cref="MeshLibrary"/> name (shared with rendering).</summary>
        public string mesh;
        /// <summary>
        /// Convex colliders, local space: the hull's corner points as [x, y, z] triples. With <see cref="planes"/> the
        /// faces are taken as given (brushes: [nx, ny, nz, d], outward normals); without, they are derived from the points.
        /// </summary>
        public float[][] vertices;
        public float[][] planes;
        public bool isStatic;

        public override ColliderShape Bake()
        {
            var s = new ColliderShape { Kind = kind, Center = PhysicsConfigUtil.Vec(center), Size = PhysicsConfigUtil.Vec(size, 1f), Radius = radius, Height = height, IsStatic = isStatic };
            switch (kind)
            {
                case ShapeKind.Box when !(s.Size.x > 0 && s.Size.y > 0 && s.Size.z > 0):
                    throw new ArgumentOutOfRangeException(nameof(size), "box size must be > 0 on every axis");
                case ShapeKind.Sphere when !(radius > 0):
                case ShapeKind.Capsule when !(radius > 0):
                    throw new ArgumentOutOfRangeException(nameof(radius), radius, "radius must be > 0");
                case ShapeKind.Mesh:
                    if (string.IsNullOrEmpty(mesh)) throw new ArgumentException("a mesh collider needs a mesh name", nameof(mesh));
                    s.Mesh = PhysicsAssets.Meshes.GetOrLoad(mesh, MeshLibrary.Get);
                    break;
                case ShapeKind.Convex:
                    s.Hull = ConvexConfig.Bake(vertices, planes);
                    break;
            }
            return s;
        }
    }

    public sealed class SurfaceMaterialConfig : ComponentConfig<SurfaceMaterial>
    {
        public float staticFriction = 0.6f, dynamicFriction = 0.5f, restitution;
        public CombineMode frictionCombine = CombineMode.Average, restitutionCombine = CombineMode.Maximum;
        public override SurfaceMaterial Bake() => new SurfaceMaterial
        {
            StaticFriction = staticFriction, DynamicFriction = dynamicFriction, Restitution = restitution,
            FrictionCombine = frictionCombine, RestitutionCombine = restitutionCombine,
        };
    }

    public sealed class CollisionFilterConfig : ComponentConfig<CollisionFilter>
    {
        public int layer;
        public int mask = ~0;
        public bool useLayerMatrix = true;
        public int group;
        public override CollisionFilter Bake()
        {
            if ((uint)layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "layer must be 0..31");
            return new CollisionFilter { Layer = layer, Mask = mask, UseLayerMatrix = useLayerMatrix, Group = group };
        }
    }

    public sealed class TriggerConfig : ComponentConfig<Trigger> { public override Trigger Bake() => default; }
    public sealed class SleepingConfig : ComponentConfig<Sleeping> { public override Sleeping Bake() => default; }

    /// <summary>Convex hull data from a config: validated, faces derived when not given, interned by content (equal hulls share one asset).</summary>
    internal static class ConvexConfig
    {
        public const int MaxDerivedPoints = 64;
        private const float Eps = 1E-4f;

        public static AssetHandle<ConvexColliderAsset> Bake(float[][] vertices, float[][] planes)
        {
            if (vertices == null || vertices.Length < 4) throw new ArgumentException("a convex collider needs at least 4 vertices", nameof(vertices));
            var pts = new Vector3[vertices.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var v = vertices[i];
                if (v == null || v.Length != 3 || !float.IsFinite(v[0]) || !float.IsFinite(v[1]) || !float.IsFinite(v[2]))
                    throw new ArgumentException($"convex vertex {i} must be 3 finite numbers", nameof(vertices));
                pts[i] = new Vector3(v[0], v[1], v[2]);
            }
            Plane[] faces = planes != null ? ParsePlanes(planes, pts) : DeriveFaces(pts);
            string key = "convex:" + Fingerprint(pts, faces);
            return PhysicsAssets.Hulls.GetOrLoad(key, _ => new ConvexColliderAsset(faces, pts));
        }

        private static Plane[] ParsePlanes(float[][] planes, Vector3[] pts)
        {
            if (planes.Length < 4) throw new ArgumentException("a convex collider needs at least 4 planes", nameof(planes));
            var result = new Plane[planes.Length];
            for (int i = 0; i < planes.Length; i++)
            {
                var p = planes[i];
                if (p == null || p.Length != 4) throw new ArgumentException($"convex plane {i} must be [nx, ny, nz, d]", nameof(planes));
                var n = new Vector3(p[0], p[1], p[2]);
                float len = n.magnitude;
                if (!(len > 1E-6f) || !float.IsFinite(p[3])) throw new ArgumentException($"convex plane {i} has no usable normal", nameof(planes));
                result[i] = new Plane(n / len, p[3] / len);
                foreach (var v in pts)
                    if (result[i].Distance(v) > 1E-3f * MathF.Max(1f, MathF.Abs(result[i].dist)))
                        throw new ArgumentException($"vertex {v} lies outside convex plane {i}: planes and vertices disagree", nameof(planes));
            }
            return result;
        }

        /// <summary>Brute-force face planes of a small point set: every triple whose plane has all points behind it. O(n^4), hence the point cap.</summary>
        internal static Plane[] DeriveFaces(Vector3[] pts)
        {
            if (pts.Length > MaxDerivedPoints) throw new ArgumentException($"deriving faces supports up to {MaxDerivedPoints} vertices; give planes for larger hulls");
            var faces = new System.Collections.Generic.List<Plane>();
            float scale = 0f; foreach (var p in pts) scale = MathF.Max(scale, p.magnitude);
            float eps = Eps * MathF.Max(1f, scale);
            for (int i = 0; i < pts.Length; i++)
                for (int j = i + 1; j < pts.Length; j++)
                    for (int k = j + 1; k < pts.Length; k++)
                    {
                        Vector3 n = Vector3.Cross(pts[j] - pts[i], pts[k] - pts[i]);
                        float len = n.magnitude;
                        if (len < eps * eps) continue; // collinear
                        n /= len;
                        float d = Vector3.Dot(n, pts[i]);
                        bool front = false, back = false;
                        foreach (var p in pts) { float s = Vector3.Dot(n, p) - d; if (s > eps) front = true; else if (s < -eps) back = true; if (front && back) break; }
                        if (front && back) continue;
                        if (!front && !back) throw new ArgumentException("convex vertices are coplanar: the hull has no volume");
                        if (front) { n = -n; d = -d; } // outward: every point behind the plane
                        bool dup = false;
                        foreach (var f in faces) if (Vector3.Dot(f.normal, n) > 1f - 1E-5f && MathF.Abs(f.dist - d) < eps) { dup = true; break; }
                        if (!dup) faces.Add(new Plane(n, d));
                    }
            if (faces.Count < 4) throw new ArgumentException("convex vertices do not enclose a volume");
            return faces.ToArray();
        }

        private static string Fingerprint(Vector3[] pts, Plane[] faces)
        {
            var sb = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var p in pts) sb.Append(p.x.ToString("R", inv)).Append(',').Append(p.y.ToString("R", inv)).Append(',').Append(p.z.ToString("R", inv)).Append(';');
            sb.Append('|');
            foreach (var f in faces) sb.Append(f.normal.x.ToString("R", inv)).Append(',').Append(f.normal.y.ToString("R", inv)).Append(',').Append(f.normal.z.ToString("R", inv)).Append(',').Append(f.dist.ToString("R", inv)).Append(';');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Runtime-only: the entity's slot in the one simulation (<see cref="PhysicsWorld.Table"/>; the slot also points at
    /// the entity's <see cref="SolverBody"/> when it has a <see cref="Body"/>). Added and cleared by the physics sync
    /// system (2.4); prefabs never set it, so a baked or copied entity starts unregistered.
    /// </summary>
    public struct PhysicsHandle : IComponent
    {
        public PhysicsProxy Proxy;
        public bool IsRegistered => !Proxy.IsNone;
    }

    /// <summary>Exists only to satisfy one-config-per-component; always bakes "not registered".</summary>
    public sealed class PhysicsHandleConfig : ComponentConfig<PhysicsHandle> { public override PhysicsHandle Bake() => default; }

    internal static class PhysicsConfigUtil
    {
        public static Vector3 Vec(float[] v, float fill = 0f) => v == null ? new Vector3(fill, fill, fill)
            : new Vector3(v.Length > 0 ? v[0] : fill, v.Length > 1 ? v[1] : fill, v.Length > 2 ? v[2] : fill);
    }
}
