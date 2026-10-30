using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics.Particles;

namespace RipNRage.Game.Content.Vfx
{
    /// <summary>
    /// The code-built base library of effects — blood, impacts per material, explosions, tracers per calibre,
    /// trails. These are the starting point: on first run they are written to <c>vfx/*.json</c> (one file per
    /// effect) and from then on the files are what the game loads and the VFX editor edits.
    /// Sizes are metres; the effect's forward axis is the surface normal (impacts), the firing direction
    /// (tracers) or backwards along the projectile (trails).
    /// </summary>
    public static class DefaultEffects
    {
        private static FloatRange R(float a, float b) => new FloatRange(a, b);
        private static FloatRange R(float v) => new FloatRange(v, v);
        private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        private static ParticleEmitter E(string name, string sprite, Action<ParticleEmitter> setup)
        {
            var e = new ParticleEmitter { name = name, sprite = sprite };
            setup(e);
            return e;
        }

        private static ParticleEffect Effect(string name, string description, float cullRadius, params ParticleEmitter[] emitters)
        {
            var fx = new ParticleEffect { name = name, description = description, cullRadius = cullRadius };
            fx.emitters.AddRange(emitters);
            return fx;
        }

        /// <summary>Registers every default effect with <see cref="VfxLibrary"/> (files loaded afterwards override them).</summary>
        public static void RegisterAll()
        {
            foreach (var fx in Build()) VfxLibrary.Register(fx);
        }

        public static List<ParticleEffect> Build()
        {
            var list = new List<ParticleEffect>();

            // ------------------------------------------------------------ blood
            list.Add(Effect("blood_hit", "Bullet hit on a living thing: spray of drops (splats where they land), mist and a puff.", 3f,
                E("spray", "Particles/drop", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.burstMin = 10; e.burstMax = 16;
                    e.shape = EmitShape.Cone; e.coneAngle = 55f; e.speed = R(2f, 6f); e.gravity = 1.2f; e.drag = 0.4f;
                    e.lifetime = R(0.35f, 0.7f); e.sizeStart = R(0.04f, 0.09f); e.sizeEndScale = 0.6f; e.stretchLength = 1.4f; e.stretchVelocity = 0.04f;
                    e.colorStart = C(0.55f, 0.02f, 0.02f); e.colorEnd = C(0.3f, 0f, 0f, 0.8f);
                    e.collide = true; e.dieOnCollision = true; e.collisionEffect = "blood_splat"; e.collisionEffectMax = 3;
                }),
                E("mist", "Particles/smoke", e =>
                {
                    e.lit = true; e.burstMin = 3; e.burstMax = 4; e.coneAngle = 60f; e.speed = R(0.5f, 1.5f); e.gravity = 0.1f; e.drag = 2f;
                    e.lifetime = R(0.4f, 0.7f); e.sizeStart = R(0.25f, 0.4f); e.sizeEndScale = 2.2f; e.rotation = R(0f, 360f); e.rotationSpeed = R(-40f, 40f);
                    e.colorStart = C(0.5f, 0.03f, 0.03f, 0.55f); e.colorEnd = C(0.35f, 0f, 0f, 0f);
                }),
                E("puff", "Particles/soft", e =>
                {
                    e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.12f); e.sizeStart = R(0.35f); e.sizeEndScale = 1.6f;
                    e.colorStart = C(0.8f, 0.05f, 0.05f, 0.8f); e.colorEnd = C(0.5f, 0f, 0f, 0f); e.sortOrder = 1;
                })));

            list.Add(Effect("blood_splat", "Decal left where a blood drop lands (spawned by the blood emitters' collision).", 1f,
                E("splat", "Particles/splat", e =>
                {
                    e.renderMode = ParticleRenderMode.Aligned; e.lit = true; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f;
                    e.lifetime = R(12f, 18f); e.sizeStart = R(0.12f, 0.28f); e.offset = new Vector3(0f, 0f, 0.01f);
                    e.colorStart = C(0.45f, 0.01f, 0.01f, 0.95f); e.colorEnd = C(0.3f, 0f, 0f, 0f);
                })));

