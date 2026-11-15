using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Game.Ecs
{
    /// <summary>Brief point light at the muzzle whenever the equipped weapon fires (9.3; was the
    /// <c>MuzzleFlashLight</c> MonoBehaviour and the <c>CombatController.OnFireWeapon</c> event).</summary>
    public struct MuzzleFlash : IComponent
    {
        public Color Color;
        public float Intensity, Range, Duration;
        /// <summary>Seconds of flash left (set by the shot, drained by the system).</summary>
        public float Timer;
    }

    public static class MuzzleFlashes
    {
        /// <summary>The player's muzzle light, dark until a shot (the player prefab builds it).</summary>
        public static Entity Spawn(World w)
        {
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(Vector3.zero));
            w.Add(e, new LightSource { Type = LightType.Point, Color = new Color(1f, 0.8f, 0.45f, 1f), Range = 14f, Intensity = 0f });
            w.Add(e, new MuzzleFlash { Color = new Color(1f, 0.8f, 0.45f, 1f), Intensity = 4f, Range = 14f, Duration = 0.07f });
            return e;
        }

        private static World _queryWorld;
        private static Query _query;

        internal static Query Flashes(World w)
        {
            if (!ReferenceEquals(_queryWorld, w)) { _queryWorld = w; _query = w.Query().With<MuzzleFlash>().Build(); }
            return _query;
        }

        /// <summary>A shot was fired: the flash starts (called by WeaponFireSystem, which runs alone).</summary>
        public static void Flash(World w)
        {
            foreach (var chunk in Flashes(w))
            {
                var flashes = chunk.Get<MuzzleFlash>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) flashes[i].Timer = flashes[i].Duration;
            }
        }
    }

    /// <summary>
    /// Rides the flash just ahead of the camera and fades it out. After the fire system: a shot lights the same
    /// frame (the old MonoBehaviour applied it a frame later).
    /// </summary>
    [UpdateAfter(typeof(Combat.WeaponFireSystem))]
    [Reads(typeof(LocalToWorld)), Writes(typeof(MuzzleFlash), typeof(LightSource), typeof(LocalTransform))] // the camera's pose, read ad hoc
    public sealed class MuzzleFlashSystem : SystemBase, ITick
    {
        private Query _flashes;

        public override void OnCreate() => _flashes = World.Query().With<MuzzleFlash>().With<LightSource>().With<LocalTransform>().Build();

        public void Tick(SystemContext ctx)
        {
            var w = ctx.World;
            var cam = PlayerLink.CameraEntity;
            bool hasCam = !cam.IsNull && w.IsAlive(cam) && w.Has<LocalToWorld>(cam);
            foreach (var chunk in _flashes)
            {
                var flashes = chunk.Get<MuzzleFlash>(); var lights = chunk.Get<LightSource>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var f = ref flashes[i];
                    if (hasCam)
                    {
                        ref readonly var c = ref w.Get<LocalToWorld>(cam);
                        xforms[i].Position = c.Position + c.Forward * 1.5f - Vector3.up * 0.2f;
                    }
                    if (f.Timer > 0f) { f.Timer -= ctx.DeltaTime; lights[i].Intensity = f.Intensity * Mathf.Clamp01(f.Timer / f.Duration + 0.3f); }
                    else lights[i].Intensity = 0f;
                }
            }
        }
    }

    /// <summary>The title's camera (9.3; was the <c>MenuCamera</c> GameObject): stands at the player start and slowly
    /// swings its yaw over the level (attract mode).</summary>
    public struct MenuSwing : IComponent
    {
        public float BaseYaw, Pitch, SwingDegrees, PeriodSeconds;
    }

    public static class MenuViews
    {
        /// <summary>The camera entity behind the title screen.</summary>
        public static Entity Spawn(World w, Vector3 playerStart, Quaternion playerRotation)
        {
            float baseYaw = playerRotation.eulerAngles.y;
            var e = w.CreateEntity();
            w.Add(e, new LocalTransform { Position = playerStart + Vector3.up * 0.6f, Rotation = Quaternion.Euler(3f, baseYaw, 0f), Scale = Vector3.one });
            w.Add(e, new CameraLens { FieldOfView = 72f, Near = 0.05f, Far = 2000f });
            w.Add(e, new MenuSwing { BaseYaw = baseYaw, Pitch = 3f, SwingDegrees = 30f, PeriodSeconds = 28f });
            return e;
        }
    }

    /// <summary>Swings the menu camera. On real time, like the MonoBehaviour it replaces (the clock's absolute value,
    /// so the view does not snap when the title returns).</summary>
    [Exclusive] // Time.unscaledTime: an engine static
    public sealed class MenuSwingSystem : SystemBase, ITick
    {
        private Query _swings;

        public override void OnCreate() => _swings = World.Query().WithReadOnly<MenuSwing>().With<LocalTransform>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _swings)
            {
                var swings = chunk.Get<MenuSwing>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref readonly var s = ref swings[i];
                    float yaw = s.BaseYaw + Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / s.PeriodSeconds) * s.SwingDegrees;
                    xforms[i].Rotation = Quaternion.Euler(s.Pitch, yaw, 0f);
                }
            }
        }
    }
}
