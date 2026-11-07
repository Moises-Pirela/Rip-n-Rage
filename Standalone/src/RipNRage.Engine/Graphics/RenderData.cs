using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// The camera the renderer draws with: a plain snapshot (pose + projection), independent of where it came from
    /// (an ECS camera entity or, until gameplay is ported, the legacy <see cref="Camera"/> component).
    /// </summary>
    public sealed class CameraView
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float fieldOfView = 60f, nearClipPlane = 0.05f, farClipPlane = 1000f;
        public Color backgroundColor = new Color(0.19f, 0.3f, 0.47f, 1f);

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 Up => Rotation * Vector3.up;
        /// <summary>View matrix (inverse of the rigid camera pose; no scale).</summary>
        public Matrix4x4 worldToCameraMatrix => Matrix4x4.TRS(Position, Rotation, Vector3.one).inverse;
        public Matrix4x4 ProjectionMatrix(float aspect) => Matrix4x4.Perspective(fieldOfView, aspect, nearClipPlane, farClipPlane);

        public Ray ScreenPointToRay(Vector2 screen, int width, int height)
        {
            float aspect = (float)width / height, tanHalf = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float nx = screen.x / width * 2f - 1f, ny = screen.y / height * 2f - 1f;
            return new Ray(Position, Rotation * new Vector3(nx * tanHalf * aspect, ny * tanHalf, 1f).normalized);
        }

        public Vector3 WorldToScreenPoint(Vector3 world, int width, int height)
        {
            var view = worldToCameraMatrix;
            Vector3 ndc = (ProjectionMatrix((float)width / height) * view).MultiplyPoint(world);
            return new Vector3((ndc.x * 0.5f + 0.5f) * width, (ndc.y * 0.5f + 0.5f) * height, view.MultiplyPoint3x4(world).z);
        }
    }

    /// <summary>World pose of a draw item, resolved once by its producer (no transform hierarchy walks while drawing).</summary>
    public struct DrawPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Matrix4x4 LocalToWorld;
        public static DrawPose From(in Matrix4x4 m, Vector3 pos, Quaternion rot, Vector3 scale) =>
            new DrawPose { LocalToWorld = m, Position = pos, Rotation = rot, Scale = scale };
    }

    /// <summary>One sprite to draw this frame.</summary>
    public sealed class SpriteDraw
    {
        /// <summary>Stable across frames for the same source object (caches key on it).</summary>
        public long Id;
        public DrawPose Pose;
        public Sprite sprite;
        public Color color = Color.white;
        public bool flipX, flipY, billboard, lit, castsShadow = true, fullBright;
        public float billboardRoll;
        public int sortingOrder;
        public RenderLayer renderLayer;
        public Material material;
        public MaterialPropertyBlock propertyBlock;

        public Material sharedMaterial => material ?? SpriteRenderer.DefaultSpriteMaterial;
    }

    /// <summary>One mesh to draw this frame.</summary>
    public sealed class MeshDraw
    {
        public long Id;
        public DrawPose Pose;
        public Mesh mesh;
        public Texture2D texture;
        public Vector2 uvScale = Vector2.one;
        public float emissive;
        public bool castsShadow = true, transparent, occluder;
        public Color color = Color.white;
        public RenderLayer renderLayer;
        public Material material;
        public MaterialPropertyBlock propertyBlock;
        internal InstanceGroup instanceGroup;

        public Material sharedMaterial => material ?? (transparent ? MeshRenderer.DefaultTransparentMeshMaterial : MeshRenderer.DefaultMeshMaterial);

        /// <summary>What has to match for two meshes to share one instanced draw.</summary>
        internal readonly struct InstanceKey : IEquatable<InstanceKey>
        {
            public readonly Mesh mesh; public readonly Material material; public readonly Texture2D texture;
            public readonly Color color; public readonly Vector2 uvScale; public readonly float emissive;
            public InstanceKey(Mesh mesh, Material material, Texture2D texture, Color color, Vector2 uvScale, float emissive)
            { this.mesh = mesh; this.material = material; this.texture = texture; this.color = color; this.uvScale = uvScale; this.emissive = emissive; }
            public bool Equals(InstanceKey o) => mesh == o.mesh && material == o.material && texture == o.texture && color.r == o.color.r && color.g == o.color.g && color.b == o.color.b && color.a == o.color.a && uvScale.x == o.uvScale.x && uvScale.y == o.uvScale.y && emissive == o.emissive;
            public override bool Equals(object obj) => obj is InstanceKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(mesh, material, texture, color.r, color.g, uvScale.x, emissive);
        }

        internal sealed class InstanceGroup
        {
            public InstanceKey key;
            public readonly List<MeshDraw> items = new List<MeshDraw>(16);
        }
    }

    public struct LightDraw
    {
        public LightType type;
        public Color color;
        public float intensity, range;
        public Vector3 Position, Forward;
    }

    /// <summary>
    /// Everything the renderer draws in one frame. Producers (the ECS render extraction, and the legacy scene
    /// collector while gameplay still uses GameObjects) fill it; the pipeline only reads it. Draw items are pooled.
    /// </summary>
    public sealed class RenderFrame
    {
        public CameraView Camera;
        public readonly List<SpriteDraw> Sprites = new List<SpriteDraw>();
        public readonly List<MeshDraw> Meshes = new List<MeshDraw>();
        public readonly List<LightDraw> Lights = new List<LightDraw>();
        public readonly List<IUiDrawer> UiDrawers = new List<IUiDrawer>();

        private readonly List<SpriteDraw> _spritePool = new List<SpriteDraw>();
        private readonly List<MeshDraw> _meshPool = new List<MeshDraw>();
        private int _spriteUsed, _meshUsed;

        internal void Reset()
        {
            Camera = null;
            Sprites.Clear(); Meshes.Clear(); Lights.Clear(); UiDrawers.Clear();
            _spriteUsed = 0; _meshUsed = 0;
        }

        /// <summary>A pooled sprite item, added to <see cref="Sprites"/>; every field must be set by the producer.</summary>
        public SpriteDraw AddSprite()
        {
            if (_spriteUsed == _spritePool.Count) _spritePool.Add(new SpriteDraw());
            var s = _spritePool[_spriteUsed++];
            Sprites.Add(s);
            return s;
        }

        public MeshDraw AddMesh()
        {
            if (_meshUsed == _meshPool.Count) _meshPool.Add(new MeshDraw());
            var m = _meshPool[_meshUsed++];
            m.instanceGroup = null;
            Meshes.Add(m);
            return m;
        }
    }

    /// <summary>A source of draw items (ECS extraction, legacy scene). Producers run in registration order.</summary>
    public interface IRenderProducer
    {
        void Produce(RenderFrame frame);
    }
}
