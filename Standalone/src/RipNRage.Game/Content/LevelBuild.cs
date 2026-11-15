using RipNRage.Engine.Ecs;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Builds a scene from a .map level (9.0; was <c>TestArea</c>, whose hand-made dock arena is gone — a level is
    /// required now): the assets and the layer matrix, the level itself (brushes, entities, the kill height, the sky),
    /// then the player at the level's start — or, for the title, the menu camera there instead.
    /// </summary>
    public static class LevelBuild
    {
        /// <summary>ProjectSettings/DynamicsManager.asset m_LayerCollisionMatrix (row = layer, bit = other layer).</summary>
        private static readonly uint[] LayerCollisionMatrix =
        {
            0xffffffff, 0xffff3fbf, 0xffff3fbf, 0xffff7bf7, 0xffff3fff, 0xffff3fbf, 0xffff1119, 0xffff303f,
            0xffff3f7f, 0xffff3f3f, 0xffff3f37, 0xffff3f3f, 0xffff3fff, 0xffff5fbf, 0xffff2009, 0xffff0001,
        };

        private static World World => Application.Current.World;

        /// <summary>Builds <paramref name="levelPath"/>. With <paramref name="spawnPlayer"/> false there is no player; a
        /// menu camera entity looks over the level from the player start (the title's backdrop).</summary>
        public static void Build(string levelPath, bool spawnPlayer = true)
        {
            GameAssets.Load();
            Materials.Load();
            Physics.SetCollisionMatrix(LayerCollisionMatrix);
            // particles collide with the world, not with actors (9.4: was the VfxRuntime MonoBehaviour's Awake)
            RipNRage.Engine.Graphics.Particles.ParticleWorld.CollisionMask =
                ~((1 << Layers.Player) | (1 << Layers.Enemies) | (1 << Layers.Projectile) | (1 << Layers.VisionSensor) | (1 << Layers.Pickup) | (1 << Layers.IgnoreRaycast));
            // sun, sky, fog and shadows come from tunables.cfg ([lighting] / [sky] / [fog] / [shadows] / [volumetric_fog])

            var level = LevelLoader.Load(levelPath);
            Debug.LogWarning($"Level: {levelPath} ({level.document.Brushes.Count} brushes, {level.document.entities.Count} entities)");
            RipNRage.Game.Ecs.Sky.Spin(World, 1.2f);
            World.Singleton<RipNRage.Game.Ecs.KillHeight>().Y = level.killY;
            if (spawnPlayer) PlayerPrefab.Spawn(level.playerStart, level.playerRotation.eulerAngles.y);
            else RipNRage.Game.Ecs.MenuViews.Spawn(World, level.playerStart, level.playerRotation); // 9.3: an entity
        }
    }
}
