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

        /// <summary>A box mesh, built once per centre and size.</summary>
        public static AssetHandle<Mesh> Box(Vector3 center, Vector3 size) =>
            RenderAssets.Meshes.GetOrLoad(FormattableString.Invariant($"box:{center.x},{center.y},{center.z}:{size.x},{size.y},{size.z}"),
                _ => Mesh.CreateBox(center, size, 1f));

        /// <summary>A flat plane mesh, built once per extent and UV scale.</summary>
        public static AssetHandle<Mesh> Plane(float width, float length, float uvScale) =>
            RenderAssets.Meshes.GetOrLoad(FormattableString.Invariant($"plane:{width}x{length}@{uvScale}"),
                _ => Mesh.CreatePlane(Vector3.zero, width, length, uvScale));

        /// <summary>A textured mesh's render data (the legacy MeshRenderer's defaults: white, shadow-casting, opaque).</summary>
        public static MeshRender Render(AssetHandle<Mesh> mesh, Texture2D texture, Color color, Material material = null,
                                      float emissive = 0f, bool castsShadow = true, bool transparent = false, bool occluder = false) =>
            new MeshRender
            {
                Mesh = mesh, Texture = RenderAssets.Textures.Intern(texture), UvScale = Vector2.one, Emissive = emissive,
                CastsShadow = castsShadow, Transparent = transparent, Occluder = occluder, Color = color,
                Layer = RenderLayer.World, Material = RenderAssets.Materials.Intern(material),
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
}
