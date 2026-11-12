using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs.Combat
{
    // ================================================================= the finisher components
    // A finisher in progress is an entity parented to the player, carrying exactly one of these. Each component's
    // system plays the move the deleted Weapon subclasses carried, against the player's components — the weapon is
    // never called, only asked (Weapon.ForcedShots) or written (the animator bools).

    /// <summary>The Glock execution: snap the view to the grabbed target, the left fist holds it, two point-blank
    /// shots through the normal fire path, release.</summary>
    public struct GrappleFinisher : IComponent
    {
        public Entity Target;
        public float Age;
        public bool Fired;
        /// <summary>When the shots are done, the grip holds a beat before letting go (the legacy 0.25 s).</summary>
        public float ReleaseAt;
    }

    /// <summary>The M1887 execution: the swing telegraphs for half a second, then one shell at arm's length.</summary>
    public struct PointBlankFinisher : IComponent
    {
        public float Age;
        public bool Fired;
    }

    /// <summary>The MG338 (and rocket launcher) weapon-butt strike: a thump and the swing animation, nothing more.</summary>
    public struct BashFinisher : IComponent
    {
        public float Age;
    }

    // ================================================================= shared moves

    /// <summary>The handful of operations every finisher ends with, shared by their systems.</summary>
    public static class Finishers
    {
        /// <summary>Forgets the combo and puts the gun back in the hands.</summary>
        public static void EndCombo(World w, ref AltFire m, ref Weapon gun)
        {
            m.Step = 0;
            m.ComboTimer = 0f;
            m.Finisher = Entity.Null;
            gun.Holstered = false;
            ShowGun(w, gun.Sprite, true);
        }

        public static void ShowGun(World w, Entity sprite, bool shown)
        {
            if (!w.IsAlive(sprite)) return;
            if (shown) w.Commands.Remove<Hidden>(sprite);
            else w.Commands.Add(sprite, new Hidden());
        }

        public static void SetGunBool(World w, Entity sprite, string name, bool value)
        {
            if (w.IsAlive(sprite) && w.Has<SpriteAnimator>(sprite)) w.Get<SpriteAnimator>(sprite).SetBool(name, value);
        }

        /// <summary>The finisher's player (its parent), with the components every finisher move touches.</summary>
        public static bool Player(World w, Entity finisher, out Entity player)
        {
            player = w.Has<Parent>(finisher) ? w.Get<Parent>(finisher).Value : Entity.Null;
            return w.IsAlive(player) && w.Has<AltFire>(player) && w.Has<Weapon>(player);
        }

        /// <summary>Ends the move: combo reset, animator bool dropped, the finisher entity destroyed.</summary>
        public static void End(SystemContext ctx, Entity finisher, Entity player, string gunBool)
        {
            var w = ctx.World;
            ref var gun = ref w.Get<Weapon>(player);
            if (gunBool != null) SetGunBool(w, gun.Sprite, gunBool, false);
            EndCombo(w, ref w.Get<AltFire>(player), ref gun);
            ctx.Commands.DestroyEntity(finisher);
        }
    }

    // ================================================================= the systems

    /// <summary>Plays <see cref="GrappleFinisher"/>: grab (fist bool + view snapped to the target's chest), the gun
    /// out at 0.25 s asking for two shots, release a beat after the second lands. Runs alone (writes the player).</summary>
    public sealed class GrappleFinisherSystem : SystemBase, ITick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().With<GrappleFinisher>().With<Parent>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var moves = chunk.Get<GrappleFinisher>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var f = ref moves[i];
                    if (!Finishers.Player(World, ents[i], out var player)) { ctx.Commands.DestroyEntity(ents[i]); continue; }
                    ref var gun = ref World.Get<Weapon>(player);
                    ref var m = ref World.Get<AltFire>(player);

                    if (f.Age == 0f) // grab: fist up, eyes on the target
                    {
                        if (World.IsAlive(m.LeftFist) && World.Has<SpriteAnimator>(m.LeftFist))
                            World.Get<SpriteAnimator>(m.LeftFist).SetBool("Grapple", true);
                        SnapLook(player, f.Target);
                    }
                    f.Age += ctx.DeltaTime;

                    if (!f.Fired && f.Age >= 0.25f) // the gun comes out and asks for both shots
                    {
                        f.Fired = true;
                        gun.Holstered = false;
                        Finishers.ShowGun(World, gun.Sprite, true);
                        gun.ForcedShots = 2;
                    }
                    if (f.Fired && f.ReleaseAt == 0f && gun.ForcedShots == 0 && gun.RefireTimer <= 0f)
                        f.ReleaseAt = f.Age + 0.25f; // both shots away: hold the grip a beat
                    if (f.ReleaseAt > 0f && f.Age >= f.ReleaseAt)
                    {
                        if (World.IsAlive(m.LeftFist) && World.Has<SpriteAnimator>(m.LeftFist))
                            World.Get<SpriteAnimator>(m.LeftFist).SetBool("Grapple", false);
                        Finishers.End(ctx, ents[i], player, null);
                    }
                }
            }
        }

        /// <summary>The legacy ForceLookRotation, at the target's chest (the old grapplePoint height).</summary>
        private void SnapLook(Entity player, Entity target)
        {
            if (!World.IsAlive(target) || !World.Has<LocalToWorld>(target) || !World.Has<PlayerLook>(player)) return;
            Vector3 point = World.Get<LocalToWorld>(target).Position + new Vector3(0f, 0.43f, 0f);
            Vector3 dir = point - World.Get<LocalToWorld>(World.Get<PlayerViewRig>(player).Camera).Position;
            if (dir.sqrMagnitude < 1E-06f) return;
            dir.Normalize();
            ref var look = ref World.Get<PlayerLook>(player);
            look.Yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            look.Pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -90f, 90f);
        }
    }

    /// <summary>Plays <see cref="PointBlankFinisher"/>: the swing telegraphs for 0.5 s, then one shell through the
    /// normal fire path, done when the shot's refire window closes.</summary>
    public sealed class PointBlankFinisherSystem : SystemBase, ITick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().With<PointBlankFinisher>().With<Parent>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var moves = chunk.Get<PointBlankFinisher>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var f = ref moves[i];
                    if (!Finishers.Player(World, ents[i], out var player)) { ctx.Commands.DestroyEntity(ents[i]); continue; }
                    ref var gun = ref World.Get<Weapon>(player);

                    if (f.Age == 0f)
                    {
                        gun.Holstered = false;
                        Finishers.ShowGun(World, gun.Sprite, true);
                        Finishers.SetGunBool(World, gun.Sprite, "WeaponMelee", true);
                    }
                    f.Age += ctx.DeltaTime;

                    if (!f.Fired && f.Age >= 0.5f) { f.Fired = true; gun.ForcedShots = 1; }
                    if (f.Fired && gun.ForcedShots == 0 && gun.RefireTimer <= 0f)
                        Finishers.End(ctx, ents[i], player, "WeaponMelee");
                }
            }
        }
    }

    /// <summary>Plays <see cref="BashFinisher"/>: the strike sound and the swing animation, 0.2 s, done.</summary>
    public sealed class BashFinisherSystem : SystemBase, ITick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().With<BashFinisher>().With<Parent>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _q)
            {
                var moves = chunk.Get<BashFinisher>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var f = ref moves[i];
                    if (!Finishers.Player(World, ents[i], out var player)) { ctx.Commands.DestroyEntity(ents[i]); continue; }
                    ref var gun = ref World.Get<Weapon>(player);

                    if (f.Age == 0f)
                    {
                        gun.Holstered = false;
                        Finishers.ShowGun(World, gun.Sprite, true);
                        Finishers.SetGunBool(World, gun.Sprite, "WeaponMelee", true);
                        RipNRage.Engine.Ecs.Audio.Sfx.Play(Weapons.Data(gun.Slot).meleeSound);
                    }
                    f.Age += ctx.DeltaTime;

                    if (f.Age >= 0.2f) Finishers.End(ctx, ents[i], player, "WeaponMelee");
                }
            }
        }
    }
}
