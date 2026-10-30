using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Engine.Graphics.Particles
{
    /// <summary>One live particle (struct array per emitter; removed by swapping with the last one).</summary>
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float age, life;
        public float size0, size1;
        public float rotation, rotationSpeed;
        public float tint;       // per-particle brightness variation (0..1)
        public int frame;        // sprite index (random pick) when not animating
        public byte bounces;
        public bool resting;     // stopped on a surface
    }

    /// <summary>Runtime state of one emitter inside an effect instance.</summary>
    public sealed class EmitterState
    {
        public ParticleEmitter def;
        public Particle[] particles;
        public int count;
        public float emitAccumulator;
        public bool burstDone;
        public bool finishedEmitting;
        public int collisionSpawns;
        public Sprite[] sprites;          // resolved: [sprite, frames...]
        public Texture2D texture;

        public int Capacity => particles.Length;
        public bool HasLiveParticles => count > 0;
    }

    /// <summary>
    /// A playing effect. Created by <see cref="ParticleWorld.Spawn"/>; keep the handle to <see cref="Follow"/> a
    /// transform (trails), <see cref="Stop"/> emitting or <see cref="Kill"/> it outright.
    /// </summary>
    public sealed class ParticleEffectInstance
    {
        public ParticleEffect def;
        public Vector3 position;
        /// <summary>Orientation: forward = the surface normal / firing direction the effect was spawned with.</summary>
        public Quaternion rotation = Quaternion.identity;
        public float scale = 1f;
        /// <summary>Tracer support: emitters with <c>lifetimeFromDistance</c> live distance / speed (&lt;= 0 = off).</summary>
        public float travelDistance = -1f;
        public bool emitting = true;
        public float time;
        public EmitterState[] emitters;
        /// <summary>Optional transform the effect follows (rockets, trails). Stops emitting when it is destroyed.</summary>
        public Transform follow;
        public Vector3 followOffset;
        /// <summary>Rotation applied on top of the followed transform's (trails use a 180° yaw so forward points backwards).</summary>
        public Quaternion followRotation = Quaternion.identity;
        /// <summary>Editor: restart automatically when finished.</summary>
        public bool loop;
        public bool IsAlive { get; internal set; } = true;
        public bool Finished { get; internal set; }
        /// <summary>Set when the instance was culled in the last frame (not drawn).</summary>
        public bool Culled { get; internal set; }

        internal GameObject lightGo;
        internal Light light;
        internal readonly System.Random rng = new System.Random();

        public Vector3 Forward => rotation * Vector3.forward;

        public void Stop() => emitting = false;

        public void Kill()
        {
            emitting = false;
            foreach (var e in emitters) e.count = 0;
            Finished = true;
        }

        public int ParticleCount
        {
            get { int n = 0; foreach (var e in emitters) n += e.count; return n; }
        }

        /// <summary>Makes the effect track a transform (plus an offset in the transform's local space). <paramref name="backwards"/> points the effect's forward against the target's forward (exhaust trails).</summary>
        public ParticleEffectInstance Follow(Transform target, Vector3 localOffset = default, bool backwards = false)
        {
            follow = target; followOffset = localOffset;
            followRotation = backwards ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            if (target != null) { position = target.TransformPoint(localOffset); rotation = target.rotation * followRotation; }
            return this;
        }
    }

    /// <summary>
    /// Owns and simulates every live <see cref="ParticleEffectInstance"/>: emission, motion, optional collision with
    /// the world and the per-effect light flash. Ticked once per frame by the application (scaled time) and drawn
    /// by <see cref="ParticleRenderer"/> in the world pass. Everything here is CPU side and allocation free after
    /// spawn (particles live in fixed arrays sized by the emitter's capacity).
    /// </summary>
    public static class ParticleWorld
    {
        private static readonly List<ParticleEffectInstance> _instances = new List<ParticleEffectInstance>();
        public static IReadOnlyList<ParticleEffectInstance> Instances => _instances;

        /// <summary>Hard cap on simultaneous effects; the oldest finished-or-not instance is dropped beyond it.</summary>
        public static int MaxInstances = 256;
        /// <summary>Layers particles with <c>collide</c> can hit (the game excludes characters / projectiles / triggers).</summary>
        public static int CollisionMask = ~0;
        /// <summary>Global multiplier for particle counts (quality setting; 0.5 halves every burst and rate).</summary>
        public static float Density = 1f;
        /// <summary>Max collision raycasts per frame across all effects; particles beyond the budget skip their test that frame (they may clip for a frame, the world stays smooth).</summary>
        public static int CollisionBudget = 600;
        /// <summary>Collision raycasts performed in the last update.</summary>
        public static int CollisionRaycasts { get; private set; }
        private static int _raycastsThisFrame;
        /// <summary>Stats of the last update.</summary>
        public static int LiveParticles { get; private set; }
        public static int LiveInstances => _instances.Count;

        /// <summary>Falls back to the engine's procedural atlas when a sprite reference is unknown.</summary>
        public static Sprite ResolveSprite(string reference)
        {
            var s = SpriteLibrary.Resolve(reference);
            if (s != null) return s;
            return ParticleTextures.Atlas.TryGet("soft", out var soft) ? soft : null;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>
        /// Starts an effect at <paramref name="position"/> facing <paramref name="forward"/> (the surface normal for impacts,
        /// the firing direction for tracers/muzzle effects). <paramref name="travelDistance"/> feeds emitters with
        /// <c>lifetimeFromDistance</c>.
        /// </summary>
        public static ParticleEffectInstance Spawn(ParticleEffect def, Vector3 position, Vector3 forward, float scale = 1f, float travelDistance = -1f)
        {
            if (def == null) return null;
            if (_instances.Count >= MaxInstances) _instances.RemoveAt(0);
            if (forward.sqrMagnitude < 1E-08f) forward = Vector3.up;
            forward = forward.normalized;
            Vector3 up = MathF.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            var inst = new ParticleEffectInstance
            {
                def = def,
                position = position,
                rotation = Quaternion.LookRotation(forward, up),
                scale = scale * MathF.Max(0.01f, def.scale),
                travelDistance = travelDistance,
            };
            inst.emitters = new EmitterState[def.emitters.Count];
            for (int i = 0; i < def.emitters.Count; i++)
            {
                var e = def.emitters[i];
                var st = new EmitterState { def = e, particles = new Particle[Math.Max(1, (int)MathF.Ceiling(e.Capacity * MathF.Max(0.05f, Density)))] };
                int frameCount = 1 + (e.frames?.Count ?? 0);
                st.sprites = new Sprite[frameCount];
                st.sprites[0] = ResolveSprite(e.sprite);
                for (int f = 1; f < frameCount; f++) st.sprites[f] = ResolveSprite(e.frames[f - 1]) ?? st.sprites[0];
                st.texture = st.sprites[0]?.texture;
                inst.emitters[i] = st;
            }
            if (def.hasLight && def.lightIntensity > 0f && Scene.Current != null)
            {
                inst.lightGo = new GameObject("VfxLight");
                inst.lightGo.transform.position = position + forward * 0.15f;
                inst.light = inst.lightGo.AddComponent<Light>();
                inst.light.type = LightType.Point;
                inst.light.color = def.lightColor;
                inst.light.range = def.lightRange * inst.scale;
                inst.light.intensity = def.lightDelay <= 0f ? def.lightIntensity : 0f;
            }
            _instances.Add(inst);
            return inst;
        }

        public static void Clear()
        {
            foreach (var inst in _instances) DestroyLight(inst);
            _instances.Clear();
            LiveParticles = 0;
        }

        public static void Remove(ParticleEffectInstance inst)
        {
            if (inst == null) return;
            DestroyLight(inst);
            inst.IsAlive = false;
            _instances.Remove(inst);
        }

        private static void DestroyLight(ParticleEffectInstance inst)
        {
            if (inst.lightGo != null && !inst.lightGo.IsDestroyed) GameObject.DestroyImmediate(inst.lightGo);
            inst.lightGo = null; inst.light = null;
        }

        // ------------------------------------------------------------------ simulation

        public static void Update(float dt)
        {
            if (dt <= 0f) { return; }
            _raycastsThisFrame = 0;
            int live = 0;
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                var inst = _instances[i];
                UpdateInstance(inst, dt);
                live += inst.ParticleCount;
                if (inst.Finished)
                {
                    if (inst.loop)
                    {
                        Restart(inst);
                        continue;
                    }
                    DestroyLight(inst);
                    inst.IsAlive = false;
                    _instances.RemoveAt(i);
                }
            }
            LiveParticles = live;
            CollisionRaycasts = _raycastsThisFrame;
        }

        /// <summary>Editor: rewinds the instance to t = 0 (keeps position / orientation).</summary>
        public static void Restart(ParticleEffectInstance inst)
        {
            inst.time = 0f;
            inst.emitting = true;
            inst.Finished = false;
            foreach (var e in inst.emitters) { e.count = 0; e.emitAccumulator = 0f; e.burstDone = false; e.finishedEmitting = false; e.collisionSpawns = 0; }
            if (inst.def.hasLight && inst.light == null && Scene.Current != null)
            {
                inst.lightGo = new GameObject("VfxLight");
                inst.light = inst.lightGo.AddComponent<Light>();
                inst.light.type = LightType.Point;
            }
            if (inst.light != null)
            {
                inst.light.color = inst.def.lightColor;
                inst.light.range = inst.def.lightRange * inst.scale;
                inst.light.intensity = 0f;
            }
        }

        private static void UpdateInstance(ParticleEffectInstance inst, float dt)
        {
            var def = inst.def;
            inst.time += dt;

            // follow target
            if (inst.follow != null)
            {
                if (inst.follow.gameObject.IsDestroyed || !inst.follow.gameObject.activeInHierarchy) { inst.follow = null; inst.emitting = false; }
                else
                {
                    inst.position = inst.follow.TransformPoint(inst.followOffset);
                    inst.rotation = inst.follow.rotation * inst.followRotation;
                }
            }

            // light flash
            if (inst.light != null)
            {
                float lt = inst.time - def.lightDelay;
                float k = def.lightDuration > 0f ? 1f - lt / def.lightDuration : (lt >= 0f ? 0f : 1f);
                inst.light.intensity = lt < 0f ? 0f : def.lightIntensity * Mathf.Clamp01(k);
                inst.lightGo.transform.position = inst.position + inst.Forward * 0.15f;
                if (lt >= def.lightDuration && !inst.loop) DestroyLight(inst);
            }

            bool anyAlive = false, anyEmitting = false;
            for (int ei = 0; ei < inst.emitters.Length; ei++)
            {
                var st = inst.emitters[ei];
                var e = st.def;
                if (!e.enabled) continue;
                Emit(inst, st, dt);
                Simulate(inst, st, dt);
                if (st.count > 0) anyAlive = true;
                if (!st.finishedEmitting) anyEmitting = true;
            }
            if (!anyAlive && !anyEmitting && (inst.light == null || inst.loop)) inst.Finished = true;
            if (!anyAlive && !anyEmitting && inst.light != null && inst.time >= def.lightDelay + def.lightDuration) inst.Finished = true;
        }

        private static void Emit(ParticleEffectInstance inst, EmitterState st, float dt)
        {
            var e = st.def;
            if (st.finishedEmitting) return;
            float local = inst.time - e.delay;
            if (local < 0f) return;
            if (!inst.emitting) { st.finishedEmitting = true; return; }

            if (!st.burstDone)
            {
                st.burstDone = true;
                int lo = Math.Min(e.burstMin, e.burstMax), hi = Math.Max(e.burstMin, e.burstMax);
                int n = lo + inst.rng.Next(hi - lo + 1);
                n = (int)MathF.Round(n * Density);
                for (int i = 0; i < n; i++) SpawnParticle(inst, st);
            }
            if (e.rate > 0f && (e.duration <= 0f || local <= e.duration))
            {
                st.emitAccumulator += e.rate * Density * dt;
                int n = (int)st.emitAccumulator;
                st.emitAccumulator -= n;
                for (int i = 0; i < n; i++) SpawnParticle(inst, st);
            }
            bool rateDone = e.rate <= 0f || (e.duration > 0f && local > e.duration);
            if (rateDone) st.finishedEmitting = true;
        }

        private static void SpawnParticle(ParticleEffectInstance inst, EmitterState st)
        {
            if (st.count >= st.particles.Length) return;
            var e = st.def;
            var rng = inst.rng;
            float s = inst.scale;
            Vector3 fwd = inst.Forward;

            Vector3 localPos = e.offset * s;
            Vector3 dir;
            switch (e.shape)
            {
                case EmitShape.Sphere:
                {
                    dir = RandomUnit(rng);
                    localPos += dir * (e.shapeRadius * s * (float)rng.NextDouble());
                    break;
                }
                case EmitShape.Hemisphere:
                {
                    dir = RandomUnit(rng);
                    if (dir.z < 0f) dir.z = -dir.z;
                    localPos += dir * (e.shapeRadius * s * (float)rng.NextDouble());
                    break;
                }
                case EmitShape.Ring:
                {
                    float a = (float)rng.NextDouble() * MathF.PI * 2f;
                    dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
                    localPos += dir * (e.shapeRadius * s);
                    dir = ConeAround(dir, e.coneAngle, rng);
                    break;
                }
                case EmitShape.Box:
                {
                    localPos += new Vector3(((float)rng.NextDouble() - 0.5f) * e.boxSize.x, ((float)rng.NextDouble() - 0.5f) * e.boxSize.y, ((float)rng.NextDouble() - 0.5f) * e.boxSize.z) * s;
                    dir = ConeAround(Vector3.forward, e.coneAngle, rng);
                    break;
                }
                default: // Cone
                {
                    if (e.shapeRadius > 0f)
                    {
                        float a = (float)rng.NextDouble() * MathF.PI * 2f, r = MathF.Sqrt((float)rng.NextDouble()) * e.shapeRadius * s;
                        localPos += new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0f);
                    }
                    dir = ConeAround(Vector3.forward, e.coneAngle, rng);
                    break;
                }
            }

            Vector3 worldDir = inst.rotation * dir;
            float speed = e.speed.Random(rng) * s;
            float life = e.lifetime.Random(rng);
            if (e.lifetimeFromDistance && inst.travelDistance > 0f) life = inst.travelDistance / MathF.Max(0.01f, speed);
            if (life <= 0.001f) return;

            ref Particle p = ref st.particles[st.count++];
            p.position = inst.position + inst.rotation * localPos;
            p.velocity = worldDir * speed;
            p.age = 0f; p.life = life;
            p.size0 = e.sizeStart.Random(rng) * s;
            p.size1 = p.size0 * e.sizeEndScale;
            p.rotation = e.rotation.Random(rng);
            p.rotationSpeed = e.rotationSpeed.Random(rng);
            p.tint = 1f - e.colorVariation * (float)rng.NextDouble();
            p.frame = st.sprites.Length > 1 && !e.animateFrames ? rng.Next(st.sprites.Length) : 0;
            p.bounces = 0;
            p.resting = false;
        }

        private static void Simulate(ParticleEffectInstance inst, EmitterState st, float dt)
        {
            var e = st.def;
            Vector3 gravity = Physics.gravity * e.gravity + e.constantForce;
            float dragK = MathF.Max(0f, 1f - e.drag * dt);
            var rng = inst.rng;
            for (int i = st.count - 1; i >= 0; i--)
            {
                ref Particle p = ref st.particles[i];
                p.age += dt;
                if (p.age >= p.life) { st.particles[i] = st.particles[--st.count]; continue; }
                p.rotation += p.rotationSpeed * dt;
                if (p.resting) continue;

                p.velocity += gravity * dt;
                if (e.turbulence > 0f) p.velocity += RandomUnit(rng) * (e.turbulence * dt);
                p.velocity *= dragK;
                Vector3 step = p.velocity * dt;
                Vector3 next = p.position + step;

                if (e.collide && _raycastsThisFrame < CollisionBudget)
                {
                    float dist = step.magnitude;
                    _raycastsThisFrame++;
                    if (dist > 1E-06f && Physics.Raycast(p.position, step / dist, out RaycastHit hit, dist + e.collisionRadius, CollisionMask))
                    {
                        if (e.dieOnCollision) { SpawnCollisionEffect(inst, st, hit); st.particles[i] = st.particles[--st.count]; continue; }
                        Vector3 n = hit.normal;
                        next = hit.point + n * e.collisionRadius;
                        float vn = Vector3.Dot(p.velocity, n);
                        Vector3 normalPart = n * vn;
                        Vector3 tangentPart = p.velocity - normalPart;
                        p.velocity = tangentPart * (1f - e.friction) - normalPart * e.bounce;
                        p.bounces++;
                        SpawnCollisionEffect(inst, st, hit);
                        // come to rest on near-horizontal surfaces once the bounce energy is gone
                        if (p.velocity.sqrMagnitude < 0.05f && n.y > 0.5f) { p.velocity = Vector3.zero; p.resting = true; }
                    }
                }
                p.position = next;
            }
        }

        private static void SpawnCollisionEffect(ParticleEffectInstance inst, EmitterState st, in RaycastHit hit)
        {
            var e = st.def;
            if (string.IsNullOrEmpty(e.collisionEffect) || st.collisionSpawns >= e.collisionEffectMax) return;
            var def = VfxLibrary.Get(e.collisionEffect);
            if (def == null || def == inst.def) return; // no recursion into itself
            st.collisionSpawns++;
            Spawn(def, hit.point, hit.normal, inst.scale / MathF.Max(0.01f, inst.def.scale));
        }

        // ------------------------------------------------------------------ helpers

        public static Vector3 RandomUnit(System.Random rng)
        {
            float z = (float)rng.NextDouble() * 2f - 1f;
            float a = (float)rng.NextDouble() * MathF.PI * 2f;
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
        }

        /// <summary>Random direction within <paramref name="angleDeg"/> of <paramref name="axis"/> (uniform over the cap).</summary>
        public static Vector3 ConeAround(Vector3 axis, float angleDeg, System.Random rng)
        {
            if (angleDeg <= 0f) return axis;
            if (angleDeg >= 180f) return RandomUnit(rng);
            float cosMax = MathF.Cos(angleDeg * Mathf.Deg2Rad);
            float cosT = 1f - (float)rng.NextDouble() * (1f - cosMax);
            float sinT = MathF.Sqrt(MathF.Max(0f, 1f - cosT * cosT));
            float phi = (float)rng.NextDouble() * MathF.PI * 2f;
            Vector3 local = new Vector3(sinT * MathF.Cos(phi), sinT * MathF.Sin(phi), cosT);
            if (axis == Vector3.forward) return local;
            Vector3 up = MathF.Abs(axis.y) > 0.99f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(axis, up) * local;
        }
    }
}
