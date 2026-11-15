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
}
