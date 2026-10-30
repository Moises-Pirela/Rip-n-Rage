using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RipNRage.Engine.Graphics.Particles
{
    /// <summary>How a particle quad is blended over the scene.</summary>
    public enum ParticleBlend { Alpha, Additive, Multiply }

    /// <summary>How a particle quad is oriented.</summary>
    public enum ParticleRenderMode
    {
        /// <summary>Faces the camera (smoke, glows, blood puffs).</summary>
        Billboard,
        /// <summary>Stretched along its velocity; length = size × stretchLength + speed × stretchVelocity (sparks, tracers, streaks).</summary>
        Stretched,
        /// <summary>Flat on the ground plane (dust rings, pools).</summary>
        Horizontal,
        /// <summary>Flat on the surface the effect was spawned on — the effect's forward is the surface normal (impact flashes, splat decals, scorch marks).</summary>
        Aligned,
    }

    /// <summary>Where particles start and which way they go. "Forward" is the effect's forward axis (the surface normal / firing direction).</summary>
    public enum EmitShape
    {
        /// <summary>At the origin, inside a cone of <c>coneAngle</c> around forward.</summary>
        Cone,
        /// <summary>Random direction, positioned inside a sphere of <c>shapeRadius</c>.</summary>
        Sphere,
        /// <summary>Random direction on the forward side (away from the surface).</summary>
        Hemisphere,
        /// <summary>Outwards in the plane perpendicular to forward, starting on a circle of <c>shapeRadius</c> (shockwaves, dust rings).</summary>
        Ring,
        /// <summary>Inside a box of <c>boxSize</c> (effect space), moving within the cone around forward.</summary>
        Box,
    }

    /// <summary>Editor hint: slider range and drag step for a numeric field.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VfxRangeAttribute : Attribute
    {
        public readonly float min, max, step;
        public VfxRangeAttribute(float min, float max, float step = 0.01f) { this.min = min; this.max = max; this.step = step; }
    }

    /// <summary>Editor hint: starts a new section in the property grid.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VfxGroupAttribute : Attribute
    {
        public readonly string name;
        public VfxGroupAttribute(string name) { this.name = name; }
    }

    /// <summary>Editor hint: the string field holds a sprite reference ("Atlas/name") and gets the sprite palette.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VfxSpriteAttribute : Attribute { }

    /// <summary>Editor hint: the string field names another effect.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VfxEffectRefAttribute : Attribute { }

    /// <summary>Editor hint: one-line description shown as a tooltip / help text.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VfxTipAttribute : Attribute
    {
        public readonly string text;
        public VfxTipAttribute(string text) { this.text = text; }
    }

    /// <summary>Inclusive random range.</summary>
    [Serializable]
    public struct FloatRange
    {
        public float min, max;
        public FloatRange(float min, float max) { this.min = min; this.max = max; }
        public FloatRange(float value) { min = max = value; }
        public float Random(System.Random rng) => min + (float)rng.NextDouble() * (max - min);
        public float Max => MathF.Max(min, max);
        public override string ToString() => $"{min:0.##}..{max:0.##}";
    }

    /// <summary>
    /// One stream of particles inside a <see cref="ParticleEffect"/>: what it looks like (sprite, blend, orientation),
    /// how many and when (burst / rate / delay), where they start (shape), how they move (speed, gravity, drag,
    /// collision) and how they change over their life (size, colour, rotation).
    /// All sizes and speeds are in metres and seconds; positions are in effect space (X right, Y up, Z forward = the
    /// surface normal or firing direction the effect was spawned with).
    /// </summary>
    [Serializable]
    public sealed class ParticleEmitter
    {
        public string name = "emitter";
        public bool enabled = true;

        [VfxGroup("Look"), VfxSprite, VfxTip("Atlas/sprite drawn for every particle (Particles/* are the built-in procedural shapes)")]
        public string sprite = "Particles/soft";
        [VfxTip("Extra sprites: a random one per particle, or played in order over the lifetime when animateFrames is on")]
        public List<string> frames = new List<string>();
        public bool animateFrames;
        public ParticleBlend blend = ParticleBlend.Alpha;
        public ParticleRenderMode renderMode = ParticleRenderMode.Billboard;
        [VfxTip("Shaded by the sun, ambient and point lights (smoke, debris); off = full bright (sparks, fire)")]
        public bool lit;
        [VfxRange(-10, 10, 1), VfxTip("Draw order among the emitters of one effect (higher draws later / on top)")]
        public int sortOrder;

        [VfxGroup("Emission"), VfxRange(0, 5, 0.01f), VfxTip("Seconds after the effect starts before this emitter begins")]
        public float delay;
        [VfxRange(0, 500, 1), VfxTip("Particles emitted at once when the emitter begins")]
        public int burstMin = 8;
        [VfxRange(0, 500, 1)]
        public int burstMax = 12;
        [VfxRange(0, 2000, 1), VfxTip("Continuous emission, particles per second")]
        public float rate;
        [VfxRange(0, 10, 0.05f), VfxTip("How long the continuous emission lasts; 0 = until the effect is stopped (trails)")]
        public float duration;
        public EmitShape shape = EmitShape.Cone;
        [VfxRange(0, 180, 1), VfxTip("Half-angle of the cone around forward (degrees); 180 = every direction")]
        public float coneAngle = 30f;
        [VfxRange(0, 10, 0.01f), VfxTip("Radius of the sphere / hemisphere / ring / cone disc particles start on")]
        public float shapeRadius;
        public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f);
        [VfxTip("Start offset in effect space (Z = along the surface normal; 0.01 lifts decals off the wall)")]
        public Vector3 offset;

        [VfxGroup("Motion")]
        public FloatRange lifetime = new FloatRange(0.3f, 0.6f);
        public FloatRange speed = new FloatRange(2f, 5f);
        [VfxRange(-5, 5, 0.05f), VfxTip("Multiplier of 9.81 m/s² downwards; negative floats upwards")]
        public float gravity = 1f;
        [VfxRange(0, 20, 0.1f), VfxTip("Velocity damping per second (smoke ~2, sparks ~0.5)")]
        public float drag;
        [VfxTip("Constant acceleration in world space (wind, updraft)")]
        public Vector3 constantForce;
        [VfxRange(0, 20, 0.1f), VfxTip("Random velocity jitter, m/s² (smoke wobble)")]
        public float turbulence;
        [VfxTip("Lifetime = travel distance / speed when the effect was spawned as a tracer (SpawnTracer)")]
        public bool lifetimeFromDistance;

        [VfxGroup("Size"), VfxTip("Quad width in metres at birth")]
        public FloatRange sizeStart = new FloatRange(0.1f, 0.2f);
        [VfxRange(0, 10, 0.05f), VfxTip("Size at death relative to the start size (0 = shrinks away, 3 = grows)")]
        public float sizeEndScale = 1f;
        [VfxRange(0.05f, 10, 0.05f), VfxTip("Height / width of the quad")]
        public float aspect = 1f;
        [VfxRange(0, 20, 0.1f), VfxTip("Stretched mode: length = size × this …")]
        public float stretchLength = 1f;
        [VfxRange(0, 2, 0.01f), VfxTip("… + speed × this (motion blur look)")]
        public float stretchVelocity = 0.05f;

        [VfxGroup("Rotation"), VfxTip("Initial roll in degrees")]
        public FloatRange rotation = new FloatRange(0f, 360f);
        [VfxTip("Roll speed, degrees per second (set min negative for both directions)")]
        public FloatRange rotationSpeed = new FloatRange(0f, 0f);

        [VfxGroup("Colour")]
        public Color colorStart = Color.white;
        public Color colorEnd = new Color(1f, 1f, 1f, 0f);
        [VfxRange(0, 1, 0.01f), VfxTip("Fraction of the lifetime over which alpha ramps up from 0")]
        public float fadeIn;
        [VfxRange(0, 8, 0.05f), VfxTip("Colour multiplier (> 1 blooms with HDR post-fx)")]
        public float brightness = 1f;
        [VfxRange(0, 1, 0.01f), VfxTip("Per-particle random darkening (0 = all identical)")]
        public float colorVariation;

        [VfxGroup("Collision"), VfxTip("Particles bounce off / stick to world geometry (a raycast per particle per frame — keep counts low)")]
        public bool collide;
        [VfxRange(0, 1, 0.01f)]
        public float bounce = 0.3f;
        [VfxRange(0, 1, 0.01f)]
        public float friction = 0.6f;
        [VfxRange(0, 0.5f, 0.005f)]
        public float collisionRadius = 0.02f;
        public bool dieOnCollision;
        [VfxEffectRef, VfxTip("Effect spawned where a particle hits (blood drop → splat)")]
        public string collisionEffect;
        [VfxRange(0, 64, 1), VfxTip("Cap on collision effects per emitter instance")]
        public int collisionEffectMax = 6;

        public ParticleEmitter Clone()
        {
            var c = (ParticleEmitter)MemberwiseClone();
            c.frames = frames != null ? new List<string>(frames) : new List<string>();
            return c;
        }

        /// <summary>Particles this emitter can have alive at once (burst + rate × lifetime).</summary>
        public int Capacity
        {
            get
            {
                int burst = Math.Max(burstMin, burstMax);
                int streaming = rate > 0f ? (int)MathF.Ceiling(rate * lifetime.Max) + 2 : 0;
                return Math.Clamp(burst + streaming, 1, 4096);
            }
        }

        /// <summary>Seconds from the effect start until this emitter's last particle can die (0 = open ended).</summary>
        public float EndTime
        {
            get
            {
                if (rate > 0f && duration <= 0f) return 0f;
                float emitEnd = delay + (rate > 0f ? duration : 0f);
                return emitEnd + lifetime.Max;
            }
        }
    }

    /// <summary>
    /// A named, data-driven visual effect: a set of emitters plus an optional light flash. Saved as <c>vfx/&lt;name&gt;.json</c>
    /// (see <see cref="VfxLibrary"/>) and played through <see cref="ParticleWorld.Spawn"/>.
    /// </summary>
    [Serializable]
    public sealed class ParticleEffect
    {
        public string name = "effect";
        public string description;
        public List<ParticleEmitter> emitters = new List<ParticleEmitter>();

        [VfxGroup("Light")]
        public bool hasLight;
        public Color lightColor = new Color(1f, 0.8f, 0.5f, 1f);
        [VfxRange(0, 30, 0.1f)]
        public float lightIntensity = 3f;
        [VfxRange(0, 60, 0.5f)]
        public float lightRange = 8f;
        [VfxRange(0, 5, 0.01f)]
        public float lightDuration = 0.15f;
        [VfxRange(0, 5, 0.01f)]
        public float lightDelay;

        [VfxGroup("General"), VfxRange(0.1f, 100, 0.1f), VfxTip("Bounding radius (metres) used for frustum culling")]
        public float cullRadius = 4f;
        [VfxRange(0.05f, 10, 0.05f), VfxTip("Multiplies every size, speed and offset of the effect")]
        public float scale = 1f;

        /// <summary>Seconds until the whole effect is over (0 = open ended: has a trail-style emitter).</summary>
        [JsonIgnore]
        public float Duration
        {
            get
            {
                float t = hasLight ? lightDelay + lightDuration : 0f;
                foreach (var e in emitters)
                {
                    if (e == null || !e.enabled) continue;
                    float end = e.EndTime;
                    if (end <= 0f) return 0f;
                    t = MathF.Max(t, end);
                }
                return t;
            }
        }

        public ParticleEffect Clone()
        {
            var c = (ParticleEffect)MemberwiseClone();
            c.emitters = new List<ParticleEmitter>();
            foreach (var e in emitters) c.emitters.Add(e.Clone());
            return c;
        }
    }
}
