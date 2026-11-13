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
