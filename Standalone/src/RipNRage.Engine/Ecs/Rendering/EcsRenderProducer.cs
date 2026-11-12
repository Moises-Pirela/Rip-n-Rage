using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Ecs.Rendering
{
    /// <summary>
    /// Extracts the frame's draw items from the ECS: the highest-priority camera, every visible sprite, mesh and
    /// light, using their <see cref="LocalToWorld"/> computed by the TransformSystem. Runs on the render thread's
    /// side of the frame (after the world updated); it only reads.
    /// </summary>
    public sealed class EcsRenderProducer : IRenderProducer
    {
        private readonly World _world;
        private readonly Query _cameras, _sprites, _meshes, _lights;
        // property blocks are classes: a pooled set reused every frame, grown once, so SpriteFx costs no allocations
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        private int _blocksUsed;
        private readonly CameraView _view = new CameraView();
        /// <summary>Draw ids from the ECS live in their own range so they never collide with other producers.</summary>
        private const long IdBase = 1L << 62;

        public EcsRenderProducer(World world)
        {
            _world = world;
            _cameras = world.Query().WithReadOnly<CameraLens>().WithReadOnly<LocalToWorld>().Build();
            _sprites = world.Query().WithReadOnly<SpriteRender>().WithReadOnly<LocalToWorld>().Without<Hidden>().Build();
            _meshes = world.Query().WithReadOnly<MeshRender>().WithReadOnly<LocalToWorld>().Without<Hidden>().Build();
            _lights = world.Query().WithReadOnly<LightSource>().WithReadOnly<LocalToWorld>().Without<Hidden>().Build();
        }

        private static long IdOf(Entity e) => IdBase | ((long)e.Generation << 32) | (uint)e.Index;

        /// <summary>The draw's property block for a non-default <see cref="SpriteFx"/> (null when everything is zero).</summary>
        private MaterialPropertyBlock BlockFor(in SpriteFx fx)
        {
            if (fx.Flash <= 0f && fx.Dissolve <= 0f) return null;
            if (_blocksUsed == _blocks.Count) _blocks.Add(new MaterialPropertyBlock());
            var b = _blocks[_blocksUsed++];
            b.SetFloat("_Flash", fx.Flash);
            b.SetColor("_FlashColor", fx.FlashColor);
            b.SetFloat("_Dissolve", fx.Dissolve);
            return b;
        }

        public void Produce(RenderFrame frame)
        {
            _blocksUsed = 0;
            int best = int.MinValue;
            foreach (var c in _cameras)
            {
                var lens = c.Get<CameraLens>(); var ltw = c.Get<LocalToWorld>();
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i) || lens[i].Priority < best) continue;
                    best = lens[i].Priority;
                    _view.Position = ltw[i].Position; _view.Rotation = ltw[i].Rotation;
                    _view.fieldOfView = lens[i].FieldOfView; _view.nearClipPlane = lens[i].Near; _view.farClipPlane = lens[i].Far;
                    _view.backgroundColor = lens[i].Background;
                }
            }
            if (best != int.MinValue) frame.Camera = _view;

            foreach (var c in _sprites)
            {
                var sr = c.Get<SpriteRender>(); var ltw = c.Get<LocalToWorld>(); var ents = c.Entities;
                bool hasFx = c.Has<SpriteFx>();
                var fx = hasFx ? c.Get<SpriteFx>() : default;
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i) || sr[i].Sprite.IsNone || sr[i].Disabled) continue;
                    ref readonly var s = ref sr[i]; ref readonly var w = ref ltw[i];
                    var d = frame.AddSprite();
                    d.Id = IdOf(ents[i]); d.Pose = DrawPose.From(w.Value, w.Position, w.Rotation, w.Scale);
                    d.sprite = RenderAssets.Sprites.Resolve(s.Sprite); d.color = s.Color; d.flipX = s.FlipX; d.flipY = s.FlipY; d.billboard = s.Billboard;
                    d.billboardRoll = s.BillboardRoll; d.lit = s.Lit; d.castsShadow = s.CastsShadow; d.fullBright = s.FullBright;
                    d.sortingOrder = s.SortingOrder; d.renderLayer = s.Layer; d.material = RenderAssets.Materials.Resolve(s.Material);
                    d.propertyBlock = hasFx ? BlockFor(in fx[i]) : null;
                }
            }

            foreach (var c in _meshes)
            {
                var mr = c.Get<MeshRender>(); var ltw = c.Get<LocalToWorld>(); var ents = c.Entities;
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i) || mr[i].Mesh.IsNone) continue;
                    ref readonly var m = ref mr[i]; ref readonly var w = ref ltw[i];
                    var d = frame.AddMesh();
                    d.Id = IdOf(ents[i]); d.Pose = DrawPose.From(w.Value, w.Position, w.Rotation, w.Scale);
                    d.mesh = RenderAssets.Meshes.Resolve(m.Mesh); d.texture = RenderAssets.Textures.Resolve(m.Texture); d.uvScale = m.UvScale; d.emissive = m.Emissive;
                    d.castsShadow = m.CastsShadow; d.transparent = m.Transparent; d.occluder = m.Occluder;
                    d.color = m.Color; d.renderLayer = m.Layer; d.material = RenderAssets.Materials.Resolve(m.Material); d.propertyBlock = null;
                }
            }

            foreach (var c in _lights)
            {
                var l = c.Get<LightSource>(); var ltw = c.Get<LocalToWorld>();
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i)) continue;
                    frame.Lights.Add(new LightDraw { type = l[i].Type, color = l[i].Color, intensity = l[i].Intensity, range = l[i].Range, Position = ltw[i].Position, Forward = ltw[i].Forward });
                }
            }
        }
    }
}
