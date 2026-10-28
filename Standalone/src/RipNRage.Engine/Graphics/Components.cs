using System.Collections.Generic;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Which pass a renderer is drawn in.</summary>
    public enum RenderLayer
    {
        /// <summary>Normal 3D world pass (depth tested).</summary>
        World = 0,
        /// <summary>Drawn after the world with a cleared depth buffer: first-person weapon/hands.</summary>
        FirstPerson = 1,
    }

    /// <summary>Base for anything the render pipeline can draw.</summary>
    public abstract class Renderer : Behaviour
    {
        public RenderLayer renderLayer = RenderLayer.World;
        public int sortingOrder;
        public Color color = Color.white;
        /// <summary>Material (shader + properties + state). Null = the engine default for this renderer type.</summary>
        public Material material;
        /// <summary>Per-object property overrides applied on top of the material.</summary>
        public MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        /// <summary>Material used for drawing (the default when none is assigned).</summary>
        public Material sharedMaterial => material ?? DefaultMaterial;
        protected abstract Material DefaultMaterial { get; }

        protected override void Update() { }
    }

    /// <summary>
    /// Draws a <see cref="Sprite"/> as a quad in the transform's XY plane, exactly like
    /// UnityEngine.SpriteRenderer (pivot-anchored, scaled by pixelsPerUnit and the transform).
    /// </summary>
    public sealed class SpriteRenderer : Renderer
    {
        public Sprite sprite;
        public bool flipX;
        public bool flipY;
        /// <summary>When true the quad is rotated around Y each frame to face the active camera (Doom-style enemies/pickups).</summary>
        public bool billboard;
        /// <summary>Lit sprites receive sun/point lights and shadows; unlit ones are drawn at full brightness (first person).</summary>
        public bool lit;
        /// <summary>World sprites are rendered into the directional shadow map (alpha-tested billboard).</summary>
        public bool castsShadow = true;
        /// <summary>Never shaded, even in the lit viewmodel pass (muzzle flashes, glows).</summary>
        public bool fullBright;

        public SpriteRenderer() { }

        private static Material _default;
        /// <summary>Engine default sprite material ("sprite" shader, alpha blended, transparent queue).</summary>
        public static Material DefaultSpriteMaterial => _default ??= new Material(ShaderLibrary.Get("sprite"), "Sprite-Default")
        {
            blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000,
        };
        protected override Material DefaultMaterial => DefaultSpriteMaterial;
    }

    /// <summary>Draws a static <see cref="Mesh"/> with a texture.</summary>
    public sealed class MeshRenderer : Renderer
    {
        public Mesh mesh;
        public Texture2D texture;
        public Vector2 uvScale = Vector2.one;
        /// <summary>Self-illumination added on top of the lighting (lamp heads, screens).</summary>
        public float emissive = 0f;
        public bool castsShadow = true;
        /// <summary>Alpha-blended, drawn after opaque geometry without depth writes (water).</summary>
        public bool transparent;
        /// <summary>Rasterised into the software occlusion buffer (static level geometry). Dynamic props should leave this off.</summary>
        public bool occluder;

        private static Material _default, _defaultTransparent;
        public static Material DefaultMeshMaterial => _default ??= new Material(ShaderLibrary.Get("mesh"), "Mesh-Default");
        public static Material DefaultTransparentMeshMaterial => _defaultTransparent ??= new Material(ShaderLibrary.Get("mesh"), "Mesh-Transparent")
        {
            blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000,
        };
        protected override Material DefaultMaterial => transparent ? DefaultTransparentMeshMaterial : DefaultMeshMaterial;
    }

    /// <summary>Perspective camera (UnityEngine.Camera subset). Looks down its transform's +Z axis.</summary>
    public sealed class Camera : Behaviour
    {
        public static Camera main { get; set; }

        public float fieldOfView = 60f;
        public float nearClipPlane = 0.05f;
        public float farClipPlane = 1000f;
        public Color backgroundColor = new Color(0.19f, 0.3f, 0.47f, 1f);
        public int depth;

        protected override void Awake()
        {
            if (main == null) main = this;
        }

        public Matrix4x4 worldToCameraMatrix => transform.worldToLocalMatrix;

        public Matrix4x4 ProjectionMatrix(float aspect) => Matrix4x4.Perspective(fieldOfView, aspect, nearClipPlane, farClipPlane);

        public Ray ScreenPointToRay(Vector2 screen, int width, int height)
        {
            float aspect = (float)width / height;
            float tanHalf = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float nx = (screen.x / width) * 2f - 1f;
            float ny = (screen.y / height) * 2f - 1f;
            Vector3 dirLocal = new Vector3(nx * tanHalf * aspect, ny * tanHalf, 1f).normalized;
            return new Ray(transform.position, transform.rotation * dirLocal);
        }

        /// <summary>Projects a world point to pixel coordinates (origin bottom-left). z = depth.</summary>
        public Vector3 WorldToScreenPoint(Vector3 world, int width, int height)
        {
            Matrix4x4 vp = ProjectionMatrix((float)width / height) * worldToCameraMatrix;
            Vector3 ndc = vp.MultiplyPoint(world);
            Vector3 local = worldToCameraMatrix.MultiplyPoint3x4(world);
            return new Vector3((ndc.x * 0.5f + 0.5f) * width, (ndc.y * 0.5f + 0.5f) * height, local.z);
        }
    }

    /// <summary>Scene-wide lighting / atmosphere (UnityEngine.RenderSettings subset).</summary>
    public static class RenderSettings
    {
        public static Vector3 sunDirection = new Vector3(0.3f, -0.8f, 0.5f).normalized;
        public static Color ambientLight = new Color(0.45f, 0.47f, 0.55f, 1f);
        public static Color fogColor = new Color(0.62f, 0.72f, 0.85f, 1f);
        public static float fogStartDistance = 60f;
        public static float fogEndDistance = 220f;
        public static Color skyZenith = new Color(0.20f, 0.42f, 0.80f, 1f);
        public static Color skyHorizon = new Color(0.70f, 0.80f, 0.92f, 1f);
        public static Color skyGround = new Color(0.20f, 0.22f, 0.25f, 1f);
        public static bool drawSky = true;
        /// <summary>Rotation of the sky (degrees) — driven by the game's SkyboxRotator.</summary>
        public static float skyboxRotation;
    }
}
