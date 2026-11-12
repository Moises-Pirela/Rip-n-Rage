using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Game.Content;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The weapon sway on the arms entity: while the player moves on the ground the arms bob on two sines and roll a
    /// little against the strafe; standing still they ease back home. Ported from the deleted <c>WeaponSway</c>
    /// behaviour, numbers unchanged (the prefab's values are in <see cref="ViewModel.Build"/>).
    /// </summary>
    public struct WeaponSway : IComponent
    {
        public float SwayAmount, SwaySpeed, HorizontalSwayAmount, HorizontalSwaySpeed;
        public float ZSmoothLerp, ZSwayAngle, ZSwayMultiplier, ZSmoothSway;
        /// <summary>Local rest position the sway oscillates around (the prefab's PlayerArms offset).</summary>
        public Vector3 BasePosition;
        internal float Timer, HorizontalTimer, Roll;
    }

    /// <summary>
    /// Applies <see cref="WeaponSway"/>, per frame and before the transforms are built. Grounded comes from the
    /// player's <see cref="CharacterMotor"/> (the sway hangs off the player's camera; there is one player). Writes
    /// another entity's components via the queries' chunks only, but reads the player by lookup, so it declares
    /// nothing and runs alone.
    /// </summary>
    [UpdateBefore(typeof(TransformSystem))]
    public sealed class WeaponSwaySystem : SystemBase, ILateTick
    {
        private Query _sways, _player;

        public override void OnCreate()
        {
            _sways = World.Query().With<WeaponSway>().With<LocalTransform>().Build();
            _player = World.Query().With<PlayerTag>().WithReadOnly<CharacterMotor>().Build();
        }

        public void LateTick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            var input = World.GetSingleton<InputState>();
            bool moving = input.Move.x != 0f || input.Move.y != 0f;

            bool grounded = false;
            foreach (var c in _player)
            {
                var motors = c.Get<CharacterMotor>();
                for (int i = 0; i < c.Length; i++) if (c.Includes(i)) { grounded = motors[i].IsGrounded; break; }
            }

            foreach (var chunk in _sways)
            {
                var sways = chunk.Get<WeaponSway>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var s = ref sways[i];
                    ref var lt = ref xforms[i];

                    if (moving)
                    {
                        s.Timer += dt * s.SwaySpeed;
                        s.HorizontalTimer += dt * s.HorizontalSwaySpeed;
                        float target = Mathf.Clamp(input.Move.x * s.ZSwayMultiplier * Time.smoothDeltaTime * s.ZSmoothSway, -s.ZSwayAngle, s.ZSwayAngle);
                        s.Roll = Mathf.LerpAngle(s.Roll, target, dt * s.ZSmoothLerp);
                        lt.Rotation = Quaternion.Euler(0f, 0f, s.Roll);
                        if (!grounded) continue;
                        lt.Position = new Vector3(
                            s.BasePosition.x + Mathf.Sin(s.HorizontalTimer) * s.HorizontalSwayAmount,
                            s.BasePosition.y + Mathf.Sin(s.Timer) * s.SwayAmount,
                            lt.Position.z);
                    }
                    else
                    {
                        s.Roll = Mathf.LerpAngle(s.Roll, 0f, dt * s.ZSmoothLerp);
                        lt.Rotation = Quaternion.Euler(0f, 0f, s.Roll);
                        if (!grounded) continue;
                        s.Timer = 0f;
                        s.HorizontalTimer = 0f;
                        lt.Position = new Vector3(
                            Mathf.Lerp(lt.Position.x, s.BasePosition.x, dt * s.HorizontalSwaySpeed),
                            Mathf.Lerp(lt.Position.y, s.BasePosition.y, dt * s.SwaySpeed),
                            lt.Position.z);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The first-person viewmodel, as entities under the camera: arms (<see cref="WeaponSway"/>) with the weapon,
    /// muzzle flash and the two fists as children, local offsets from the PlayerCharacter prefab. Built by
    /// <see cref="Content.PlayerPrefab"/> after <see cref="PlayerLink.Attach"/> created the camera rig.
    /// </summary>
    public sealed class ViewModel
    {
        public AnimatedSprite Weapon, MuzzleFlash, LeftFist, RightFist;
        public Entity Arms;

        public static ViewModel Build(World w)
        {
            var vm = new ViewModel();

            // PlayerArms (0.017, -0.65, 2.36) [WeaponSway], child of the camera entity.
            // Created immediately (prefab building is not inside a system), like the AnimatedSprites below: a
            // command-buffered entity is not alive until the next sync point, and anything the build then sets on it
            // through a checked accessor would be dropped.
            Vector3 armsHome = new Vector3(0.017f, -0.65f, 2.36f);
            var arms = w.CreateEntity();
            w.Add(arms, new LocalTransform { Position = armsHome, Rotation = Quaternion.identity, Scale = Vector3.one });
            w.Add(arms, new Parent { Value = PlayerLink.CameraEntity });
            w.Add(arms, new WeaponSway
            {
                SwayAmount = 0.01f, SwaySpeed = 20f, HorizontalSwayAmount = 0.01f, HorizontalSwaySpeed = 10f,
                ZSmoothLerp = 12f, ZSwayAngle = 5f, ZSwayMultiplier = -100f, ZSmoothSway = 20f,
                BasePosition = armsHome,
            });
            vm.Arms = arms;

            // Weapon (scale 0.1); the equipped weapon moves / rescales it per weapon data
            vm.Weapon = new AnimatedSprite(w, GameAssets.Glock["Glock01"], RenderLayer.FirstPerson,
                parent: arms, localScale: Vector3.one * 0.1f);

            // MuzzleFlash (-0.5, 4.5, 0.05) scale 0.5, fullbright, starts hidden (local to the weapon sprite)
            vm.MuzzleFlash = new AnimatedSprite(w, GameAssets.Glock["GlockFlash"], RenderLayer.FirstPerson, fullBright: true,
                parent: vm.Weapon.Entity, localPosition: new Vector3(-0.5f, 4.5f, 0.05f), localScale: Vector3.one * 0.5f);
            vm.MuzzleFlash.SetActive(false);

            // LeftFist (-0.35, 0.6, -0.8) / RightFist (0.35, 0.6, -0.8), scale (0.1, 0.1, 0.075), above the weapon
            vm.LeftFist = new AnimatedSprite(w, GameAssets.Fist["fist_l_01"], RenderLayer.FirstPerson, sortingOrder: 1,
                parent: arms, localPosition: new Vector3(-0.35f, 0.6f, -0.8f), localScale: new Vector3(0.1f, 0.1f, 0.075f));
            vm.LeftFist.runtimeAnimatorController = GameAssets.LeftFistController;

            vm.RightFist = new AnimatedSprite(w, GameAssets.Fist["fist_r_01"], RenderLayer.FirstPerson, sortingOrder: 1,
                parent: arms, localPosition: new Vector3(0.35f, 0.6f, -0.8f), localScale: new Vector3(0.1f, 0.1f, 0.075f));
            vm.RightFist.runtimeAnimatorController = GameAssets.RightFistController;

            return vm;
        }
    }
}
