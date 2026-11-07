using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// TEMPORARY bridge for the ECS migration: turns the GameObject scene's renderers, lights and camera into draw
    /// items, so gameplay that is not ported yet keeps rendering. Delete once nothing uses the
    /// SpriteRenderer / MeshRenderer / Light / Camera components.
    /// </summary>
    public sealed class LegacySceneProducer : IRenderProducer
    {
        private readonly System.Func<Scene> _scene;
        private readonly ConditionalWeakTable<object, StrongBox<long>> _ids = new ConditionalWeakTable<object, StrongBox<long>>();
        private long _nextId = 1;
        private readonly CameraView _camera = new CameraView();

        public LegacySceneProducer(System.Func<Scene> scene) { _scene = scene; }

        private long IdOf(object o)
        {
            if (!_ids.TryGetValue(o, out var box)) { box = new StrongBox<long>(_nextId++); _ids.Add(o, box); }
            return box.Value;
        }

        public void Produce(RenderFrame frame)
        {
            var scene = _scene();
            var cam = Camera.main;
            if (frame.Camera == null && cam != null && !cam.destroyed)
            {
                _camera.Position = cam.transform.position; _camera.Rotation = cam.transform.rotation;
                _camera.fieldOfView = cam.fieldOfView; _camera.nearClipPlane = cam.nearClipPlane; _camera.farClipPlane = cam.farClipPlane;
                _camera.backgroundColor = cam.backgroundColor;
                frame.Camera = _camera;
            }
            foreach (var l in Light.Lights)
            {
                if (l.destroyed || !l.isActiveAndEnabled) continue;
                frame.Lights.Add(new LightDraw { type = l.type, color = l.color, intensity = l.intensity, range = l.range, Position = l.transform.position, Forward = l.transform.forward });
            }
            if (scene == null) return;
            foreach (var go in scene.GameObjects)
            {
                if (go.destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.Components)
                {
                    if (c.destroyed) continue;
                    if (c is Behaviour b && !b.enabled) continue;
                    switch (c)
                    {
                        case SpriteRenderer sr when sr.sprite != null:
                        {
                            var d = frame.AddSprite();
                            var t = sr.transform;
                            d.Id = IdOf(sr); d.Pose = DrawPose.From(t.localToWorldMatrix, t.position, t.rotation, t.lossyScale);
                            d.sprite = sr.sprite; d.color = sr.color; d.flipX = sr.flipX; d.flipY = sr.flipY;
                            d.billboard = sr.billboard; d.billboardRoll = sr.billboardRoll; d.lit = sr.lit; d.castsShadow = sr.castsShadow;
                            d.fullBright = sr.fullBright; d.sortingOrder = sr.sortingOrder; d.renderLayer = sr.renderLayer;
                            d.material = sr.material; d.propertyBlock = sr.propertyBlock;
                            break;
                        }
                        case MeshRenderer mr:
                        {
                            var d = frame.AddMesh();
                            var t = mr.transform;
                            d.Id = IdOf(mr); d.Pose = DrawPose.From(t.localToWorldMatrix, t.position, t.rotation, t.lossyScale);
                            d.mesh = mr.mesh; d.texture = mr.texture; d.uvScale = mr.uvScale; d.emissive = mr.emissive;
                            d.castsShadow = mr.castsShadow; d.transparent = mr.transparent; d.occluder = mr.occluder;
                            d.color = mr.color; d.renderLayer = mr.renderLayer; d.material = mr.material; d.propertyBlock = mr.propertyBlock;
                            break;
                        }
                    }
                    if (c is IUiDrawer ui) frame.UiDrawers.Add(ui);
                }
            }
        }
    }
}
