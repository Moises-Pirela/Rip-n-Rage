namespace RipNRage.Engine.Ecs.Transforms
{
    /// <summary>Pose relative to the parent (or the world when there is no <see cref="Parent"/>).</summary>
    public struct LocalTransform : IComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static LocalTransform At(Vector3 position) => new LocalTransform { Position = position, Rotation = Quaternion.identity, Scale = Vector3.one };
        public static LocalTransform At(Vector3 position, Quaternion rotation) => new LocalTransform { Position = position, Rotation = rotation, Scale = Vector3.one };
        public static readonly LocalTransform Identity = At(Vector3.zero);

        public Matrix4x4 Matrix => Matrix4x4.TRS(Position, Rotation, Scale);
        public Vector3 Forward => Rotation * Vector3.forward;
    }

    /// <summary>Attaches this entity to another: its <see cref="LocalTransform"/> is then relative to the parent.</summary>
    public struct Parent : IComponent
    {
        public Entity Value;
    }

    /// <summary>
    /// World pose, written by <see cref="TransformSystem"/> every frame (read-only for everything else). Carries the
    /// matrix plus the decomposed world position / rotation / lossy scale the renderer and gameplay want, computed
    /// exactly like the legacy Transform (rotation = parent * local, lossy scale = component-wise product).
    /// </summary>
    public struct LocalToWorld : IComponent
    {
        public Matrix4x4 Value;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        /// <summary>Frame the value was computed for (internal bookkeeping for the hierarchy pass).</summary>
        internal long Frame;

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 Up => Rotation * Vector3.up;
    }

    public sealed class LocalTransformConfig : ComponentConfig<LocalTransform>
    {
        public float[] position = { 0, 0, 0 };
        /// <summary>Euler angles in degrees.</summary>
        public float[] rotation = { 0, 0, 0 };
        public float[] scale = { 1, 1, 1 };
        public override LocalTransform Bake() => new LocalTransform
        {
            Position = new Vector3(position[0], position[1], position[2]),
            Rotation = Quaternion.Euler(rotation[0], rotation[1], rotation[2]),
            Scale = new Vector3(scale[0], scale[1], scale[2]),
        };
    }

    /// <summary>Configs never reference runtime entities; the world pose is derived, so this bakes the identity.</summary>
    public sealed class LocalToWorldConfig : ComponentConfig<LocalToWorld>
    {
        public override LocalToWorld Bake() => new LocalToWorld { Value = Matrix4x4.identity, Rotation = Quaternion.identity, Scale = Vector3.one };
    }
}
