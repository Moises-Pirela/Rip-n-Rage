namespace RipNRage.Engine.Graphics
{
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
        /// <summary>Rotation of the sky (degrees) — driven by the game's SkySpinSystem.</summary>
        public static float skyboxRotation;
    }

    /// <summary>The engine-default materials a draw falls back to when none is assigned (9.7: were statics on the
    /// deleted SpriteRenderer / MeshRenderer components).</summary>
    public static class DefaultMaterials
    {
        private static Material _sprite, _mesh, _meshTransparent;

        public static Material Sprite => _sprite ??= new Material(ShaderLibrary.Get("sprite"), "Sprite-Default")
        {
            blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000,
        };
        public static Material Mesh => _mesh ??= new Material(ShaderLibrary.Get("mesh"), "Mesh-Default");
        public static Material MeshTransparent => _meshTransparent ??= new Material(ShaderLibrary.Get("mesh"), "Mesh-Transparent")
        {
            blend = BlendMode.AlphaBlend, cull = CullMode.Off, depthWrite = false, renderQueue = 3000,
        };
    }
}
