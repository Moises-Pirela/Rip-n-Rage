using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine.Graphics.Particles;

namespace RipNRage.Game.Content.Vfx
{
    /// <summary>Per-ammo-type visuals.</summary>
    public sealed class BulletBinding
    {
        /// <summary>Effect spawned from the muzzle towards the hit point (SpawnTracer); empty = none.</summary>
        public string tracer = "";
        /// <summary>Scale applied to the surface impact effect for this calibre.</summary>
        public float impactScale = 1f;
        /// <summary>Scale applied to the blood effect for this calibre.</summary>
        public float bloodScale = 1f;
        /// <summary>Optional impact effect override for this calibre (e.g. energy weapons scorch instead of chipping).</summary>
        public string impactOverride = "";
    }

    /// <summary>
    /// <c>vfx/bindings.json</c>: which effect plays for what. Surfaces → impact effect, ammo types → tracer +
    /// scales, explosion / trail / death kinds → effect names. Hot-reloaded by <see cref="VfxRuntime"/>.
    /// </summary>
    public sealed class VfxBindings
    {
        public Dictionary<string, string> surfaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BulletBinding> bullets = new Dictionary<string, BulletBinding>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> explosions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> trails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> deaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Effect for hits on living things (the "flesh" surface also maps here).</summary>
        public string blood = "blood_hit";

        public static VfxBindings CreateDefault()
        {
            var b = new VfxBindings();
            b.surfaces["default"] = "impact_concrete";
            b.surfaces["concrete"] = "impact_concrete";
            b.surfaces["wood"] = "impact_wood";
            b.surfaces["metal"] = "impact_metal";
            b.surfaces["glass"] = "impact_glass";
            b.surfaces["sand"] = "impact_sand";
            b.surfaces["water"] = "impact_water";
            b.surfaces["flesh"] = "blood_hit";
            b.bullets["light"] = new BulletBinding { tracer = "tracer_light", impactScale = 0.8f, bloodScale = 0.8f };
            b.bullets["heavy"] = new BulletBinding { tracer = "tracer_heavy", impactScale = 1.3f, bloodScale = 1.4f };
            b.bullets["shell"] = new BulletBinding { tracer = "tracer_shell", impactScale = 0.55f, bloodScale = 0.6f };
            b.bullets["energy"] = new BulletBinding { tracer = "tracer_energy", impactScale = 1f, bloodScale = 1f, impactOverride = "impact_energy" };
            b.bullets["explosive"] = new BulletBinding { tracer = "", impactScale = 1f, bloodScale = 1f };
            b.bullets["enemy"] = new BulletBinding { tracer = "", impactScale = 0.7f, bloodScale = 0.7f };
            b.explosions["rocket"] = "explosion_rocket";
            b.trails["rocket"] = "rocket_trail";
            b.trails["bullet"] = "bullet_trail";
            b.deaths["default"] = "blood_death";
            return b;
        }

        public string ImpactFor(string surface, BulletBinding bullet)
        {
            if (bullet != null && !string.IsNullOrEmpty(bullet.impactOverride) && !string.Equals(surface, Surfaces.Flesh, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(surface, Surfaces.Water, StringComparison.OrdinalIgnoreCase))
                return bullet.impactOverride;
            if (surface != null && surfaces.TryGetValue(surface, out var e) && !string.IsNullOrEmpty(e)) return e;
            return surfaces.TryGetValue("default", out var d) ? d : null;
        }

        public BulletBinding Bullet(string key) => key != null && bullets.TryGetValue(key, out var b) ? b : null;
    }

    /// <summary>
    /// Game-side entry points for the data-driven VFX: weapons and projectiles call these, the bindings file
    /// decides which <see cref="ParticleEffect"/> plays. Falls back gracefully (no effect, no exception) when
    /// an effect or binding is missing, so the game runs with an empty vfx folder too.
    /// </summary>
    public static class GameVfx
    {
        public static VfxBindings Bindings = VfxBindings.CreateDefault();
        public static string BindingsPath => VfxLibrary.Directory == null ? null : Path.Combine(VfxLibrary.Directory, "bindings.json");

        /// <summary>True once effect data exists (code defaults or files).</summary>
        public static bool Available => VfxLibrary.Effects.Count > 0;

        public static string AmmoKey(AmmoType t) => t switch
        {
            AmmoType.LIGHT => "light", AmmoType.HEAVY => "heavy", AmmoType.SHELL => "shell", AmmoType.ENERGY => "energy", _ => "explosive",
        };

        /// <summary>Bullet hit on whatever the raycast found: blood for living things, a surface impact otherwise.</summary>
        public static void Impact(in RaycastHit hit, string bulletKey)
        {
            if (!Available) return;
            string surface = SurfaceResolver.Resolve(hit);
            var bullet = Bindings.Bullet(bulletKey);
            if (string.Equals(surface, Surfaces.Flesh, StringComparison.OrdinalIgnoreCase)) { Blood(hit.point, hit.normal, bulletKey); return; }
            string effect = Bindings.ImpactFor(surface, bullet);
            VfxLibrary.Spawn(effect, hit.point, hit.normal, bullet?.impactScale ?? 1f);
        }

        /// <summary>Impact at a known point / normal with an explicit surface (projectiles that have no collider info).</summary>
        public static void ImpactOn(string surface, Vector3 point, Vector3 normal, string bulletKey)
        {
            if (!Available) return;
            var bullet = Bindings.Bullet(bulletKey);
            if (string.Equals(surface, Surfaces.Flesh, StringComparison.OrdinalIgnoreCase)) { Blood(point, normal, bulletKey); return; }
            VfxLibrary.Spawn(Bindings.ImpactFor(surface, bullet), point, normal, bullet?.impactScale ?? 1f);
        }

