using System.Collections.Generic;
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
            // the pickup's trigger sphere (prefab radius 1.5) is an ECS Trigger entity; TriggerVolume relays its
            // overlaps to AmmoPickup.OnTriggerEnter. It follows the GameObject, which Bob moves up and down.
            var trigger = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticSphere(
                Application.Current.World, Vector3.zero, 1.5f, Layers.Pickup, trigger: true);
            RipNRage.Game.Ecs.TriggerVolume.Attach(go, trigger);
            go.AddComponent<RipNRage.Game.Ecs.TriggerFollower>();

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
        /// The AR_Grunt rifleman: AIState + AIAgent (the state tree runs on the entity: AiBrain), a 10 m vision
        /// trigger, projectile spawn and grapple point. The scene scaled them by 2.25; the sprite scale here keeps
        /// them ~2.2 m tall.
        /// </summary>
        public static readonly Prefab Rifleman = new Prefab("AR_Grunt", () =>
        {
            // The scene object is uniformly scaled by 2.25 with its origin at the body centre;
            // the local offsets below are the scene values multiplied by that scale.
            const float S = 2.25f;
            var go = new GameObject("AR_Grunt");
            go.layer = Layers.Enemies;
            // The enemy is its entity (6.1): AIState.Awake builds the body (capsule + spring + motor), the sprite
            // child (8-sided sheet, feet on the ground) and the effects, and this GameObject follows the entity.
            var state = go.AddComponent<AIState>();
            state.data = GameAssets.RiflemanData;
            var agent = go.AddComponent<AIAgent>();
            agent.data = GameAssets.RiflemanData;
            go.AddComponent<EnemyDestruction>();          // death: the sprite is sliced into a ragdoll (skeletons/rifleman.json)

            var vision = new GameObject("VisionSensor");
            vision.transform.SetParent(go.transform, false);
            vision.layer = Layers.VisionSensor;
            vision.AddComponent<AgentVisionSensor>()._state = state;

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

        /// <summary>Spawns a rifleman with its feet on <paramref name="floorPosition"/> (the body floats its ride height above the floor).</summary>
        public static GameObject SpawnRifleman(Vector3 floorPosition, Quaternion rotation) =>
            Rifleman.Instantiate(floorPosition + Vector3.up * (0.5f * 2.25f + CharacterProfiles.Enemy.rideHeight), rotation);
    }

    /// <summary>Fades a sprite out over its lifetime (replaces particle-system lifetime/alpha curves).</summary>
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
}
