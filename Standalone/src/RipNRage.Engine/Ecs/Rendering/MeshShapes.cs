using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Rendering
{
    /// <summary>
    /// "I am drawn as a box this big" (10.4). The mesh is a shared asset named after its own shape
    /// (<see cref="MeshLibrary"/>), so the size can come from a level key instead of from code building a name:
    /// the component says the size, <see cref="BoxMeshSystem"/> puts the matching mesh on the renderer.
    /// </summary>
    public struct BoxMesh : IComponent
    {
        public Vector3 Center, Size;
        public float UvScale;
        /// <summary>One texture repeat across the width, whatever the size (the level props' convention).</summary>
        public bool FitUvToWidth;
    }

    /// <summary>"I am drawn as a flat plane this wide" — the water and the ground.</summary>
    public struct PlaneMesh : IComponent
    {
        public float Width, Length, UvScale;
    }

    public sealed class BoxMeshConfig : ComponentConfig<BoxMesh>
    {
        public float[] center = { 0, 0, 0 };
        public float[] size = { 1, 1, 1 };
        public float uvScale = 1f;
        public bool fitUvToWidth;
        public override BoxMesh Bake() => new BoxMesh
        {
            Center = ShapeConfigUtil.Vec(center), Size = ShapeConfigUtil.Vec(size, 1f), UvScale = uvScale, FitUvToWidth = fitUvToWidth,
        };
    }

    public sealed class PlaneMeshConfig : ComponentConfig<PlaneMesh>
    {
        /// <summary>"w l", or "w h l" with the height ignored — a plane is flat, and that is how the maps write it.</summary>
        public float[] size = { 1, 1 };
        public float uvScale = 1f;
        public override PlaneMesh Bake() => new PlaneMesh
        {
            Width = ShapeConfigUtil.Wide(size), Length = ShapeConfigUtil.Long(size), UvScale = uvScale,
        };
    }

    /// <summary>Puts the mesh a <see cref="BoxMesh"/> describes on its renderer, as the entity is created.</summary>
    [EntityFilter(typeof(BoxMesh), typeof(MeshRender))]
    [Reads(typeof(BoxMesh)), Writes(typeof(MeshRender))]
    public sealed class BoxMeshSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var b = ctx.World.Get<BoxMesh>(e);
            float uv = b.FitUvToWidth ? 1f / Mathf.Max(b.Size.x, 0.01f) : b.UvScale;
            ctx.World.Get<MeshRender>(e).Mesh = RenderAssets.Meshes.GetOrLoad(MeshLibrary.BoxName(b.Center, b.Size, uv), MeshLibrary.Get);
        }
    }

    /// <summary>The same for a <see cref="PlaneMesh"/>.</summary>
    [EntityFilter(typeof(PlaneMesh), typeof(MeshRender))]
    [Reads(typeof(PlaneMesh)), Writes(typeof(MeshRender))]
    public sealed class PlaneMeshSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var p = ctx.World.Get<PlaneMesh>(e);
            ctx.World.Get<MeshRender>(e).Mesh = RenderAssets.Meshes.GetOrLoad(MeshLibrary.PlaneName(p.Width, p.Length, p.UvScale), MeshLibrary.Get);
        }
    }

    internal static class ShapeConfigUtil
    {
        public static Vector3 Vec(float[] v, float fallback = 0f) =>
            v == null || v.Length < 3 ? new Vector3(fallback, fallback, fallback) : new Vector3(v[0], v[1], v[2]);

        /// <summary>The width of a "w l" / "w h l" size.</summary>
        public static float Wide(float[] v) => v != null && v.Length >= 1 ? v[0] : 1f;

        /// <summary>The length of a "w l" / "w h l" size (the last number either way).</summary>
        public static float Long(float[] v) => v == null || v.Length == 0 ? 1f : v.Length >= 3 ? v[2] : v[v.Length - 1];
    }
}
