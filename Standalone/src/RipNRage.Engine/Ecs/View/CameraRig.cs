using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.View
{
    /// <summary>
    /// Finds the camera the frame is rendered from — the highest-priority <see cref="CameraLens"/>, the same choice
    /// <see cref="EcsRenderProducer"/> makes — for the engine code that needs the viewpoint during the frame
    /// (ragdoll billboarding, distance fades). Build the query once and keep it; queries are cached plans, not scans.
    /// </summary>
    public static class ActiveCamera
    {
        public static Query BuildQuery(World world) =>
            world.Query().WithReadOnly<CameraLens>().WithReadOnly<LocalToWorld>().Build();

        /// <summary>The active camera's world pose, or false when the world has no camera entity.</summary>
        public static bool TryGet(Query cameras, out LocalToWorld pose, out CameraLens lens)
        {
            pose = default; lens = default;
            int best = int.MinValue;
            foreach (var c in cameras)
            {
                var ls = c.Get<CameraLens>(); var ltw = c.Get<LocalToWorld>();
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i) || ls[i].Priority < best) continue;
                    best = ls[i].Priority; pose = ltw[i]; lens = ls[i];
                }
            }
            return best != int.MinValue;
        }
    }

    /// <summary>
    /// Shake on a camera entity: the weapon kick (an angular offset that decays back to zero) plus a "trauma" shake
    /// (explosions and heavy weapons add trauma, which decays over time and drives smooth noise on both the rotation
    /// and the position). Ported from the legacy <c>CameraRecoil</c> behaviour, numbers unchanged.
    ///
    /// <para>The component owns the entity's whole local pose while it is present: <see cref="CameraShakeSystem"/>
    /// writes <c>LocalTransform.Rotation</c> from the offsets and <c>Position</c> from
    /// <see cref="BasePosition"/> + the shake offset. Anything that wants to move the camera node itself writes
    /// <see cref="BasePosition"/>, not the transform.</para>
    ///
    /// <para>Because of that, <see cref="CameraShake"/> and <see cref="ViewBob"/> belong on different entities: both
    /// write the local position, and the shake, applied second, would win. The player's rig puts the bob on the
    /// camera pivot and the shake on its camera child, exactly as the prefab's HeadBob / CameraRecoil pair was.</para>
    /// </summary>
    public struct CameraShake : IComponent
    {
        // ---- kick (weapon recoil) ----
        /// <summary>Degrees/second the kick is taken up at (a weapon's recoilSpeed overrides it).</summary>
        public float RotationSpeed;
        /// <summary>How fast the kick returns to zero (a weapon's recoilRecoverySpeed overrides it).</summary>
        public float ReturnSpeed;

        // ---- trauma (explosions, heavy weapons) ----
        /// <summary>Trauma lost per second.</summary>
        public float TraumaDecay;
        /// <summary>Degrees of shake at full trauma.</summary>
        public float MaxShakeAngle;
        /// <summary>Metres of shake at full trauma.</summary>
        public float MaxShakeOffset;
        public float ShakeFrequency;

        /// <summary>Local position the shake offsets are added to (the camera node's rest position).</summary>
        public Vector3 BasePosition;

        /// <summary>Current trauma, 0..1. Shake scales with its square, so small hits stay subtle.</summary>
        public float Trauma;
        /// <summary>Noise phase offset, so two cameras never shake in step.</summary>
        public float Seed;

        /// <summary>The kick being aimed for, and the smoothed value actually applied.</summary>
        internal Vector3 Target, Current;

        /// <summary>Adds shake energy (0..1); effects add up, big explosions saturate.</summary>
        public void AddTrauma(float amount) => Trauma = Mathf.Clamp01(Trauma + Mathf.Max(0f, amount));

        /// <summary>A kick of <paramref name="pitch"/> degrees up, with random yaw / roll inside the given bounds.</summary>
        public void Kick(float pitch, float yaw, float roll) =>
            Target += new Vector3(-pitch, Random.Range(-yaw, yaw), Random.Range(-roll, roll));

        /// <summary>Shake from a world event seen from <paramref name="cameraPosition"/>: full strength at the centre, quadratic falloff to zero at <paramref name="radius"/>.</summary>
        public void ShakeAt(Vector3 cameraPosition, Vector3 position, float radius, float strength)
        {
            float d = Vector3.Distance(cameraPosition, position);
            float k = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, radius));
            AddTrauma(strength * k * k);
        }

        /// <summary>Drops the kick and the trauma (the death sequence takes the camera over).</summary>
        public void Clear() { Trauma = 0f; Target = Vector3.zero; Current = Vector3.zero; }

        public static CameraShake Default => new CameraShake
        {
            RotationSpeed = 6f, ReturnSpeed = 25f,
            TraumaDecay = 1.5f, MaxShakeAngle = 5f, MaxShakeOffset = 0.1f, ShakeFrequency = 18f,
            Seed = Random.Range(0f, 100f),
        };
    }

    /// <summary>
    /// Vertical head bob on a camera node: a sine while the owner is moving on the ground, easing back to rest
    /// otherwise, plus whatever vertical <see cref="HeightOffset"/> gameplay asks for (the slide's camera drop).
    /// Ported from the legacy <c>HeadBob</c> behaviour.
    ///
    /// <para>The decisions are gameplay's: it writes <see cref="Active"/> (on the ground, so the bob applies at all)
    /// and <see cref="Moving"/> (there is movement input) each frame. While <see cref="Active"/> is false the node is
    /// left exactly where it is — as the legacy behaviour did in mid-air.</para>
    /// </summary>
    public struct ViewBob : IComponent
    {
        /// <summary>Bob cycles per second (also the rate the bob eases back to rest at).</summary>
        public float Frequency;
        /// <summary>Bob amplitude in metres.</summary>
        public float WaveLength;
        /// <summary>Local position to bob around.</summary>
        public Vector3 BasePosition;
        /// <summary>Extra vertical offset gameplay asks for (negative = crouched).</summary>
        public float HeightOffset;
        /// <summary>Written by gameplay: the bob only applies while this is true (grounded).</summary>
        public bool Active;
        /// <summary>Written by gameplay: there is movement input, so bob rather than settle.</summary>
        public bool Moving;

        internal float Timer;
    }

    /// <summary>
    /// Applies <see cref="ViewBob"/> and <see cref="CameraShake"/> to their entities' <see cref="LocalTransform"/>,
    /// at the end of the frame and before <see cref="TransformSystem"/> builds the world poses — so the camera the
    /// renderer sees is the shaken one, and nothing simulates against a shaking pose.
    ///
    /// <para>The kick is taken up on the fixed step (as the legacy behaviour did in FixedUpdate) so its speed does
    /// not depend on the frame rate; the trauma noise is per frame, because it is a visual.</para>
    /// </summary>
    [UpdateBefore(typeof(TransformSystem))]
    [Reads(typeof(ViewBob)), Writes(typeof(CameraShake), typeof(LocalTransform))]
    public sealed class CameraShakeSystem : SystemBase, IFixedTick, ILateTick
    {
        private Query _shakes, _bobs;

        public override void OnCreate()
        {
            _shakes = World.Query().With<CameraShake>().With<LocalTransform>().Build();
            _bobs = World.Query().With<ViewBob>().With<LocalTransform>().Build();
        }

        /// <summary>Takes up the weapon kick and lets it fall back towards zero.</summary>
        public void FixedTick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            foreach (var chunk in _shakes)
            {
                var shakes = chunk.Get<CameraShake>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var s = ref shakes[i];
                    s.Target = Vector3.Lerp(s.Target, Vector3.zero, s.ReturnSpeed * dt);
                    s.Current = Vector3.Slerp(s.Current, s.Target, s.RotationSpeed * dt);
                }
            }
        }

        public void LateTick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;

            // ---- bob: the node's rest position, plus the crouch offset, plus the walking sine ----
            foreach (var chunk in _bobs)
            {
                var bobs = chunk.Get<ViewBob>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var b = ref bobs[i];
                    if (!b.Active) continue; // in the air: frozen where it is (legacy semantics)
                    ref var lt = ref xforms[i];
                    float rest = b.BasePosition.y + b.HeightOffset;
                    if (b.Moving)
                    {
                        b.Timer += dt * b.Frequency;
                        lt.Position = new Vector3(lt.Position.x, rest + Mathf.Sin(b.Timer) * b.WaveLength, lt.Position.z);
                    }
                    else
                    {
                        b.Timer = 0f;
                        lt.Position = new Vector3(lt.Position.x, Mathf.Lerp(lt.Position.y, rest, dt * b.Frequency), lt.Position.z);
                    }
                }
            }

            // ---- shake: decay the trauma, then write the node's rotation and offset ----
            foreach (var chunk in _shakes)
            {
                var shakes = chunk.Get<CameraShake>(); var xforms = chunk.Get<LocalTransform>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var s = ref shakes[i];
                    ref var lt = ref xforms[i];
                    s.Trauma = Mathf.Max(0f, s.Trauma - s.TraumaDecay * dt);
                    float sh = s.Trauma * s.Trauma;
                    Vector3 shakeRot = Vector3.zero, shakeOff = Vector3.zero;
                    if (sh > 0.0001f)
                    {
                        float t = Time.time * s.ShakeFrequency;
                        shakeRot = new Vector3(Noise(t, s.Seed) * s.MaxShakeAngle * sh,
                                               Noise(t, s.Seed + 11f) * s.MaxShakeAngle * sh,
                                               Noise(t, s.Seed + 23f) * s.MaxShakeAngle * 1.5f * sh);
                        shakeOff = new Vector3(Noise(t, s.Seed + 37f) * s.MaxShakeOffset * sh,
                                               Noise(t, s.Seed + 41f) * s.MaxShakeOffset * sh, 0f);
                    }
                    lt.Rotation = Quaternion.Euler(s.Current + shakeRot);
                    lt.Position = s.BasePosition + shakeOff;
                }
            }
        }

        /// <summary>Smooth -1..1 noise (sum of incommensurate sines) so the shake never looks like a vibration.</summary>
        internal static float Noise(float t, float seed) =>
            (Mathf.Sin(t * 1.0f + seed * 7.3f) + Mathf.Sin(t * 2.31f + seed * 3.1f) * 0.5f + Mathf.Sin(t * 0.47f + seed * 1.7f) * 0.35f) / 1.85f;
    }

    // ---------------------------------------------------------------- configs (JSON prefabs)

    public sealed class CameraShakeConfig : ComponentConfig<CameraShake>
    {
        public float rotationSpeed = 6f, returnSpeed = 25f;
        public float traumaDecay = 1.5f, maxShakeAngle = 5f, maxShakeOffset = 0.1f, shakeFrequency = 18f;
        public float[] basePosition = { 0, 0, 0 };
        public override CameraShake Bake()
        {
            var s = CameraShake.Default;
            s.RotationSpeed = rotationSpeed; s.ReturnSpeed = returnSpeed;
            s.TraumaDecay = traumaDecay; s.MaxShakeAngle = maxShakeAngle; s.MaxShakeOffset = maxShakeOffset; s.ShakeFrequency = shakeFrequency;
            s.BasePosition = new Vector3(basePosition[0], basePosition[1], basePosition[2]);
            return s;
        }
    }

    public sealed class ViewBobConfig : ComponentConfig<ViewBob>
    {
        public float frequency = 15f, waveLength = 0.1f;
        public float[] basePosition = { 0, 0, 0 };
        public override ViewBob Bake() => new ViewBob
        {
            Frequency = frequency, WaveLength = waveLength,
            BasePosition = new Vector3(basePosition[0], basePosition[1], basePosition[2]),
        };
    }
}
