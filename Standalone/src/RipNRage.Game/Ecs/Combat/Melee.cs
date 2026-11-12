using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>
    /// The fist combo: two jabs and a finisher. <see cref="Step"/> counts the presses of the current combo, the two
    /// timers are the legacy coroutines (a jab is allowed again almost at once; the combo forgets itself after
    /// 0.75 s), and the fist entities are the viewmodel sprites whose animators take the Jab trigger.
    /// </summary>
    public struct Melee : IComponent
    {
        public int Step;
        /// <summary>Until the next jab may start (the old AllowJab coroutine).</summary>
        public float JabTimer;
        /// <summary>Until the combo resets and the gun comes back (the old ResetJabCounter coroutine).</summary>
        public float ComboTimer;
        /// <summary>A press from the scripted automation; the real input is read from <see cref="InputState"/>.</summary>
        public bool Requested;
        public Entity LeftFist, RightFist;
    }

    /// <summary>
    /// Runs the fist combo on scaled time: a press hides the gun and throws a fist, a landing one shakes the camera
    /// and thumps, and the combo forgets itself if nothing follows. Replaces CombatController's three coroutines and
    /// its _canJab / _comboStepCounter pair. Runs alone (the 1 m probe is a physics query).
    /// </summary>
    [UpdateAfter(typeof(EcsInputSystem))]
    public sealed class MeleeSystem : SystemBase, ITick
    {
        /// <summary>Reach of the jab, and what it can connect with (the prefab's combat layer mask).</summary>
        private const float Reach = 1f;

        private Query _q;
        public override void OnCreate() => _q = World.Query().With<Melee>().With<Weapon>().With<PlayerViewRig>().Build();

        public void Tick(SystemContext ctx)
        {
            var input = World.GetSingleton<InputState>();
            float dt = ctx.DeltaTime;

            foreach (var chunk in _q)
            {
                var melees = chunk.Get<Melee>(); var guns = chunk.Get<Weapon>(); var rigs = chunk.Get<PlayerViewRig>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var m = ref melees[i];
                    ref var gun = ref guns[i];

                    bool pressed = m.Requested || input.AltFirePressed;
                    m.Requested = false;

                    if (m.JabTimer > 0f) m.JabTimer -= dt;
                    if (m.ComboTimer > 0f && (m.ComboTimer -= dt) <= 0f) EndCombo(ref m, ref gun);

                    if (!pressed || m.Step >= 3 || m.JabTimer > 0f) continue;

                    PlayerState.Current?.SetViolentCombatState();
                    m.Step++;
                    if (m.Step < 3) Jab(ref m, ref gun, in rigs[i]);
                    else EndCombo(ref m, ref gun); // the finisher's grapple comes back in 5.5
                }
            }
        }

        private void Jab(ref Melee m, ref Weapon gun, in PlayerViewRig rig)
        {
            // the gun comes off screen while the fists are out
            gun.Holstered = true;
            Hide(gun.Sprite, true);
            Trigger(m.Step == 1 ? m.LeftFist : m.RightFist);

            m.JabTimer = 0.005f;
            m.ComboTimer = 0.75f;

            var eye = World.Get<LocalToWorld>(rig.Camera);
            if (!PhysicsApi.Raycast(eye.Position, eye.Forward, out _, Reach, Weapons.HitMask)) return;
            PlayerCamera.Kick();
            RipNRage.Engine.Ecs.Audio.Sfx.Play(GameAssets.MeleeHit);
        }

        private void EndCombo(ref Melee m, ref Weapon gun)
        {
            m.Step = 0;
            m.ComboTimer = 0f;
            gun.Holstered = false;
            Hide(gun.Sprite, false);
        }

        private void Hide(Entity sprite, bool hidden)
        {
            if (!World.IsAlive(sprite)) return;
            if (hidden) World.Commands.Add(sprite, new Hidden());
            else World.Commands.Remove<Hidden>(sprite);
        }

        private void Trigger(Entity fist)
        {
            if (World.IsAlive(fist) && World.Has<SpriteAnimator>(fist)) World.Get<SpriteAnimator>(fist).SetTrigger("Jab");
        }
    }
}
