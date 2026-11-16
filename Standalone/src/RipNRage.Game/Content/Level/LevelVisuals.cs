using System;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Game.Content.Level
{
    /// <summary>
    /// What a level looks like, as entities (9.1; the brushes and the light / lamp / plane classes were GameObjects with
    /// renderer components). Render entities are built immediately — level build, not inside a system — so the loader
    /// and the classes can parent to them at once.
    /// <para>Meshes are interned under a name that says what they are (a box's size, a plane's extent, a level chunk's
    /// file + timestamp + index): the asset registry is append-only, so a level rebuilt on every RESTART / respawn /
    /// load must reuse its meshes, not intern new copies.</para>
    /// </summary>
    public static class LevelVisuals
    {
        private static World W => Application.Current.World;

        /// <summary>A box mesh, built once per centre, size and UV scale — under the <see cref="MeshLibrary"/> name a prefab writes.</summary>
        public static AssetHandle<Mesh> Box(Vector3 center, Vector3 size, float uvScale = 1f) =>
            RenderAssets.Meshes.GetOrLoad(MeshLibrary.BoxName(center, size, uvScale), MeshLibrary.Get);

        /// <summary>A flat plane mesh, built once per extent and UV scale (the same rule).</summary>
        public static AssetHandle<Mesh> Plane(float width, float length, float uvScale) =>
            RenderAssets.Meshes.GetOrLoad(MeshLibrary.PlaneName(width, length, uvScale), MeshLibrary.Get);

        /// <summary>A textured mesh's render data (the legacy MeshRenderer's defaults: white, shadow-casting, opaque).</summary>
        public static MeshRender Render(AssetHandle<Mesh> mesh, Texture2D texture, Color color, Material material = null,
                                      float emissive = 0f, bool castsShadow = true, bool transparent = false, bool occluder = false) =>
            new MeshRender
            {
                Mesh = mesh, Texture = RenderAssets.Textures.Intern(texture, ReferenceEquals(texture, Texture2D.White) ? "white" : null), // "white" is the name MeshRenderConfig gives it
                UvScale = Vector2.one, Emissive = emissive,
                CastsShadow = castsShadow, Transparent = transparent, Occluder = occluder, Color = color,
                Layer = RenderLayer.World, Material = RenderAssets.Materials.Intern(material, material?.name),
            };

        /// <summary>An entity at <paramref name="position"/> (under <paramref name="parent"/> when given) drawing <paramref name="render"/>.</summary>
        public static Entity Spawn(Vector3 position, in MeshRender render, Entity parent = default)
        {
            var e = W.CreateEntity();
            W.Add(e, LocalTransform.At(position));
            if (!parent.IsNull) W.Add(e, new Parent { Value = parent });
            W.Add(e, render);
            return e;
        }

        /// <summary>A point light entity.</summary>
        public static Entity PointLight(Vector3 position, Color color, float intensity, float range, Entity parent = default)
        {
            var e = W.CreateEntity();
            W.Add(e, LocalTransform.At(position));
            if (!parent.IsNull) W.Add(e, new Parent { Value = parent });
            W.Add(e, new LightSource { Type = LightType.Point, Color = color, Intensity = intensity, Range = range });
            return e;
        }
    }

    /// <summary>
    /// A thin bar stretched from a fixed hook to a hanging prop every frame (9.2: the pendulum / spring rod — was the
    /// <c>RodFollower</c> MonoBehaviour). Visual only; the joint is the physics.
    /// </summary>
    public struct RodVisual : IComponent
    {
        public Entity Target;
        public Vector3 Hook;
        public float Width;
    }

    /// <summary>Stretches every <see cref="RodVisual"/> after the frame's motion (the prop moved under physics).</summary>
    [Writes(typeof(LocalTransform)), Reads(typeof(RodVisual))] // its own transform by query; the target's is read ad hoc
    public sealed class RodSystem : SystemBase, ILateTick
    {
        private Query _rods;

        public override void OnCreate() => _rods = World.Query().WithReadOnly<RodVisual>().With<LocalTransform>().Build();

        public void LateTick(SystemContext ctx)
        {
            var w = ctx.World;
            foreach (var chunk in _rods)
            {
                var rods = chunk.Get<RodVisual>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref readonly var r = ref rods[i];
                    if (!w.IsAlive(r.Target) || !w.Has<LocalTransform>(r.Target)) continue;
                    Place(ref xforms[i], w.Get<LocalTransform>(r.Target).Position, r.Hook, r.Width);
                }
            }
        }

        /// <summary>The rod's pose for a span (the unit box reaches +Y, so it stands on <paramref name="from"/>).</summary>
        public static void Place(ref LocalTransform rod, Vector3 from, Vector3 to, float width)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return;
            rod.Rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            rod.Position = from;
            rod.Scale = new Vector3(width, len, width);
        }
    }
}