        public static void Blood(Vector3 point, Vector3 normal, string bulletKey, float scale = 1f)
        {
            if (!Available) return;
            var bullet = Bindings.Bullet(bulletKey);
            VfxLibrary.Spawn(Bindings.blood, point, normal, (bullet?.bloodScale ?? 1f) * scale);
        }

        /// <summary>Tracer from the muzzle to the hit point (or the end of the ray when nothing was hit).</summary>
        public static void Tracer(string bulletKey, Vector3 from, Vector3 to)
        {
            if (!Available) return;
            var bullet = Bindings.Bullet(bulletKey);
            if (bullet == null || string.IsNullOrEmpty(bullet.tracer)) return;
            VfxLibrary.SpawnTracer(bullet.tracer, from, to);
        }

        public static void Explosion(string kind, Vector3 point, Vector3 normal, float scale = 1f)
        {
            if (!Available) return;
            if (!Bindings.explosions.TryGetValue(kind ?? "", out var effect)) Bindings.explosions.TryGetValue("rocket", out effect);
            VfxLibrary.Spawn(effect, point, normal, scale);
        }

        /// <summary>Continuous effect following a projectile; call <see cref="ParticleEffectInstance.Stop"/> when it dies.</summary>
        public static ParticleEffectInstance Trail(string kind, Transform follow)
        {
            if (!Available || follow == null) return null;
            if (!Bindings.trails.TryGetValue(kind ?? "", out var effect) || string.IsNullOrEmpty(effect)) return null;
            var inst = VfxLibrary.Spawn(effect, follow.position, -follow.forward);
            if (inst == null) return null;
            inst.Follow(follow, default, backwards: true);
            return inst;
        }

        public static void Death(string kind, Vector3 point)
        {
            if (!Available) return;
            if (!Bindings.deaths.TryGetValue(kind ?? "", out var effect)) Bindings.deaths.TryGetValue("default", out effect);
            VfxLibrary.Spawn(effect, point, Vector3.up);
        }

        /// <summary>Plays any effect by name (enemy telegraphs, dashes…); silently does nothing when unknown.</summary>
        public static ParticleEffectInstance Spawn(string effect, Vector3 point, Vector3 forward, float scale = 1f)
        {
            if (!Available || string.IsNullOrEmpty(effect)) return null;
            return VfxLibrary.Spawn(effect, point, forward, scale);
        }

        /// <summary>Approximate world-space muzzle of the first-person weapon (the viewmodel has its own projection, so it is placed relative to the camera).</summary>
        public static Vector3 MuzzlePosition(Transform camera) =>
            camera.position + camera.forward * 0.9f + camera.right * 0.28f - camera.up * 0.22f;

        // ------------------------------------------------------------------ bindings file

        public static void LoadBindings()
        {
            string path = BindingsPath;
            if (path == null) return;
            if (!File.Exists(path))
            {
                try { File.WriteAllText(path, JsonSerializer.Serialize(Bindings, EngineJson.Options)); }
                catch (Exception e) { Debug.LogWarning("vfx: could not write default bindings: " + e.Message); }
                return;
            }
            try
            {
                var b = JsonSerializer.Deserialize<VfxBindings>(File.ReadAllText(path), EngineJson.Options);
                if (b != null) { Normalize(b); Bindings = b; }
            }
            catch (Exception e) { Debug.LogError("vfx: bindings.json failed to load: " + e.Message); }
        }

        /// <summary>Deserialised dictionaries are case sensitive; rebuild them case-insensitive and fill gaps from the defaults.</summary>
        private static void Normalize(VfxBindings b)
        {
            var def = VfxBindings.CreateDefault();
            b.surfaces = Merge(b.surfaces, def.surfaces);
            b.explosions = Merge(b.explosions, def.explosions);
            b.trails = Merge(b.trails, def.trails);
            b.deaths = Merge(b.deaths, def.deaths);
            var bullets = new Dictionary<string, BulletBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in def.bullets) bullets[kv.Key] = kv.Value;
            if (b.bullets != null) foreach (var kv in b.bullets) if (kv.Value != null) bullets[kv.Key] = kv.Value;
            b.bullets = bullets;
            if (string.IsNullOrEmpty(b.blood)) b.blood = def.blood;
        }

        private static Dictionary<string, string> Merge(Dictionary<string, string> file, Dictionary<string, string> defaults)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in defaults) d[kv.Key] = kv.Value;
            if (file != null) foreach (var kv in file) d[kv.Key] = kv.Value;
            return d;
        }
    }

    /// <summary>
    /// Lives on the session object: keeps <c>bindings.json</c> hot-reloaded and sets the particle collision mask
    /// (particles bounce off level geometry and props, never off characters, projectiles or triggers).
    /// </summary>
    public class VfxRuntime : MonoBehaviour
    {
        private DateTime _stamp;
        private float _nextPoll;

        protected override void Awake()
        {
            ParticleWorld.CollisionMask = ~((1 << Layers.Player) | (1 << Layers.Enemies) | (1 << Layers.Projectile) | (1 << Layers.VisionSensor) | (1 << Layers.Pickup) | (1 << Layers.IgnoreRaycast));
            _stamp = Stamp();
        }

        protected override void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var s = Stamp();
            if (s == _stamp) return;
            _stamp = s;
            GameVfx.LoadBindings();
            Debug.LogWarning("vfx: bindings reloaded");
        }

        private static DateTime Stamp()
        {
            string p = GameVfx.BindingsPath;
            try { return p != null && File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue; }
            catch (IOException) { return DateTime.MinValue; }
        }
    }
}
