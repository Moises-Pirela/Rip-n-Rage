using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The player's view rig, as entities: the <see cref="Pivot"/> (the prefab's "Camera" node — head bob and the
    /// look angles) and its <see cref="Camera"/> child (the prefab's "WorldCamera" — the lens and the recoil / trauma
    /// shake). Sits on the player's character entity, which is their parent chain's root.
    ///
    /// <para>The camera is the one the renderer uses: an ECS <see cref="CameraLens"/> outranks the legacy
    /// <c>Camera.main</c>, which now only follows along for the scripts layers 5–8 still own.</para>
    /// </summary>
    public struct PlayerViewRig : IComponent
    {
        public Entity Pivot, Camera;
        /// <summary>
        /// Something other than <see cref="PlayerViewSystem"/> owns the pivot's pose (the death sequence drives it
        /// directly). While this is set the look angles, the bob and the shake are left alone.
        /// </summary>
        public bool External;
    }

    /// <summary>
    /// Drives the view rig from the player's state, once per frame and before the transforms are built: the pivot
    /// takes the <see cref="PlayerLook"/> angles, and the head bob is told whether the player is on the ground, is
    /// being asked to move, and how far the slide wants the camera dropped.
    ///
    /// <para>It writes components on other entities (the rig's two children), so it declares nothing and the
    /// scheduler runs it alone.</para>
    /// </summary>
    [UpdateAfter(typeof(PlayerLookSystem)), UpdateBefore(typeof(CameraShakeSystem))]
    public sealed class PlayerViewSystem : SystemBase, ILateTick
    {
        private Query _q;
        public override void OnCreate() =>
            _q = World.Query().With<PlayerViewRig>().With<PlayerLook>().With<PlayerMotor>().With<CharacterMotor>().Build();

        public void LateTick(SystemContext ctx)
        {
            var input = World.GetSingleton<InputState>();
            bool moving = input.Move.x != 0f || input.Move.y != 0f;

            foreach (var chunk in _q)
            {
                var rigs = chunk.Get<PlayerViewRig>(); var looks = chunk.Get<PlayerLook>();
                var motors = chunk.Get<PlayerMotor>(); var characters = chunk.Get<CharacterMotor>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref readonly var rig = ref rigs[i];
                    if (rig.External) continue;

                    // the pivot carries the view angles (the old cameraPivot.localRotation)
                    if (World.IsAlive(rig.Pivot) && World.Has<LocalTransform>(rig.Pivot))
                    {
                        ref readonly var look = ref looks[i];
                        World.Get<LocalTransform>(rig.Pivot).Rotation = Quaternion.Euler(look.Pitch, look.Yaw, look.Roll);
                    }

                    // the bob only runs on the ground, and follows the slide's camera drop
                    if (World.IsAlive(rig.Pivot) && World.Has<ViewBob>(rig.Pivot))
                    {
                        ref var bob = ref World.Get<ViewBob>(rig.Pivot);
                        bob.Active = characters[i].IsGrounded;
                        bob.Moving = moving;
                        bob.HeightOffset = motors[i].CameraHeightOffset;
                    }
                }
            }
        }
    }

    /// <summary>
    /// MIGRATION BRIDGE (gone in layer 9): copies the view rig's local poses onto the prefab's "Camera" /
    /// "WorldCamera" GameObjects at the end of the ECS frame — after the bob and the shake have been applied and
    /// before the frame is rendered — so <c>Camera.main.transform</c> is the same pose the renderer uses this frame,
    /// not last frame's. The nodes are children of the player GameObject, which <see cref="PlayerLink"/> already keeps
    /// on the body, so the local pose is what has to be mirrored.
    /// </summary>
    [UpdateAfter(typeof(CameraShakeSystem))]
    public sealed class PlayerViewFollowSystem : SystemBase, ILateTick
    {
        public void LateTick(SystemContext ctx)
        {
            var link = PlayerLink.Current;
            if (link == null || link.IsDestroyed) return;
            Follow(link.cameraPivot, PlayerLink.PivotEntity, ctx.World);
            Follow(link.worldCamera, PlayerLink.CameraEntity, ctx.World);
        }

        private static void Follow(Transform t, Entity e, World w)
        {
            if (t == null || !w.IsAlive(e) || !w.Has<LocalTransform>(e)) return;
            ref readonly var lt = ref w.Get<LocalTransform>(e);
            t.localPosition = lt.Position;
            t.localRotation = lt.Rotation;
        }
    }

    /// <summary>
    /// Where gameplay reaches the player's camera: the shake on the camera entity, and the rig's own switches.
    /// <c>CameraRecoil</c> is the facade the unported weapon / explosion scripts still call; this is the direct path.
    /// </summary>
    public static class PlayerCamera
    {
        private static World W => Application.Current?.World;

        /// <summary>The camera entity, or <see cref="Entity.Null"/> when there is no player.</summary>
        public static Entity Entity => PlayerLink.CameraEntity;

        public static bool Has => W != null && W.IsAlive(PlayerLink.CameraEntity) && W.Has<CameraShake>(PlayerLink.CameraEntity);

        /// <summary>The camera's shake, by reference (check <see cref="Has"/> first).</summary>
        public static ref CameraShake Shake => ref W.Get<CameraShake>(PlayerLink.CameraEntity);

        /// <summary>World position of the camera this frame (the pose the renderer used).</summary>
        public static Vector3 Position =>
            W != null && W.IsAlive(PlayerLink.CameraEntity) && W.Has<LocalToWorld>(PlayerLink.CameraEntity)
                ? W.Get<LocalToWorld>(PlayerLink.CameraEntity).Position : Vector3.zero;

        /// <summary>Hands the pivot over to something else (the death sequence) or back to <see cref="PlayerViewSystem"/>.</summary>
        public static void SetExternal(bool external)
        {
            var p = PlayerLink.PlayerEntity;
            if (W == null || !W.IsAlive(p) || !W.Has<PlayerViewRig>(p)) return;
            W.Get<PlayerViewRig>(p).External = external;
        }
    }
}
