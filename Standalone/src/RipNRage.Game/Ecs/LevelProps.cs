using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// "I am made of one of the level's materials" (10.4): a brush texture name is not just a picture — it carries a
    /// tint, sometimes a shader, and the surface a bullet kicks up off it. The component holds which one;
    /// <see cref="LevelTexturedSystem"/> puts it on the renderer and the impact surface as the entity is created, so
    /// a map saying <c>texture orange</c> needs nobody to look three things up for it.
    /// </summary>
    public struct LevelTextured : IComponent
    {
        public int Texture;
    }

    public sealed class LevelTexturedConfig : ComponentConfig<LevelTextured>
    {
        /// <summary>A name from the level texture table ("planks", "orange", "sand"...).</summary>
        public string texture = "grid";
        public override LevelTextured Bake() => new LevelTextured { Texture = LevelTextures.IndexOf(texture) };
    }

    /// <summary>Dresses an entity in a level texture: the picture and tint it draws with, the surface it is made of.</summary>
    [EntityFilter(typeof(LevelTextured))]
    [Reads(typeof(LevelTextured)), Writes(typeof(MeshRender), typeof(ImpactSurface))]
    public sealed class LevelTexturedSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            var def = LevelTextures.At(w.Get<LevelTextured>(e).Texture);
            if (def == null) return;
            if (w.Has<MeshRender>(e))
            {
                ref var r = ref w.Get<MeshRender>(e);
                r.Texture = RenderAssets.Textures.Intern(def.Texture ?? Texture2D.White, def.Texture == null ? "white" : null);
                r.Color = def.tint;
                var material = def.material?.Invoke();
                if (material != null) r.Material = RenderAssets.Materials.Intern(material, material.name);
                if (def.transparent) r.Transparent = true;
            }
            if (w.Has<ImpactSurface>(e)) w.Get<ImpactSurface>(e).Id = Surfaces.Id(def.surface);
        }
    }
}
