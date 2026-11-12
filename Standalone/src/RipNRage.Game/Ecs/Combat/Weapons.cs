using System.Runtime.CompilerServices;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Game.Content.Vfx;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.Combat
{
    /// <summary>
    /// The equipped weapon: runtime fire state plus a slot into <see cref="Weapons.All"/>, where the stats live —
    /// weapon definitions are shared immutable data (the behaviour-tree pattern), not copies on the entity.
    /// </summary>
    public struct Weapon : IComponent
    {
        public int Slot;
        /// <summary>Seconds until the next shot (scaled time, so the death slow-motion slows the gun).</summary>
        public float RefireTimer;
        public float Bloom;
        /// <summary>The gun is off the trigger (jab combo, death).</summary>
        public bool Holstered;
        /// <summary>Shots a finisher has asked for: fired through the normal path (ammo, refire, recoil) even while
        /// holstered — the finisher requests, the fire system fires. Data in, shots out; no cross-system calls.</summary>
        public int ForcedShots;
        /// <summary>The viewmodel sprite entity whose animator gets the IsFiring bool.</summary>
        public Entity Sprite;
        internal bool AnimFiring;
    }

    [InlineArray(Weapons.AmmoTypes)] public struct AmmoBlock { private int _e0; }

    /// <summary>Ammo by <see cref="AmmoType"/>, and the previously held slot (quick swap).</summary>
    public struct Inventory : IComponent
    {
        public AmmoBlock Ammo;
        public int LastSlot;
    }

    /// <summary>
    /// The weapon definitions and the pure functions over them. State lives in <see cref="Weapon"/> /
    /// <see cref="Inventory"/>; everything here is data in, data out.
    /// </summary>
    public static class Weapons
    {
        public const int AmmoTypes = 5;

        /// <summary>The four weapons, by slot (set once by the player prefab).</summary>
        public static WeaponData[] All = System.Array.Empty<WeaponData>();

        /// <summary>Per ammo type (the PlayerCharacter prefab's limits).</summary>
        public static readonly int[] AmmoLimits = { 120, 60, 120, 100, 10 };

        /// <summary>What the player's shots can hit (the prefab's layerMask 6421: Default | Water | Enemy | Environment).</summary>
        public const int HitMask = 6421;

        /// <summary>WeaponRecoil.recoil was authored for Unity's additive model; 12 → 3 degrees.</summary>
        public const float KickScale = 0.25f;

        public static WeaponData Data(int slot) => All[slot];

        /// <summary>Spread for the next shot: base + bloom + movement and air penalties (direction offset per unit forward).</summary>
        public static float CurrentSpread(in Weapon w, Vector3 velocity, bool grounded)
        {
            var st = All[w.Slot].weaponStats;
            velocity.y = 0f;
            float s = st.weaponSpread + w.Bloom
                      + st.moveSpread * Mathf.Clamp01(velocity.magnitude / 16f)
                      + (grounded ? 0f : st.airSpread);
            float cap = Mathf.Max(st.spreadMax, st.weaponSpread);
            return st.spreadMax > 0f ? Mathf.Min(s, cap) : s;
        }

        public static bool AddAmmo(ref Inventory inv, AmmoType type, int amount)
        {
            int i = (int)type;
            if (inv.Ammo[i] >= AmmoLimits[i]) return false;
            inv.Ammo[i] = Mathf.Clamp(inv.Ammo[i] + amount, 0, AmmoLimits[i]);
            return true;
        }

        public static int[] AmmoArray(in Inventory inv)
        {
            var a = new int[AmmoTypes];
            for (int i = 0; i < AmmoTypes; i++) a[i] = inv.Ammo[i];
            return a;
        }

        public static void LoadAmmo(ref Inventory inv, int[] ammo)
        {
            for (int i = 0; i < AmmoTypes && i < ammo.Length; i++) inv.Ammo[i] = ammo[i];
        }
    }

    /// <summary>
    /// Fires the equipped weapon while the fire input is held: refire and bloom on scaled time, ammo, recoil and
    /// trauma on the camera entity's shake, the viewmodel's IsFiring bool, then the shot itself — hitscan rays that
    /// resolve hits as <see cref="DamageEvent"/>s on whatever entity the ray names (headshots judged by its
    /// <see cref="Hitbox"/>), or a projectile spawn. Replaces the Weapon / HitscanWeapon / ProjectileWeapon class
    /// hierarchy and its coroutines. Runs alone (physics queries; no [Reads]/[Writes]).
    /// </summary>
    [UpdateAfter(typeof(EcsInputSystem))]
    public sealed class WeaponFireSystem : SystemBase, ITick
    {
        /// <summary>Where rockets leave from (the prefab's ProjectileSpawn node; a bridge until projectiles are entities in 5.4).</summary>
        public static Transform ProjectileSpawn;

        private Query _q;
        public override void OnCreate() => _q = World.Query().With<Weapon>().With<Inventory>().With<PlayerViewRig>().Build();

        public void Tick(SystemContext ctx)
        {
            if (Weapons.All.Length == 0) return;
            var input = World.GetSingleton<InputState>();
            // ctx.DeltaTime is the Application's scaled Time.deltaTime, so slow motion slows the gun as the
            // WaitForSeconds coroutines did — and a headless test world drives it through World.Update(dt)
            float dt = ctx.DeltaTime;

            foreach (var chunk in _q)
            {
                var weapons = chunk.Get<Weapon>(); var invs = chunk.Get<Inventory>(); var rigs = chunk.Get<PlayerViewRig>();
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var w = ref weapons[i];
                    var st = Weapons.All[w.Slot].weaponStats;

                    if (w.RefireTimer > 0f)
                    {
                        w.RefireTimer -= dt;
                        if (w.RefireTimer <= 0f && w.AnimFiring) SetFiring(ref w, false);
                    }
                    w.Bloom = Mathf.MoveTowards(w.Bloom, 0f, st.bloomRecovery * dt);

                    bool forced = w.ForcedShots > 0;
                    if ((!input.FireHeld && !forced) || (w.Holstered && !forced) || w.RefireTimer > 0f) continue;
                    if (invs[i].Ammo[(int)Weapons.All[w.Slot].ammoType] <= 0) { w.ForcedShots = 0; continue; } // dry: a finisher gives up

                    if (forced) w.ForcedShots--;
                    Fire(ctx, ents[i], ref w, ref invs[i], in rigs[i]);
                }
            }
        }

        private void Fire(SystemContext ctx, Entity player, ref Weapon w, ref Inventory inv, in PlayerViewRig rig)
        {
            var data = Weapons.All[w.Slot];
            var st = data.weaponStats;

            inv.Ammo[(int)data.ammoType]--;
            w.RefireTimer = st.fireRate;
            SetFiring(ref w, true);
            float spread = Spread(player, in w);
            w.Bloom = Mathf.Min(w.Bloom + st.bloomPerShot, Mathf.Max(0f, st.spreadMax - st.weaponSpread));

            // the camera entity: aim frame and recoil
            var eye = World.Get<LocalToWorld>(rig.Camera);
            if (World.Has<CameraShake>(rig.Camera)) Kick(ref World.Get<CameraShake>(rig.Camera), data.weaponRecoil);

            RipNRage.Engine.Ecs.Audio.Sfx.Play(data.fireSound);
            PlayerState.Current?.SetViolentCombatState();
            var combat = EnragedInjector<CombatController>.GetInstance();
            if (combat != null) { combat.OnFireWeapon?.Invoke(); combat.OnAmmoChanged?.Invoke(); }
            EnragedInjector<HudController>.GetInstance()?.CurrentCrosshair?.OnShoot();

            if (data.projectileKind >= 0) LaunchProjectile(data, in eye, spread * st.projectileSpreadScale);
            else Hitscan(ctx, data, in eye, spread);
        }

        private void Hitscan(SystemContext ctx, WeaponData data, in LocalToWorld eye, float spread)
        {
            var st = data.weaponStats;
            string bulletKey = GameVfx.AmmoKey(data.ammoType);
            Vector3 muzzle = GameVfx.MuzzlePosition(eye.Position, eye.Rotation);
            bool vfx = GameVfx.Available;

            for (int b = 0; b < st.bulletsPerShot; b++)
            {
                Vector3 dir = eye.Forward + eye.Right * Random.Range(-spread, spread) + eye.Up * Random.Range(-spread, spread);
                if (!PhysicsApi.Raycast(eye.Position, dir, out var hit, 100f, Weapons.HitMask))
                {
                    if (vfx) GameVfx.Tracer(bulletKey, muzzle, eye.Position + dir.normalized * 100f);
                    continue;
                }
                if (vfx) GameVfx.Tracer(bulletKey, muzzle, hit.point);

                if (World.Has<Health>(hit.entity) && !World.Has<Dead>(hit.entity))
                {
                    bool headshot = World.Has<Hitbox>(hit.entity)
                                    && World.Get<Hitbox>(hit.entity).IsHeadshot(World.Get<LocalToWorld>(hit.entity), hit.point);
                    ctx.Post(new DamageEvent
                    {
                        Target = hit.entity,
                        Amount = st.damagePerShot * (headshot ? 2f : 1f),
                        Point = hit.point, Direction = dir.normalized,
                        Impulse = st.hitImpulse > 0f ? st.hitImpulse : 6f,
                        Headshot = headshot,
                    });
                    if (vfx) GameVfx.Blood(hit.point, hit.normal, bulletKey, headshot ? 1.7f : 1f);
                }
                else
                {
                    if (vfx) GameVfx.Impact(hit, bulletKey);
                    // bullets shove loose props, swing doors, set pendulums going
                    var body = PhysicsApi.World.Table.BodyOf(hit.proxy);
                    if (body != null && !body.isKinematic)
                        body.AddImpulseAtPosition(dir.normalized * (st.hitImpulse > 0f ? st.hitImpulse : 6f), hit.point);
                }
            }
        }

        private void LaunchProjectile(WeaponData data, in LocalToWorld eye, float spread)
        {
            Vector3 dir = eye.Forward + eye.Right * Random.Range(-spread, spread) + eye.Up * Random.Range(-spread, spread);
            Vector3 origin = ProjectileSpawn != null ? ProjectileSpawn.position : eye.Position;
            Projectiles.Launch(World, data.projectileKind, origin, dir, PlayerLink.PlayerEntity);
        }

        private float Spread(Entity player, in Weapon w)
        {
            Vector3 velocity = World.TryGet<BodyVelocity>(player, out var bv) ? bv.Linear : Vector3.zero;
            bool grounded = !World.TryGet<CharacterMotor>(player, out var motor) || motor.IsGrounded;
            return Weapons.CurrentSpread(in w, velocity, grounded);
        }

        /// <summary>The legacy CameraRecoil.ApplyRecoil: pitch up, random yaw / roll, trauma for heavy weapons.</summary>
        private static void Kick(ref CameraShake shake, in WeaponRecoil recoil)
        {
            if (recoil.recoilSpeed > 0f) shake.RotationSpeed = recoil.recoilSpeed;
            if (recoil.recoilRecoverySpeed > 0f) shake.ReturnSpeed = recoil.recoilRecoverySpeed;
            shake.Kick(recoil.recoil.x * Weapons.KickScale, recoil.recoil.y * Weapons.KickScale, recoil.recoil.z * Weapons.KickScale);
            shake.AddTrauma(recoil.cameraShake);
        }

        private void SetFiring(ref Weapon w, bool firing)
        {
            w.AnimFiring = firing;
            if (World.IsAlive(w.Sprite) && World.Has<SpriteAnimator>(w.Sprite))
                World.Get<SpriteAnimator>(w.Sprite).SetBool("IsFiring", firing);
        }
    }
}