            list.Add(Effect("blood_death", "Enemy death: big burst of drops and gibs, a dark cloud.", 5f,
                E("burst", "Particles/drop", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.burstMin = 30; e.burstMax = 40; e.shape = EmitShape.Sphere; e.shapeRadius = 0.3f;
                    e.speed = R(2f, 7f); e.gravity = 1.3f; e.drag = 0.3f; e.lifetime = R(0.5f, 1f); e.sizeStart = R(0.05f, 0.1f); e.sizeEndScale = 0.7f;
                    e.stretchLength = 1.4f; e.stretchVelocity = 0.04f; e.colorStart = C(0.55f, 0.02f, 0.02f); e.colorEnd = C(0.3f, 0f, 0f, 0.8f);
                    e.collide = true; e.dieOnCollision = true; e.collisionEffect = "blood_splat"; e.collisionEffectMax = 8;
                }),
                E("gibs", "Particles/chunk", e =>
                {
                    e.lit = true; e.burstMin = 6; e.burstMax = 10; e.shape = EmitShape.Sphere; e.shapeRadius = 0.2f; e.speed = R(3f, 6f); e.gravity = 1.5f;
                    e.lifetime = R(2f, 3f); e.sizeStart = R(0.08f, 0.15f); e.rotationSpeed = R(-400f, 400f);
                    e.colorStart = C(0.45f, 0.04f, 0.04f); e.colorEnd = C(0.35f, 0.02f, 0.02f, 0f);
                    e.collide = true; e.bounce = 0.3f; e.friction = 0.7f; e.collisionEffect = "blood_splat"; e.collisionEffectMax = 4;
                }),
                E("cloud", "Particles/smoke", e =>
                {
                    e.lit = true; e.burstMin = 6; e.burstMax = 8; e.shape = EmitShape.Sphere; e.shapeRadius = 0.3f; e.speed = R(0.6f, 1.6f); e.gravity = 0.05f; e.drag = 2f;
                    e.lifetime = R(0.8f, 1.2f); e.sizeStart = R(0.4f, 0.6f); e.sizeEndScale = 2.5f; e.rotationSpeed = R(-30f, 30f);
                    e.colorStart = C(0.45f, 0.03f, 0.03f, 0.6f); e.colorEnd = C(0.3f, 0f, 0f, 0f);
                })));

            // ------------------------------------------------------------ impacts per material
            list.Add(Effect("impact_concrete", "Bullet on stone / concrete: flash, sparks, chips, dust and a dark bullet hole.", 2.5f,
                Flash(C(1f, 0.9f, 0.7f, 0.9f), 0.2f),
                E("sparks", "Particles/spark", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 4; e.burstMax = 8; e.coneAngle = 55f;
                    e.speed = R(4f, 9f); e.gravity = 1f; e.drag = 0.3f; e.lifetime = R(0.15f, 0.4f); e.sizeStart = R(0.015f, 0.03f); e.stretchLength = 2f; e.stretchVelocity = 0.06f;
                    e.colorStart = C(1f, 0.85f, 0.5f); e.colorEnd = C(1f, 0.4f, 0.1f, 0f); e.collide = true; e.bounce = 0.4f; e.friction = 0.3f;
                }),
                E("chips", "Particles/chunk", e =>
                {
                    e.lit = true; e.burstMin = 5; e.burstMax = 9; e.coneAngle = 60f; e.speed = R(1.5f, 4f); e.gravity = 1.5f; e.lifetime = R(0.6f, 1.2f);
                    e.sizeStart = R(0.02f, 0.05f); e.rotationSpeed = R(-500f, 500f); e.colorStart = C(0.6f, 0.58f, 0.55f); e.colorEnd = C(0.6f, 0.58f, 0.55f, 0f);
                    e.collide = true; e.bounce = 0.3f; e.friction = 0.6f;
                }),
                Dust(C(0.75f, 0.72f, 0.68f, 0.5f), 3, 5, 0.15f, 0.25f),
                Mark("Particles/dot", C(0.12f, 0.12f, 0.12f, 0.85f), 0.05f, 0.07f)));

            list.Add(Effect("impact_wood", "Bullet on wood: splinters, brown dust, a dark hole.", 2.5f,
                Flash(C(1f, 0.85f, 0.6f, 0.7f), 0.14f),
                E("splinters", "Particles/splinter", e =>
                {
                    e.lit = true; e.burstMin = 6; e.burstMax = 10; e.coneAngle = 50f; e.speed = R(2f, 5f); e.gravity = 1.4f; e.lifetime = R(0.7f, 1.3f);
                    e.sizeStart = R(0.05f, 0.12f); e.aspect = 1f; e.rotationSpeed = R(-600f, 600f); e.colorStart = C(0.6f, 0.42f, 0.22f); e.colorEnd = C(0.5f, 0.35f, 0.18f, 0f);
                    e.collide = true; e.bounce = 0.2f; e.friction = 0.7f;
                }),
                E("sparks", "Particles/spark", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 0; e.burstMax = 2; e.coneAngle = 40f;
                    e.speed = R(3f, 6f); e.gravity = 1f; e.lifetime = R(0.1f, 0.25f); e.sizeStart = R(0.012f, 0.02f); e.stretchLength = 2f; e.stretchVelocity = 0.05f;
                    e.colorStart = C(1f, 0.8f, 0.4f); e.colorEnd = C(1f, 0.4f, 0.1f, 0f);
                }),
                Dust(C(0.55f, 0.42f, 0.28f, 0.5f), 3, 4, 0.12f, 0.2f),
                Mark("Particles/dot", C(0.1f, 0.07f, 0.04f, 0.9f), 0.05f, 0.07f)));

            list.Add(Effect("impact_metal", "Bullet on metal: bright flash, lots of ricocheting sparks, a grey scuff.", 3f,
                Flash(C(1f, 0.95f, 0.85f), 0.28f),
                E("sparks", "Particles/spark", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 10; e.burstMax = 16; e.coneAngle = 65f;
                    e.speed = R(5f, 12f); e.gravity = 1f; e.drag = 0.2f; e.lifetime = R(0.2f, 0.55f); e.sizeStart = R(0.015f, 0.03f); e.stretchLength = 2.5f; e.stretchVelocity = 0.07f;
                    e.colorStart = C(1f, 0.95f, 0.7f); e.colorEnd = C(1f, 0.5f, 0.15f, 0f); e.collide = true; e.bounce = 0.6f; e.friction = 0.2f;
                }),
                E("ring", "Particles/ring", e =>
                {
                    e.renderMode = ParticleRenderMode.Aligned; e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f;
                    e.lifetime = R(0.12f); e.sizeStart = R(0.1f); e.sizeEndScale = 4f; e.offset = new Vector3(0f, 0f, 0.02f);
                    e.colorStart = C(1f, 0.9f, 0.7f, 0.8f); e.colorEnd = C(1f, 0.7f, 0.4f, 0f);
                }),
                Dust(C(0.6f, 0.6f, 0.62f, 0.3f), 1, 2, 0.1f, 0.16f),
                Mark("Particles/dot", C(0.45f, 0.45f, 0.48f, 0.8f), 0.04f, 0.06f)));

            list.Add(Effect("impact_glass", "Bullet on glass: glittering shards and a sharp glint.", 3f,
                E("glint", "Particles/flare", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.08f); e.sizeStart = R(0.3f); e.sizeEndScale = 1.4f;
                    e.colorStart = C(0.85f, 0.95f, 1f); e.colorEnd = C(0.6f, 0.8f, 1f, 0f);
                }),
                E("shards", "Particles/shard", e =>
                {
                    e.lit = true; e.burstMin = 10; e.burstMax = 16; e.coneAngle = 70f; e.speed = R(1.5f, 5f); e.gravity = 1.5f; e.lifetime = R(0.8f, 1.6f);
                    e.sizeStart = R(0.03f, 0.09f); e.rotationSpeed = R(-700f, 700f); e.colorStart = C(0.8f, 0.9f, 1f, 0.85f); e.colorEnd = C(0.8f, 0.9f, 1f, 0f);
                    e.collide = true; e.bounce = 0.25f; e.friction = 0.5f;
                }),
                E("sparkle", "Particles/dot", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = 6; e.burstMax = 10; e.coneAngle = 80f; e.speed = R(1f, 3f); e.gravity = 1.5f; e.lifetime = R(0.3f, 0.7f);
                    e.sizeStart = R(0.01f, 0.02f); e.colorStart = C(1f, 1f, 1f); e.colorEnd = C(0.7f, 0.9f, 1f, 0f);
                })));

            list.Add(Effect("impact_sand", "Bullet on sand / dirt: a grainy puff and a spray of grains.", 2.5f,
                E("grains", "Particles/dot", e =>
                {
                    e.lit = true; e.burstMin = 10; e.burstMax = 16; e.coneAngle = 45f; e.speed = R(2f, 5f); e.gravity = 1.6f; e.lifetime = R(0.4f, 0.8f);
                    e.sizeStart = R(0.015f, 0.03f); e.colorStart = C(0.78f, 0.68f, 0.5f); e.colorEnd = C(0.7f, 0.6f, 0.45f, 0f); e.collide = true; e.dieOnCollision = true;
                }),
                Dust(C(0.8f, 0.7f, 0.5f, 0.65f), 5, 8, 0.2f, 0.35f)));

            list.Add(Effect("impact_water", "Bullet on water: splash column, expanding ring and mist.", 3f,
                E("splash", "Particles/drop", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.burstMin = 12; e.burstMax = 18; e.coneAngle = 22f; e.speed = R(3f, 6.5f); e.gravity = 1.2f;
                    e.lifetime = R(0.4f, 0.75f); e.sizeStart = R(0.04f, 0.08f); e.stretchLength = 1.5f; e.stretchVelocity = 0.03f;
                    e.colorStart = C(0.75f, 0.88f, 1f, 0.85f); e.colorEnd = C(0.7f, 0.85f, 1f, 0f);
                }),
                E("ring", "Particles/ring", e =>
                {
                    e.renderMode = ParticleRenderMode.Aligned; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.6f); e.sizeStart = R(0.15f); e.sizeEndScale = 6f;
                    e.offset = new Vector3(0f, 0f, 0.02f); e.colorStart = C(0.9f, 0.95f, 1f, 0.7f); e.colorEnd = C(0.9f, 0.95f, 1f, 0f);
                }),
                E("mist", "Particles/soft", e =>
                {
                    e.burstMin = 2; e.burstMax = 3; e.coneAngle = 30f; e.speed = R(0.5f, 1.2f); e.gravity = 0.2f; e.drag = 2f; e.lifetime = R(0.4f, 0.7f);
                    e.sizeStart = R(0.2f, 0.3f); e.sizeEndScale = 2.5f; e.colorStart = C(0.9f, 0.95f, 1f, 0.45f); e.colorEnd = C(0.9f, 0.95f, 1f, 0f);
                })));

            list.Add(Effect("impact_energy", "Energy bolt hit: cyan flash, crackling arcs and a scorch.", 2.5f,
                Flash(C(0.6f, 0.9f, 1f), 0.3f),
                E("arcs", "Particles/spark", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 8; e.burstMax = 12; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.05f;
                    e.speed = R(1f, 3f); e.gravity = 0f; e.drag = 3f; e.lifetime = R(0.15f, 0.35f); e.sizeStart = R(0.02f, 0.04f); e.stretchLength = 3f; e.stretchVelocity = 0.08f;
                    e.colorStart = C(0.7f, 0.95f, 1f); e.colorEnd = C(0.4f, 0.5f, 1f, 0f);
                }),
                Mark("Particles/scorch", C(0.05f, 0.05f, 0.08f, 0.85f), 0.12f, 0.16f)));

            // ------------------------------------------------------------ explosions
            var rocket = Effect("explosion_rocket", "Rocket detonation: animated fireball, flash, fire licks, smoke, sparks, debris, shockwave and scorch.", 14f,
                E("fireball", "RocketLauncher/explosion_01", e =>
                {
                    e.frames = new List<string> { "RocketLauncher/explosion_02", "RocketLauncher/explosion_03" }; e.animateFrames = true;
                    e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.45f); e.sizeStart = R(3.2f, 3.6f); e.sizeEndScale = 1.25f;
                    e.offset = new Vector3(0f, 0f, 1.2f); e.colorStart = C(1f, 1f, 1f); e.colorEnd = C(1f, 0.6f, 0.3f, 0f); e.sortOrder = 3;
                }),
                E("flash", "Particles/flare", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.12f); e.sizeStart = R(4f); e.sizeEndScale = 1.5f;
                    e.offset = new Vector3(0f, 0f, 1f); e.colorStart = C(1f, 0.9f, 0.6f); e.colorEnd = C(1f, 0.6f, 0.3f, 0f); e.sortOrder = 4;
                }),
                E("fire", "Particles/fire", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = 10; e.burstMax = 14; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.6f; e.speed = R(2f, 5f); e.gravity = -0.3f; e.drag = 2.5f;
                    e.lifetime = R(0.3f, 0.6f); e.sizeStart = R(0.6f, 1f); e.sizeEndScale = 1.8f; e.rotation = R(-20f, 20f); e.rotationSpeed = R(-90f, 90f);
                    e.colorStart = C(1f, 0.7f, 0.3f); e.colorEnd = C(0.6f, 0.1f, 0f, 0f); e.sortOrder = 2;
                }),
                E("smoke", "Particles/smoke", e =>
                {
                    e.lit = true; e.delay = 0.05f; e.burstMin = 8; e.burstMax = 12; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.8f; e.speed = R(1.5f, 3.5f); e.gravity = -0.15f; e.drag = 1.5f; e.turbulence = 1.5f;
                    e.lifetime = R(1.4f, 2.4f); e.sizeStart = R(0.9f, 1.4f); e.sizeEndScale = 2.8f; e.rotationSpeed = R(-25f, 25f); e.fadeIn = 0.05f;
                    e.colorStart = C(0.25f, 0.23f, 0.22f, 0.75f); e.colorEnd = C(0.3f, 0.3f, 0.3f, 0f); e.sortOrder = 1;
                }),
                E("sparks", "Particles/spark", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 25; e.burstMax = 35; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.3f;
                    e.speed = R(8f, 18f); e.gravity = 1.2f; e.drag = 0.8f; e.lifetime = R(0.4f, 0.9f); e.sizeStart = R(0.03f, 0.06f); e.stretchLength = 2f; e.stretchVelocity = 0.05f;
                    e.colorStart = C(1f, 0.8f, 0.4f); e.colorEnd = C(1f, 0.3f, 0f, 0f); e.collide = true; e.bounce = 0.5f; e.friction = 0.3f;
                }),
                E("debris", "Particles/chunk", e =>
                {
                    e.lit = true; e.burstMin = 10; e.burstMax = 16; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.3f; e.speed = R(5f, 12f); e.gravity = 1.6f;
                    e.lifetime = R(1.2f, 2.2f); e.sizeStart = R(0.08f, 0.2f); e.rotationSpeed = R(-600f, 600f); e.colorStart = C(0.35f, 0.32f, 0.3f); e.colorEnd = C(0.35f, 0.32f, 0.3f, 0f);
                    e.collide = true; e.bounce = 0.35f; e.friction = 0.6f;
                }),
                E("shockwave", "Particles/ring", e =>
                {
                    e.renderMode = ParticleRenderMode.Aligned; e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f;
                    e.lifetime = R(0.35f); e.sizeStart = R(0.8f); e.sizeEndScale = 12f; e.offset = new Vector3(0f, 0f, 0.05f);
                    e.colorStart = C(1f, 0.8f, 0.6f, 0.6f); e.colorEnd = C(1f, 0.6f, 0.3f, 0f);
                }),
                E("scorch", "Particles/scorch", e =>
                {
                    e.renderMode = ParticleRenderMode.Aligned; e.lit = true; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(25f);
                    e.sizeStart = R(3f, 3.6f); e.offset = new Vector3(0f, 0f, 0.02f); e.colorStart = C(0.05f, 0.04f, 0.04f, 0.9f); e.colorEnd = C(0.05f, 0.04f, 0.04f, 0f); e.sortOrder = -1;
                }));
            rocket.hasLight = true; rocket.lightColor = C(1f, 0.6f, 0.25f); rocket.lightIntensity = 8f; rocket.lightRange = 30f; rocket.lightDuration = 0.35f;
            list.Add(rocket);

            // ------------------------------------------------------------ trails
            var trail = Effect("rocket_trail", "Smoke and exhaust glow following a rocket (spawned with Follow; open ended until stopped).", 6f,
                E("smoke", "Particles/smoke", e =>
                {
                    e.lit = true; e.burstMin = e.burstMax = 0; e.rate = 60f; e.duration = 0f; e.coneAngle = 10f; e.speed = R(1f, 2f); e.gravity = -0.05f; e.drag = 1f;
                    e.lifetime = R(0.6f, 1f); e.sizeStart = R(0.15f, 0.25f); e.sizeEndScale = 3.5f; e.rotationSpeed = R(-40f, 40f); e.fadeIn = 0.05f;
                    e.colorStart = C(0.8f, 0.78f, 0.75f, 0.6f); e.colorEnd = C(0.6f, 0.6f, 0.6f, 0f);
                }),
                E("exhaust", "Particles/soft", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 0; e.rate = 40f; e.duration = 0f; e.coneAngle = 8f; e.speed = R(0.5f, 1f); e.gravity = 0f;
                    e.lifetime = R(0.08f, 0.15f); e.sizeStart = R(0.2f); e.sizeEndScale = 0.4f; e.colorStart = C(1f, 0.7f, 0.3f); e.colorEnd = C(1f, 0.3f, 0f, 0f); e.sortOrder = 1;
                }));
            list.Add(trail);

            // ------------------------------------------------------------ enemy readability
            list.Add(Effect("enemy_aim", "Attack telegraph at an enemy's muzzle: an orange glow that swells for the wind-up, with a few sparks pulled inwards.", 2f,
                E("glow", "Particles/soft", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.35f);
                    e.sizeStart = R(0.12f); e.sizeEndScale = 3.2f; e.colorStart = C(1f, 0.55f, 0.15f, 0.3f); e.colorEnd = C(1f, 0.75f, 0.3f, 0.9f); e.sortOrder = 1;
                }),
                E("sparks", "Particles/dot", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = 6; e.burstMax = 9; e.shape = EmitShape.Sphere; e.shapeRadius = 0.45f;
                    e.speed = R(-1.6f, -1.0f); e.gravity = 0f; e.lifetime = R(0.25f, 0.33f); e.sizeStart = R(0.02f, 0.035f); e.sizeEndScale = 0.3f;
                    e.colorStart = C(1f, 0.7f, 0.3f); e.colorEnd = C(1f, 0.9f, 0.6f, 0f);
                })));
            list.Add(Effect("enemy_muzzle", "Enemy shot: muzzle flare and a smoke puff.", 2f,
                Flash(C(1f, 0.8f, 0.45f), 0.35f),
                E("smoke", "Particles/smoke", e =>
                {
                    e.lit = true; e.burstMin = 2; e.burstMax = 3; e.coneAngle = 25f; e.speed = R(1f, 2f); e.gravity = -0.1f; e.drag = 2.5f;
                    e.lifetime = R(0.4f, 0.7f); e.sizeStart = R(0.15f, 0.25f); e.sizeEndScale = 2.5f; e.colorStart = C(0.7f, 0.7f, 0.7f, 0.45f); e.colorEnd = C(0.6f, 0.6f, 0.6f, 0f);
                })));
            list.Add(Effect("enemy_alert", "Yellow burst above an enemy's head when it spots the player.", 2f,
                E("ring", "Particles/ring", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.3f);
                    e.sizeStart = R(0.2f); e.sizeEndScale = 4f; e.colorStart = C(1f, 0.95f, 0.3f, 0.9f); e.colorEnd = C(1f, 0.9f, 0.3f, 0f);
                }),
                E("spark", "Particles/flare", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0.8f); e.coneAngle = 0f; e.gravity = 0f; e.lifetime = R(0.35f);
                    e.sizeStart = R(0.35f); e.sizeEndScale = 0.6f; e.colorStart = C(1f, 0.95f, 0.4f); e.colorEnd = C(1f, 0.9f, 0.3f, 0f); e.sortOrder = 1;
                })));
            list.Add(Effect("bullet_trail", "Thin hot trail behind an enemy slug (follows the projectile).", 3f,
                E("trail", "Particles/soft", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 0; e.rate = 90f; e.duration = 0f; e.coneAngle = 4f; e.speed = R(0.2f, 0.5f); e.gravity = 0f;
                    e.lifetime = R(0.18f, 0.26f); e.sizeStart = R(0.09f); e.sizeEndScale = 0.2f; e.colorStart = C(1f, 0.65f, 0.25f, 0.9f); e.colorEnd = C(1f, 0.3f, 0.1f, 0f);
                })));
            list.Add(Effect("dash", "Speed lines and a dust kick behind a dashing player.", 3f,
                E("lines", "Particles/streak", e =>
                {
                    e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = 8; e.burstMax = 12; e.shape = EmitShape.Box; e.boxSize = new Vector3(1.2f, 1.4f, 0.4f);
                    e.coneAngle = 6f; e.speed = R(6f, 10f); e.gravity = 0f; e.drag = 3f; e.lifetime = R(0.15f, 0.25f); e.sizeStart = R(0.04f, 0.07f); e.stretchLength = 6f; e.stretchVelocity = 0.04f;
                    e.colorStart = C(0.8f, 0.9f, 1f, 0.7f); e.colorEnd = C(0.8f, 0.9f, 1f, 0f);
                }),
                Dust(C(0.8f, 0.78f, 0.72f, 0.35f), 3, 5, 0.2f, 0.35f)));

            // ------------------------------------------------------------ tracers (forward = firing direction, lifetime from travel distance)
            list.Add(Effect("tracer_light", "Pistol / light calibre tracer: thin fast yellow streak.", 1f,
                Bolt(0.04f, 160f, 12f, 0.004f, C(1f, 0.85f, 0.5f, 0.9f), C(1f, 0.7f, 0.3f, 0.6f))));
            list.Add(Effect("tracer_heavy", "Heavy calibre tracer: thick orange streak.", 1f,
                Bolt(0.07f, 200f, 16f, 0.004f, C(1f, 0.6f, 0.2f, 1f), C(1f, 0.45f, 0.15f, 0.7f))));
            list.Add(Effect("tracer_shell", "Shotgun pellet: short faint streak.", 1f,
                Bolt(0.035f, 140f, 6f, 0.003f, C(1f, 0.8f, 0.4f, 0.7f), C(1f, 0.7f, 0.3f, 0.4f))));
            var energy = Effect("tracer_energy", "Energy weapon bolt: cyan streak with a glowing core.", 1f,
                Bolt(0.08f, 120f, 10f, 0.004f, C(0.5f, 0.9f, 1f, 1f), C(0.3f, 0.5f, 1f, 0.5f)),
                E("core", "Particles/soft", e =>
                {
                    e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.coneAngle = 0f; e.speed = R(120f); e.gravity = 0f; e.lifetimeFromDistance = true; e.lifetime = R(0.3f);
                    e.sizeStart = R(0.14f); e.colorStart = C(0.6f, 0.95f, 1f); e.colorEnd = C(0.6f, 0.95f, 1f); e.sortOrder = 1;
                }));
            list.Add(energy);

            return list;
        }

        // ------------------------------------------------------------------ shared building blocks

        /// <summary>Brief additive flash at the impact point.</summary>
        private static ParticleEmitter Flash(Color color, float size) => E("flash", "Particles/flare", e =>
        {
            e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(0.06f, 0.08f);
            e.sizeStart = R(size, size * 1.3f); e.sizeEndScale = 1.5f; e.offset = new Vector3(0f, 0f, 0.03f);
            e.colorStart = color; e.colorEnd = new Color(color.r, color.g * 0.7f, color.b * 0.4f, 0f); e.sortOrder = 5;
        });

        /// <summary>Slow lit dust puff drifting off the surface.</summary>
        private static ParticleEmitter Dust(Color color, int min, int max, float size0, float size1) => E("dust", "Particles/dust", e =>
        {
            e.lit = true; e.burstMin = min; e.burstMax = max; e.shape = EmitShape.Hemisphere; e.shapeRadius = 0.05f; e.speed = R(0.4f, 1.2f); e.gravity = -0.05f; e.drag = 2.5f;
            e.lifetime = R(0.6f, 1.1f); e.sizeStart = R(size0, size1); e.sizeEndScale = 3f; e.rotationSpeed = R(-30f, 30f); e.fadeIn = 0.1f;
            e.colorStart = color; e.colorEnd = new Color(color.r, color.g, color.b, 0f);
        });

        /// <summary>Long-lived surface-aligned decal (bullet hole, scuff, scorch).</summary>
        private static ParticleEmitter Mark(string sprite, Color color, float size0, float size1) => E("mark", sprite, e =>
        {
            e.renderMode = ParticleRenderMode.Aligned; e.lit = true; e.burstMin = e.burstMax = 1; e.speed = R(0f); e.gravity = 0f; e.lifetime = R(20f, 25f);
            e.sizeStart = R(size0, size1); e.offset = new Vector3(0f, 0f, 0.005f); e.colorStart = color; e.colorEnd = new Color(color.r, color.g, color.b, 0f); e.sortOrder = -1;
        });

        /// <summary>Tracer streak that flies from the muzzle to the hit point.</summary>
        private static ParticleEmitter Bolt(float width, float speed, float stretch, float stretchVel, Color c0, Color c1) => E("bolt", "Particles/streak", e =>
        {
            e.renderMode = ParticleRenderMode.Stretched; e.blend = ParticleBlend.Additive; e.burstMin = e.burstMax = 1; e.coneAngle = 0f; e.speed = R(speed); e.gravity = 0f;
            e.lifetimeFromDistance = true; e.lifetime = R(0.3f); e.sizeStart = R(width); e.aspect = 1f; e.stretchLength = stretch; e.stretchVelocity = stretchVel;
            e.colorStart = c0; e.colorEnd = c1;
        });
    }
}
