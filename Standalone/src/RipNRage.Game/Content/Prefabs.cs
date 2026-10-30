using System.Collections.Generic;
using AI.BehaviorTree;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Code definitions of the Unity prefabs under Assets/_Game/Prefabs (and the AR_Grunt enemy that
    /// was authored directly in the TestArea scene). Transforms, colliders and component values are
    /// the ones stored in the .prefab / .unity files.
    /// </summary>
    public static class Prefabs
    {
        // ---------------------------------------------------------------- weapons (Prefabs/Weapons/*.prefab)

        public static readonly Prefab Glock = new Prefab("Glock", () => MakeWeapon<Glock>(() => GameAssets.GlockData));
        public static readonly Prefab M1887 = new Prefab("M1887", () => MakeWeapon<M1887Shotgun>(() => GameAssets.M1887Data));
        public static readonly Prefab MG338 = new Prefab("MG338", () =>
        {
            var go = MakeWeapon<MG338>(() => GameAssets.MG338Data);
            go.GetComponent<MG338>().meleeClip = GameAssets.MGPunch;
            return go;
        });
        public static readonly Prefab RocketLauncher = new Prefab("RocketLauncher", () =>
        {
            var go = MakeWeapon<RocketLauncher>(() => GameAssets.RocketLauncherData);
            go.GetComponent<RocketLauncher>().projectilePrefab = ProjectileRocket;
            return go;
        });

        private static GameObject MakeWeapon<T>(System.Func<WeaponData> data) where T : Weapon, new()
        {
            var go = new GameObject(typeof(T).Name);
            go.AddComponent<AudioSource>();
            var w = go.AddComponent<T>();
            w.weaponData = data();
            return go;
        }

        // ---------------------------------------------------------------- VFX

        /// <summary>
        /// Stand-in for the EffectCore "Bullet_GoldFire_Small_Impact" particle prefab: a small
        /// billboarded flash sprite. HitscanWeapon destroys it after 0.5 s like the original.
        /// </summary>
        public static readonly Prefab BulletImpact = new Prefab("BulletImpact", () =>
        {
            var go = new GameObject("BulletImpact");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.Glock["GlockFlash"];
            sr.billboard = true;
            sr.color = new Color(1f, 0.85f, 0.5f, 0.9f);
            go.transform.localScale = Vector3.one * 0.035f;
            go.AddComponent<FadeOut>().lifetime = 0.5f;
            return go;
        });

        /// <summary>Prefabs/Projectiles/RocketExplosion.prefab: animated explosion sprite, scale 3.</summary>
        public static readonly Prefab RocketExplosion = new Prefab("RocketExplosion", () =>
        {
            var go = new GameObject("RocketExplosion");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.RocketLauncher["explosion_01"];
            sr.billboard = true;
            go.transform.localScale = Vector3.one * 3f * 0.25f; // the 1024px explosion tiles at scale 3 cover ~30 units in Unity; keep it readable here
            var anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = GameAssets.ExplosionController;
            go.AddComponent<FadeOut>().lifetime = 0.4f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.25f, 1f); light.range = 30f; light.intensity = 6f;
            go.AddComponent<FlashLight>();
            return go;
        });

        // ---------------------------------------------------------------- projectiles (Prefabs/Projectiles/*.prefab)

        /// <summary>Projectile_Rocket.prefab: speed 50, damage 1000, range 100, trigger box 0.2x0.2x1.</summary>
        public static readonly Prefab ProjectileRocket = new Prefab("Projectile_Rocket", () =>
        {
            var go = new GameObject("Projectile_Rocket");
            go.layer = Layers.Projectile;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.2f, 0.2f, 1f);
            Physics.RegisterMover(col);
            var p = go.AddComponent<RocketProjectile>();
            p.projectileStats = new ProjectileStats { speed = 50, damage = 1000, range = 100, hitVFX = RocketExplosion, trailEffect = "rocket", explosionEffect = "rocket", bulletKey = "explosive" };
            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.Glock["GlockFlash"]; // glowing exhaust (the EffectCore rocket mesh was not ported)
            sr.billboard = true;
            sr.color = new Color(1f, 0.75f, 0.35f, 1f);
            vis.transform.localScale = new Vector3(0.035f, 0.035f, 0.035f);
            var rl = go.AddComponent<Light>();
            rl.type = LightType.Point; rl.color = new Color(1f, 0.7f, 0.35f, 1f); rl.range = 10f; rl.intensity = 2.5f;
            return go;
        });

        /// <summary>Projectile_StandardBullet.prefab: speed 50, damage 5, range 100 (fired by enemies).</summary>
        public static readonly Prefab ProjectileStandardBullet = new Prefab("Projectile_StandardBullet", () =>
        {
            var go = new GameObject("Projectile_StandardBullet");
            go.layer = Layers.Projectile;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.2f, 0.2f, 1f);
            Physics.RegisterMover(col);
            var p = go.AddComponent<Projectile>();
            // readability + lethality (standalone): slower but twice as damaging, a bright glowing slug with a trail and its own light
            p.projectileStats = new ProjectileStats { speed = 40, damage = 10, range = 100, hitVFX = BulletImpact, bulletKey = "enemy", trailEffect = "bullet" };
            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = GameAssets.Glock["GlockFlash"];
            sr.billboard = true;
            sr.fullBright = true;
            sr.color = new Color(1f, 0.75f, 0.25f, 1f);
            vis.transform.localScale = Vector3.one * 0.06f;
            var bl = go.AddComponent<Light>();
            bl.type = LightType.Point; bl.color = new Color(1f, 0.7f, 0.3f, 1f); bl.range = 5f; bl.intensity = 1.4f;
            return go;
        });

        // ---------------------------------------------------------------- pickups (Prefabs/PickupPrefabs/*.prefab)

        private static Sprite[] AmmoPickupSprites() => new[]
        {
            GameAssets.Pickups["pickup_lightammo"], GameAssets.Pickups["pickup_heavyammo"], GameAssets.Pickups["pickup_shellammo"],
            GameAssets.Pickups["pickup_energyammo"], GameAssets.Pickups["pickup_explosiveammo"],
        };

        /// <summary>BaseAmmoPickup.prefab: scale 0.75, trigger sphere r=1.5, world collision sphere r=1, sprite child.</summary>
        private static GameObject MakeAmmoPickup(string name, AmmoType type, int count)
        {
            var go = new GameObject(name);
            go.layer = Layers.Pickup;
            go.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.5f;
            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform, false);
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.billboard = true;
            sr.lit = true;
            var pickup = go.AddComponent<AmmoPickup>();
            pickup.ammoType = type;
            pickup.ammoCount = count;
            pickup.ammoTypeSprite = AmmoPickupSprites();
            pickup.pickupSpriteRenderer = sr;
            go.AddComponent<Bob>();
            return go;
        }

        public static readonly Prefab LightAmmoPickup = new Prefab("LightAmmoPickup", () => MakeAmmoPickup("LightAmmoPickup", AmmoType.LIGHT, 50));
        public static readonly Prefab HeavyAmmoPickup = new Prefab("HeavyAmmoPickup", () => MakeAmmoPickup("HeavyAmmoPickup", AmmoType.HEAVY, 25));
        public static readonly Prefab ShellAmmoPickup = new Prefab("ShellAmmoPickup", () => MakeAmmoPickup("ShellAmmoPickup", AmmoType.SHELL, 16));
        public static readonly Prefab EnergyAmmoPickup = new Prefab("EnergyAmmoPickup", () => MakeAmmoPickup("EnergyAmmoPickup", AmmoType.ENERGY, 250));
        public static readonly Prefab ExplosiveAmmoPickup = new Prefab("ExplosiveAmmoPickup", () => MakeAmmoPickup("ExplosiveAmmoPickup", AmmoType.EXPLOSIVE, 3));

        // ---------------------------------------------------------------- enemies (AR_Grunt from TestArea.unity)

        /// <summary>
        /// The AR_Grunt rifleman: AIState + AIAgent + BehaviorTreeOwner + NavMeshAgent + capsule,
        /// a 4-direction sprite (AnimationStateController), a 10 m vision trigger, projectile spawn and
        /// grapple point. The scene scaled them by 2.25; the sprite scale here keeps them ~2.2 m tall.
        /// </summary>
        public static readonly Prefab Rifleman = new Prefab("AR_Grunt", () =>
        {
            // The scene object is uniformly scaled by 2.25 with its origin at the body centre;
            // the local offsets below are the scene values multiplied by that scale.
            const float S = 2.25f;
            var go = new GameObject("AR_Grunt");
            go.layer = Layers.Enemies;
            go.AddComponent<AudioSource>(); // attack telegraph / shot / alert sounds (standalone)
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.15f * S;
            capsule.height = 1f * S;
            capsule.center = Vector3.zero;

            // 512 px tiles @100 ppu, centred on the body; scaled so the character is ~2.25 units tall.
            var holder = new GameObject("SpriteHolder");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localScale = Vector3.one * (S / 5.12f);
            var sr = holder.AddComponent<SpriteRenderer>();
            sr.billboard = true;
            sr.lit = true;

            var nav = go.AddComponent<NavMeshAgent>();
            nav.speed = 2f; nav.radius = 0.25f * S; nav.height = 1f * S; nav.baseOffset = 0.5f * S;
            var state = go.AddComponent<AIState>();
            state.data = GameAssets.RiflemanData;
            var animCtrl = go.AddComponent<AnimationStateController>();
            animCtrl.SpriteRenderer = sr;
            var agent = go.AddComponent<AIAgent>();
            agent.navMeshAgent = nav;
            agent.animationStateController = animCtrl;
            agent.projectile = ProjectileStandardBullet;
            agent.data = GameAssets.RiflemanData;
            var owner = go.AddComponent<BehaviorTreeOwner>();
            owner.tree = BehaviorTrees.RiflemanTree;
            go.AddComponent<EnemyHitFlash>().target = sr; // hit flash + death dissolve (sprite shader path)

            var vision = new GameObject("VisionSensor");
            vision.transform.SetParent(go.transform, false);
            vision.layer = Layers.VisionSensor;
            var visionCol = vision.AddComponent<SphereCollider>();
            visionCol.isTrigger = true;
            visionCol.radius = 10f;
            vision.AddComponent<AgentVisionSensor>()._treeOwner = owner;

            var spawn = new GameObject("ProjectileSpawnPostion");
            spawn.transform.SetParent(go.transform, false);
            spawn.transform.localPosition = new Vector3(0, 0.1f, 0.2f) * S;
            agent.projectileSpawnPosition = spawn.transform;

            var grapple = new GameObject("GrapplePoint");
            grapple.transform.SetParent(go.transform, false);
            grapple.transform.localPosition = new Vector3(-0.0279f, 0.19f, 0) * S;
            state.grapplePoint = grapple.transform;
            return go;
        });

        /// <summary>Spawns a rifleman with its feet on <paramref name="floorPosition"/>.</summary>
        public static GameObject SpawnRifleman(Vector3 floorPosition, Quaternion rotation) =>
            Rifleman.Instantiate(floorPosition + Vector3.up * (0.5f * 2.25f), rotation);
    }

    /// <summary>Fades a sprite out over its lifetime (replaces particle-system lifetime/alpha curves).</summary>
    public class FadeOut : MonoBehaviour
    {
        public float lifetime = 0.5f;
        private float _t;
        private SpriteRenderer _sr;
        protected override void Awake() { _sr = GetComponent<SpriteRenderer>(); }
        protected override void Update()
        {
            _t += Time.deltaTime;
            if (_sr != null) { var c = _sr.color; c.a = Mathf.Clamp01(1f - _t / lifetime); _sr.color = c; }
        }
    }

    /// <summary>Gentle hover for pickups.</summary>
    public class Bob : MonoBehaviour
    {
        private float _baseY; private float _phase;
        protected override void Start() { _baseY = transform.position.y; _phase = Random.Range(0f, 6.28f); }
        protected override void Update()
        {
            var p = transform.position; p.y = _baseY + Mathf.Sin(Time.time * 2f + _phase) * 0.15f; transform.position = p;
        }
    }

    /// <summary>The behaviour trees authored with the graph editor (Assets/_AI_BehaviorTree/ScriptableObjects).</summary>
    public static class BehaviorTrees
    {
        /// <summary>
        /// BehaviorTree2.asset:
        /// Root → PlayerSpotted ? InAttackRange(10) ? [LookAt, Attack] : [MoveTo]
        ///                       : [Wait(1), RandomLocation(r=15, min=10), MoveTo]
        /// </summary>
        public static BehaviorTree RiflemanTree
        {
            get
            {
                var attack = new SequencerNode(); attack.children.AddRange(new BaseNode[] { new LookAtNode(), new AttackNode() });
                var chase = new SequencerNode(); chase.children.Add(new MoveToNode());
                var inRange = new InAttackRangeNode { attackRange = 10 }; inRange.children.AddRange(new BaseNode[] { attack, chase });
                var wander = new SequencerNode();
                wander.children.AddRange(new BaseNode[] { new WaitNode { duration = 1 }, new RandomLocationNode { radius = 15, minDistance = 10 }, new MoveToNode() });
                var spotted = new PlayerSpottedNode(); spotted.children.AddRange(new BaseNode[] { inRange, wander });
                var root = new RootNode { child = spotted };
                var tree = new BehaviorTree { name = "BehaviorTree2", rootNode = root, blackboard = new Blackboard() };
                tree.Traverse(root, n => tree.nodes.Add(n));
                return tree;
            }
        }
    }
}
