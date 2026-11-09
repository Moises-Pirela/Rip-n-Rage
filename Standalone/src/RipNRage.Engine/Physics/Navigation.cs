using System.Collections.Generic;

namespace RipNRage.Engine.AI
{
    public struct NavMeshHit
    {
        public Vector3 position;
        public bool hit;
    }

    public sealed class NavMeshPath
    {
        public Vector3[] corners = System.Array.Empty<Vector3>();
    }

    /// <summary>
    /// Replacement for Unity's NavMesh queries. Without a baked navmesh, positions are sampled by
    /// dropping a ray onto the level geometry so AI wander targets land on walkable ground.
    /// </summary>
    public static class NavMesh
    {
        public const int AllAreas = -1;

        public static bool SamplePosition(Vector3 source, out NavMeshHit hit, float maxDistance, int areaMask)
        {
            hit = new NavMeshHit { position = source, hit = false };
            if (Physics.Raycast(source + Vector3.up * maxDistance, Vector3.down, out var rh, maxDistance * 2f, ~(1 << Layers.Player | 1 << Layers.Enemies)))
            {
                hit.position = rh.point;
                hit.hit = true;
                return true;
            }
            return false;
        }
    }
}
