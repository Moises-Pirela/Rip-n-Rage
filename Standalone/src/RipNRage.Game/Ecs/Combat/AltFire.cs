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
    /// <summary>What the third press of a weapon's alt-fire does. Declared by the weapon's data; the move itself runs
    /// as a finisher entity (see <see cref="AltFireSystem.SpawnFinisher"/>).</summary>
    public enum FinisherKind { None, Grapple, PointBlankShot, Bash }

    /// <summary>
    /// The alt-fire combo: press 1 and 2 throw the fists, press 3 is the equipped weapon's finisher. The two timers
    /// are the legacy coroutines (a jab is allowed again almost at once; the combo forgets itself after 0.75 s). While
    /// <see cref="Finisher"/> names a live entity the combo is in its finisher and further presses are ignored.
    /// </summary>
    public struct AltFire : IComponent
    {
        public int Step;
        /// <summary>Until the next jab may start (the old AllowJab coroutine).</summary>
        public float JabTimer;
        /// <summary>Until an unfinished combo forgets itself (the old ResetJabCounter coroutine).</summary>
        public float ComboTimer;
        /// <summary>A press from the scripted automation; the real input is read from <see cref="InputState"/>.</summary>
        public bool Requested;
        public Entity LeftFist, RightFist;
        /// <summary>The finisher entity in progress (Null = none). Parented to the player: it is the player's move,
        /// and the hierarchy tears it down with them.</summary>
        public Entity Finisher;
    }

    /// <summary>
    /// Runs the combo on scaled time: a press hides the gun and throws a fist, a landing jab shakes the camera and
    /// thumps, an unfinished combo forgets itself — and the third press spawns the weapon's finisher entity, whose
    /// own system takes it from there. Runs alone (the 1 m probe is a physics query).
    /// </summary>
    [UpdateAfter(typeof(EcsInputSystem))]
    [Exclusive] // the viewmodel GameObjects
    public sealed class AltFireSystem : SystemBase, ITick
    {
        /// <summary>Reach of a jab and of the finisher's target probe.</summary>
        public const float Reach = 1f;

        private Query _q;
        public override void OnCreate() => _q = World.Query().With<AltFire>().With<Weapon>().With<PlayerViewRig>().Build();

        public void Tick(SystemContext ctx)
        {
            var input = World.GetSingleton<InputState>();
            float dt = ctx.DeltaTime;

            foreach (var chunk in _q)
            {
                var combos = chunk.Get<AltFire>(); var guns = chunk.Get<Weapon>(); var rigs = chunk.Get<PlayerViewRig>();
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var m = ref combos[i];
                    ref var gun = ref guns[i];

                    bool pressed = m.Requested || input.AltFirePressed;
                    m.Requested = false;

                    if (!m.Finisher.IsNull) continue; // the finisher's own system owns the combo until it ends
                    if (m.JabTimer > 0f) m.JabTimer -= dt;
                    if (m.ComboTimer > 0f && (m.ComboTimer -= dt) <= 0f) Finishers.EndCombo(World, ref m, ref gun);

                    if (!pressed || m.Step >= 3 || m.JabTimer > 0f) continue;

                    m.Step++;
                    if (m.Step < 3) Jab(ctx, ents[i], ref m, ref gun, in rigs[i]);
                    else SpawnFinisher(ctx, ents[i], ref m, ref gun, in rigs[i]);
                }
            }
        }

        private void Jab(SystemContext ctx, Entity player, ref AltFire m, ref Weapon gun, in PlayerViewRig rig)
        {
            // the gun comes off screen while the fists are out
            gun.Holstered = true;
            Finishers.ShowGun(World, gun.Sprite, false);
            var fist = m.Step == 1 ? m.LeftFist : m.RightFist;
            if (World.IsAlive(fist) && World.Has<SpriteAnimator>(fist)) World.Get<SpriteAnimator>(fist).SetTrigger("Jab");

            m.JabTimer = 0.005f;
            m.ComboTimer = 0.75f;

            var struck = Probe(in rig, out var hit);
            if (struck.IsNull) return;
            PlayerCamera.Kick();
            RipNRage.Engine.Ecs.Audio.Sfx.Play(GameAssets.MeleeHit);
            // 11.16: the jab is a blow. It never was - layer 9's "still no melee damage path" - the fist kicked the
            // camera and played a sound. Fury's melee bonus (Rages.DamageDealt with melee) applies to this.
            ctx.Post(new DamageEvent
            {
                Instigator = player, Target = struck, Amount = JabDamage, Melee = true,
                Point = hit.point, Direction = hit.normal == Vector3.zero ? Vector3.forward : -hit.normal, Impulse = 8f,
            });
        }

        /// <summary>
        /// The third press becomes an entity: parented to the player, carrying the one component the weapon's data
        /// asks for, driven by that component's system. A grapple with nobody in reach spawns nothing and the combo
        /// just resets — the legacy rule.
        /// </summary>
        private void SpawnFinisher(SystemContext ctx, Entity player, ref AltFire m, ref Weapon gun, in PlayerViewRig rig)
        {
            var kind = Weapons.Data(gun.Slot).finisher;
            Entity target = kind == FinisherKind.Grapple ? Probe(in rig) : Entity.Null;
            if (kind == FinisherKind.None || (kind == FinisherKind.Grapple && target.IsNull))
            {
                Finishers.EndCombo(World, ref m, ref gun);
                return;
            }

            var e = ctx.Commands.CreateEntity();
            ctx.Commands.Add(e, new Parent { Value = player });
            switch (kind)
            {
                case FinisherKind.Grapple: ctx.Commands.Add(e, new GrappleFinisher { Target = target }); break;
                case FinisherKind.PointBlankShot: ctx.Commands.Add(e, new PointBlankFinisher()); break;
                case FinisherKind.Bash: ctx.Commands.Add(e, new BashFinisher()); break;
            }
            m.Finisher = e;
            m.ComboTimer = 0f; // the finisher owns the combo's end now
        }

        /// <summary>Whatever damageable thing stands within arm's reach of the camera.</summary>
        /// <summary>What one jab does to what it reaches.</summary>
        public const float JabDamage = 20f;

        private Entity Probe(in PlayerViewRig rig) => Probe(in rig, out _);

        private Entity Probe(in PlayerViewRig rig, out RipNRage.Engine.RaycastHit hit)
        {
            var eye = World.Get<LocalToWorld>(rig.Camera);
            return PhysicsApi.Raycast(eye.Position, eye.Forward, out hit, Reach, Weapons.HitMask)
                   && World.Has<Health>(hit.entity) && !World.Has<Dead>(hit.entity) ? hit.entity : Entity.Null;
        }
    }
}
