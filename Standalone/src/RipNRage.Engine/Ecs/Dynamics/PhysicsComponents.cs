using System;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Graphics;

// Namespace is "Dynamics", not "Physics": RipNRage.Engine.Physics is the legacy static facade, and a namespace of
// the same name would shadow it inside RipNRage.Engine.Ecs.
namespace RipNRage.Engine.Ecs.Dynamics
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
        public AssetHandle<ConvexHull> Hull;
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

    /// <summary>Shape assets colliders reference. Meshes share <see cref="RenderAssets.Meshes"/> so a "box" is one asset.</summary>
    public static class PhysicsAssets
    {
        public static readonly AssetRegistry<ConvexHull> Hulls = new AssetRegistry<ConvexHull>();
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
                    // Convex hulls come from level brushes (code / the level loader sets ColliderShape.Hull);
                    // a config cannot describe one yet (layer 10: editors on configs).
                    throw new NotSupportedException("convex colliders cannot be baked from a config yet; set ColliderShape.Hull from code");
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

    internal static class PhysicsConfigUtil
    {
        public static Vector3 Vec(float[] v, float fill = 0f) => v == null ? new Vector3(fill, fill, fill)
            : new Vector3(v.Length > 0 ? v[0] : fill, v.Length > 1 ? v[1] : fill, v.Length > 2 ? v[2] : fill);
    }
}
