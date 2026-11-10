using System;
using System.Collections.Generic;
using RipNRage.Engine.Destruction;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Destruction
{
    public struct RagdollOptions
    {
        /// <summary>Velocity the pieces start with (the body was moving).</summary>
        public Vector3 initialVelocity;
        /// <summary>The killing blow: applied to the piece nearest <see cref="impulsePoint"/>.</summary>
        public Vector3 impulse, impulsePoint;
        /// <summary>Gib: no joints at all, every piece flies off with <see cref="scatterImpulse"/>.</summary>
        public bool gib;
        public float scatterImpulse;
        /// <summary>Collision layer of the pieces and the layers they collide with.</summary>
        public int layer, collisionMask;
        /// <summary>Seconds before the pieces are removed (0 = the skeleton's lifetime); they fade over the last second.</summary>
        public float lifetime;
        public bool fadeOut, continuous, selfCollision;
        // look of the pieces (the sprite they were cut from)
        public bool lit, castsShadow;
        public Color color;
        public AssetHandle<Material> material;

        public static RagdollOptions Default => new RagdollOptions
        {
            layer = Layers.OnlyGroundCollision,
            collisionMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision),
            fadeOut = true, continuous = true, lit = true, castsShadow = true, color = Color.white,
        };
    }

    /// <summary>
    /// One piece of a sliced sprite: billboarded, rolled in the view plane by the body's spin since it spawned,
    /// faded and removed at the end of its lifetime. <see cref="Ragdoll"/> groups the pieces of one ragdoll.
    /// </summary>
    public struct RagdollPiece : IComponent
    {
        public int Ragdoll;
        /// <summary>Name of the skeleton part (head, arm_l ...).</summary>
        public string Part;
        public Quaternion SpawnRotation;
        public float Age, Lifetime;
        public bool FadeOut;
        public float BaseAlpha;
    }

    /// <summary>An impulse at a world point to apply once the piece's body is in the simulation (the spawn's blow).</summary>
    public struct PendingImpulse : IComponent { public Vector3 Impulse, Point, AngularImpulse; }

    public sealed class RagdollPieceConfig : ComponentConfig<RagdollPiece> { public override RagdollPiece Bake() => new RagdollPiece { BaseAlpha = 1f }; }
    public sealed class PendingImpulseConfig : ComponentConfig<PendingImpulse> { public override PendingImpulse Bake() => default; }

    /// <summary>A piece came off (its joint was removed or broke).</summary>
    public struct RagdollPieceDetachedEvent : IPostProcessEvent { public Entity Piece; public int Ragdoll; }

    /// <summary>Sprite destruction on the ECS: slices a sprite along a <see cref="SpriteSkeleton"/> view into jointed pieces.</summary>
    public static class SpriteRagdolls
    {
        private static int _nextId;

        /// <summary>
        /// Spawns the pieces (entities: cut-sprite render, body, collider, ball joint to the parent piece) for the
        /// skeleton view matching <paramref name="sprite"/>'s side, laid out in the plane facing
        /// <paramref name="viewer"/>. Returns the piece entities (empty when the view has no usable parts).
        /// </summary>
        public static List<Entity> Spawn(World world, SpriteSkeleton skeleton, Sprite sprite, Vector3 spritePosition, Vector3 spriteScale, Vector3 viewer, RagdollOptions options)
        {
            var pieces = new List<Entity>();
            if (skeleton == null || sprite == null) return pieces;
            var view = skeleton.ViewForSide(SkeletonLibrary.SideOf(skeleton, sprite));
            if (view == null || view.parts.Count == 0) return pieces;
            if (options.layer == 0 && options.collisionMask == 0) options = RagdollOptions.Default;
            if (options.color.a <= 0f) options.color = Color.white;
            int id = ++_nextId;
            float lifetime = options.lifetime > 0f ? options.lifetime : skeleton.lifetime;

            Vector3 toCam = viewer - spritePosition; toCam.y = 0f;
            Quaternion facing = toCam.sqrMagnitude > 1E-06f ? Quaternion.LookRotation(-toCam.normalized) : Quaternion.identity;
            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
            Vector2 cell = new Vector2(sprite.rect.width, sprite.rect.height);
            Vector2 pivotPx = sprite.pivot;
            float sx = MathF.Abs(spriteScale.x), sy = MathF.Abs(spriteScale.y);
            Vector3 WorldOf(Vector2 n)
            {
                Vector2 px = new Vector2(n.x * cell.x, n.y * cell.y) - pivotPx;
                return spritePosition + facing * new Vector3(px.x / ppu * sx, px.y / ppu * sy, 0f);
            }

            var cmd = world.Commands;
            var byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            var defs = new List<(SkeletonPart def, Entity e)>();
            foreach (var def in view.parts)
            {
                if (def.polygon.Count < 3) continue;
                var polyPx = new Vector2[def.polygon.Count];
                for (int i = 0; i < polyPx.Length; i++) polyPx[i] = new Vector2(def.polygon[i].x * cell.x, def.polygon[i].y * cell.y);
                Vector2 centroidPx = Sprite.Centroid(polyPx);
                // drawn at scale 1 (the piece's pixels-per-unit absorbs the sprite's world scale): colliders in plain world units
                Sprite piece = sprite.Cut(polyPx, centroidPx, sprite.name + ":" + def.name, ppu / MathF.Max(sx, 1E-04f));
                Vector3 centre = WorldOf(new Vector2(centroidPx.x / cell.x, centroidPx.y / cell.y));
                Rect b = def.Bounds();
                float w = MathF.Max(b.width * cell.x / ppu * sx, 0.04f), h = MathF.Max(b.height * cell.y / ppu * sy, 0.04f);
                Quaternion rot = facing;
                ColliderShape shape;
                if (def.shape == PartShape.Capsule)
                {
                    shape = new ColliderShape { Kind = ShapeKind.Capsule, Radius = MathF.Min(w, h) * 0.5f, Height = MathF.Max(w, h) };
                    if (w > h) rot = facing * Quaternion.AngleAxis(90f, Vector3.forward); // lie along the long side
                }
                else shape = new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(w, h, skeleton.depth) };

                var e = cmd.CreateEntity();
                cmd.Add(e, new LocalTransform { Position = centre, Rotation = rot, Scale = new Vector3(1f, sy / MathF.Max(sx, 1E-04f), 1f) });
                cmd.Add(e, new SpriteRender
                {
                    Sprite = RenderAssets.Sprites.Intern(piece), Billboard = true, Lit = options.lit, CastsShadow = options.castsShadow,
                    Color = options.color, Material = options.material, Layer = RenderLayer.World,
                });
                cmd.Add(e, shape);
                cmd.Add(e, new BodyConfig
                {
                    mass = MathF.Max(def.mass, 0.05f), linearDamping = 0.1f, angularDamping = 0.6f,
                    collisionDetection = options.continuous ? CollisionDetectionMode.Continuous : CollisionDetectionMode.Discrete,
                }.Bake());
                cmd.Add(e, new BodyVelocity { Linear = options.initialVelocity });
                cmd.Add(e, new SurfaceMaterial { StaticFriction = 0.7f, DynamicFriction = 0.7f, Restitution = 0.05f, FrictionCombine = CombineMode.Average, RestitutionCombine = CombineMode.Maximum });
                cmd.Add(e, new CollisionFilter { Layer = options.layer, Mask = options.collisionMask, UseLayerMatrix = false, Group = options.selfCollision ? 0 : -id });
                cmd.Add(e, new RagdollPiece { Ragdoll = id, Part = def.name, SpawnRotation = rot, Lifetime = lifetime, FadeOut = options.fadeOut, BaseAlpha = options.color.a });
                byName[def.name] = e;
                defs.Add((def, e));
                pieces.Add(e);
            }

            // every part hangs off its parent at its pivot
            if (!options.gib)
                foreach (var (def, e) in defs)
                {
                    if (def.loose || string.IsNullOrEmpty(def.parent) || !byName.TryGetValue(def.parent, out var parent) || parent == e) continue;
                    var j = new JointConfig { kind = JointKind.Ball }.Bake();
                    j.Connected = parent;
                    j.UseWorldAnchor = true; j.WorldAnchor = WorldOf(def.pivot);
                    j.BreakImpulse = def.breakImpulse;
                    cmd.Add(e, j);
                }

            // the blow: on the piece nearest the impact point; gibs scatter outwards
            if (options.impulse.sqrMagnitude > 0f && defs.Count > 0)
            {
                Entity nearest = defs[0].e; float bd = float.MaxValue;
                foreach (var (def, e) in defs)
                {
                    float d = (WorldOf(def.pivot) - options.impulsePoint).sqrMagnitude;
                    if (d < bd) { bd = d; nearest = e; }
                }
                cmd.Add(nearest, new PendingImpulse { Impulse = options.impulse, Point = options.impulsePoint });
            }
            if (options.gib && options.scatterImpulse > 0f)
                for (int i = 0; i < defs.Count; i++)
                {
                    var (def, e) = defs[i];
                    Vector3 dir = WorldOf(def.pivot) - spritePosition; dir.y = MathF.Abs(dir.y) + 0.3f;
                    dir = dir.sqrMagnitude > 1E-06f ? dir.normalized : Vector3.up;
                    cmd.Add(e, new PendingImpulse
                    {
                        Impulse = dir * options.scatterImpulse + Vector3.up * (options.scatterImpulse * 0.3f), Point = WorldOf(def.pivot),
                        AngularImpulse = facing * new Vector3(0f, 0f, (i % 2 == 0 ? 1f : -1f) * options.scatterImpulse * 0.2f),
                    });
                }
            return pieces;
        }

        /// <summary>Tears a piece off (its joint goes; it keeps flying with its own momentum).</summary>
        public static void Detach(World world, Entity piece)
        {
            if (!world.IsAlive(piece) || !world.Has<Joint>(piece)) return;
            int rag = world.Has<RagdollPiece>(piece) ? world.Get<RagdollPiece>(piece).Ragdoll : 0;
            world.Commands.Remove<Joint>(piece);
            world.Post(new RagdollPieceDetachedEvent { Piece = piece, Ragdoll = rag });
        }
    }

    /// <summary>
    /// Ragdoll upkeep: applies the spawn's pending impulses once the bodies are simulated (fixed step, before the
    /// solver), and per frame rolls each billboarded piece by its body's spin in the view plane, fades it over its
    /// last second and removes it at the end of its lifetime. Broken joints are reported as detachments.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    public sealed class RagdollSystem : SystemBase, IFixedTick, ITick, IConsumes<JointBrokenEvent>
    {
        private Query _impulses, _pieces;
        private PhysicsSyncSystem _sync;
        /// <summary>Where pieces billboard towards (the active camera); set by the host, default the origin.</summary>
        public static Func<Vector3> Viewer = () => Vector3.zero;

        public override void OnCreate()
        {
            _impulses = World.Query().WithReadOnly<PendingImpulse>().WithReadOnly<PhysicsHandle>().Build();
            _pieces = World.Query().With<RagdollPiece>().With<SpriteRender>().WithReadOnly<LocalToWorld>().Build();
        }

        public void FixedTick(SystemContext ctx)
        {
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            if (_sync == null) return;
            foreach (var chunk in _impulses)
            {
                var imp = chunk.Get<PendingImpulse>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    var b = _sync.BodyOf(ents[i]);
                    if (b == null) continue;
                    b.AddImpulseAtPosition(imp[i].Impulse, imp[i].Point);
                    if (imp[i].AngularImpulse != Vector3.zero) b.AddTorque(imp[i].AngularImpulse, ForceMode.Impulse);
                    ctx.Commands.Remove<PendingImpulse>(ents[i]);
                }
            }
        }

        public void Tick(SystemContext ctx)
        {
            Vector3 viewer = Viewer();
            float dt = ctx.DeltaTime;
            foreach (var chunk in _pieces)
            {
                var pc = chunk.Get<RagdollPiece>(); var sr = chunk.Get<SpriteRender>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var p = ref pc[i];
                    p.Age += dt;
                    // the roll in the billboard plane: where the body's local up points, projected on the quad
                    Vector3 toCam = viewer - ltw[i].Position; toCam.y = 0f;
                    Vector3 n = toCam.sqrMagnitude > 1E-06f ? -toCam.normalized : Vector3.forward;
                    Quaternion rel = ltw[i].Rotation * Quaternion.Inverse(p.SpawnRotation);
                    Vector3 up = rel * Vector3.up;
                    Vector3 projected = up - n * Vector3.Dot(up, n);
                    if (projected.sqrMagnitude > 1E-06f) sr[i].BillboardRoll = Vector3.SignedAngle(Vector3.up, projected.normalized, n);
                    if (p.FadeOut && p.Lifetime > 0f && p.Age > p.Lifetime - 1f)
                    {
                        var col = sr[i].Color; col.a = p.BaseAlpha * Mathf.Clamp01(p.Lifetime - p.Age); sr[i].Color = col;
                    }
                    if (p.Lifetime > 0f && p.Age >= p.Lifetime) ctx.Commands.DestroyEntity(ents[i]);
                }
            }
        }

        public void Consume(SystemContext ctx, ReadOnlySpan<JointBrokenEvent> events)
        {
            foreach (var e in events)
                if (World.IsAlive(e.Entity) && World.Has<RagdollPiece>(e.Entity))
                    ctx.Post(new RagdollPieceDetachedEvent { Piece = e.Entity, Ragdoll = World.Get<RagdollPiece>(e.Entity).Ragdoll });
        }
    }
}
