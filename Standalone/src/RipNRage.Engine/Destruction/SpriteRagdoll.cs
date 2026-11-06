using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Destruction
{
    /// <summary>Options for slicing a sprite into a ragdoll.</summary>
    public struct RagdollOptions
    {
        /// <summary>Velocity every part starts with (the dying sprite's motion).</summary>
        public Vector3 initialVelocity;
        /// <summary>Impulse applied at <see cref="impulsePoint"/> to the part nearest to it (the killing blow).</summary>
        public Vector3 impulse;
        public Vector3 impulsePoint;
        /// <summary>Break every joint: loose pieces instead of a ragdoll (explosions).</summary>
        public bool gib;
        /// <summary>Extra outward impulse per part when gibbing (N·s).</summary>
        public float scatterImpulse;
        /// <summary>Layer of the part objects (default: OnlyGroundCollision, which characters and projectiles ignore).</summary>
        public int layer;
        /// <summary>Layers the parts collide with (default: the level geometry).</summary>
        public int collisionMask;
        /// <summary>Seconds until the ragdoll is removed (0 = the skeleton's lifetime).</summary>
        public float lifetime;
        /// <summary>Parts fade out over the last second of their life.</summary>
        public bool fadeOut;
        /// <summary>Use continuous collision for the parts (small fast pieces).</summary>
        public bool continuous;
        /// <summary>Let the pieces of one ragdoll collide with each other (off: they only collide with the world and other ragdolls — far fewer contacts, no grinding).</summary>
        public bool selfCollision;

        public static RagdollOptions Default => new RagdollOptions
        {
            layer = Layers.OnlyGroundCollision,
            collisionMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision),
            fadeOut = true, continuous = true,
        };
    }

    /// <summary>One piece of a sliced sprite.</summary>
    public sealed class RagdollPart
    {
        public SkeletonPart definition;
        public GameObject gameObject;
        public RigidBody body;
        public SpriteRenderer renderer;
        public Collider collider;
        /// <summary>Joint to the parent part (null for roots and once detached).</summary>
        public BallConstraint joint;
        public RagdollPart parent;
        /// <summary>Pose the part was spawned with (its roll is measured from it).</summary>
        public Quaternion spawnRotation = Quaternion.identity;
        public string name => definition.name;
        public bool attached => joint != null && !joint.broken;
    }

    /// <summary>
    /// A sprite sliced along a <see cref="SpriteSkeleton"/> into rigid-body pieces: every part is a cut-out sprite
    /// (only its polygon of the texture is drawn) on a box or capsule body, hung off its parent with a ball joint
    /// — a ragdoll of billboards. Parts tumble in their billboard plane (the body's roll is read back into
    /// <see cref="SpriteRenderer.billboardRoll"/>), joints can break (blasts, <see cref="Detach"/>), and the whole
    /// thing is removed after its lifetime. Created with <see cref="Spawn"/> from any sprite the skeleton's sheet
    /// produced; the original renderer is left to the caller (hide it).
    /// </summary>
    public sealed class SpriteRagdoll : Behaviour
    {
        public SpriteSkeleton skeleton;
        public SkeletonView view;
        public readonly List<RagdollPart> parts = new List<RagdollPart>();
        public float lifetime;
        public bool fadeOut = true;
        private float _age;

        /// <summary>Raised when a joint breaks (the part that came loose).</summary>
        public event Action<SpriteRagdoll, RagdollPart> PartDetached;

        public RagdollPart Find(string partName)
        {
            foreach (var p in parts) if (string.Equals(p.name, partName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>The part whose body is nearest to a world point.</summary>
        public RagdollPart Nearest(Vector3 worldPoint)
        {
            RagdollPart best = null; float bd = float.MaxValue;
            foreach (var p in parts)
            {
                if (p.body == null || p.body.destroyed) continue;
                float d = (p.body.worldCenterOfMass - worldPoint).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Tears a part off its parent (no-op for roots / already loose parts).</summary>
        public void Detach(string partName) { var p = Find(partName); if (p != null) Detach(p); }

        public void Detach(RagdollPart p)
        {
            if (p?.joint == null) return;
            var j = p.joint;
            Physics.World.RemoveJoint(j);
            j.enabled = false;
            p.joint = null;
            PartDetached?.Invoke(this, p);
        }

        /// <summary>Breaks every joint: the ragdoll becomes loose pieces.</summary>
        public void BreakAll()
        {
            foreach (var p in parts) if (p.joint != null) Detach(p);
        }

        /// <summary>Impulse at a world point, applied to the nearest part.</summary>
        public void AddImpulseAt(Vector3 impulse, Vector3 worldPoint)
        {
            var p = Nearest(worldPoint);
            p?.body?.AddImpulseAtPosition(impulse, worldPoint);
        }

        /// <summary>Explosion on every part.</summary>
        public void AddExplosion(float impulse, Vector3 origin, float radius, float upwards = 0.5f)
        {
            foreach (var p in parts) p.body?.AddExplosionForce(impulse, origin, radius, upwards);
        }

        // ------------------------------------------------------------------ creation

        /// <summary>
        /// Slices <paramref name="sprite"/> (a cell of the skeleton's sheet, drawn as a billboard at
        /// <paramref name="spritePosition"/> with <paramref name="spriteScale"/>) into a ragdoll. The view is the
        /// skeleton's view for the sprite's side. Returns null when the skeleton has no usable view.
        /// </summary>
        public static SpriteRagdoll Spawn(SpriteSkeleton skeleton, Sprite sprite, Vector3 spritePosition, Vector3 spriteScale, RagdollOptions options, SpriteRenderer template = null)
        {
            if (skeleton == null || sprite == null) return null;
            var view = skeleton.ViewForSide(SkeletonLibrary.SideOf(skeleton, sprite));
            if (view == null || view.parts.Count == 0) return null;

            var root = new GameObject("Ragdoll_" + skeleton.name);
            root.transform.position = spritePosition;
            var rag = root.AddComponent<SpriteRagdoll>();
            rag.skeleton = skeleton; rag.view = view;
            rag.lifetime = options.lifetime > 0f ? options.lifetime : skeleton.lifetime;
            rag.fadeOut = options.fadeOut;
            if (options.layer == 0 && options.collisionMask == 0) options = RagdollOptions.Default;

            // the sprite faces the camera around Y: parts are laid out in that plane
            var cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - spritePosition : Vector3.back;
            toCam.y = 0f;
            Quaternion facing = toCam.sqrMagnitude > 1E-06f ? Quaternion.LookRotation(-toCam.normalized) : Quaternion.identity;

            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
            Vector2 cell = new Vector2(sprite.rect.width, sprite.rect.height);
            Vector2 spritePivotPx = sprite.pivot;
            float sx = MathF.Abs(spriteScale.x), sy = MathF.Abs(spriteScale.y);

            // world point of a normalised cell coordinate
            Vector3 World(Vector2 n)
            {
                Vector2 px = new Vector2(n.x * cell.x, n.y * cell.y) - spritePivotPx;
                return spritePosition + facing * new Vector3(px.x / ppu * sx, px.y / ppu * sy, 0f);
            }

            var byName = new Dictionary<string, RagdollPart>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in view.parts)
            {
                if (def.polygon.Count < 3) continue;
                var polyPx = new Vector2[def.polygon.Count];
                for (int i = 0; i < polyPx.Length; i++) polyPx[i] = new Vector2(def.polygon[i].x * cell.x, def.polygon[i].y * cell.y);
                Vector2 centroidPx = Sprite.Centroid(polyPx);
                // the piece is drawn at scale 1 (its pixels-per-unit absorb the sprite's world scale) so the part's
                // colliders are plain world units
                Sprite piece = sprite.Cut(polyPx, centroidPx, sprite.name + ":" + def.name, ppu / MathF.Max(sx, 1E-04f));
                Vector3 centre = World(new Vector2(centroidPx.x / cell.x, centroidPx.y / cell.y));
                Rect b = def.Bounds();
                float w = MathF.Max(b.width * cell.x / ppu * sx, 0.04f), h = MathF.Max(b.height * cell.y / ppu * sy, 0.04f);

                var go = new GameObject(root.name + ":" + def.name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = centre;
                go.transform.rotation = facing;
                go.layer = options.layer;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = piece;
                sr.billboard = true;
                sr.lit = template == null || template.lit;
                sr.castsShadow = template == null || template.castsShadow;
                sr.color = template != null ? template.color : Color.white;
                if (template != null && template.material != null) sr.material = template.material;
                go.transform.localScale = new Vector3(1f, sy / MathF.Max(sx, 1E-04f), 1f);

                Collider col;
                if (def.shape == PartShape.Capsule)
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.radius = MathF.Min(w, h) * 0.5f;
                    cc.height = MathF.Max(w, h);
                    if (w > h) go.transform.rotation = facing * Quaternion.AngleAxis(90f, Vector3.forward); // lie along the long side
                    col = cc;
                }
                else
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.size = new Vector3(w, h, skeleton.depth);
                    col = bc;
                }
                var body = go.AddComponent<RigidBody>();
                body.mass = MathF.Max(def.mass, 0.05f);
                body.friction = 0.7f; body.restitution = 0.05f;
                body.linearDamping = 0.1f; body.angularDamping = 0.6f;
                body.collisionMask = options.collisionMask;
                body.useLayerMatrix = false;
                body.collisionDetection = options.continuous ? CollisionDetectionMode.Continuous : CollisionDetectionMode.Discrete;
                if (!options.selfCollision) body.collisionGroup = -root.instanceId; // one negative group per ragdoll
                body.linearVelocity = options.initialVelocity;
                body.ComputeMass(); body.ReadTransform(); // usable right now (Awake repeats this next frame)

                var part = new RagdollPart { definition = def, gameObject = go, body = body, renderer = sr, collider = col, spawnRotation = go.transform.rotation };
                rag.parts.Add(part);
                byName[def.name] = part;
            }

            // joints: every part hangs off its parent at its pivot (once the bodies exist with their poses)
            foreach (var part in rag.parts)
            {
                var def = part.definition;
                if (string.IsNullOrEmpty(def.parent) || !byName.TryGetValue(def.parent, out var parent) || parent == part) continue;
                part.parent = parent;
                if (def.loose || options.gib) continue;
                var j = new BallConstraint(part.body, parent.body, World(def.pivot)) { breakImpulse = def.breakImpulse };
                var captured = part;
                j.Broke += _ => { if (captured.joint == j) { captured.joint = null; rag.PartDetached?.Invoke(rag, captured); } };
                part.joint = j;
                Physics.World.AddJoint(j);
            }

            // the blow
            if (options.impulse.sqrMagnitude > 0f) rag.AddImpulseAt(options.impulse, options.impulsePoint);
            if (options.gib && options.scatterImpulse > 0f)
            {
                foreach (var p in rag.parts)
                {
                    Vector3 dir = (p.body.worldCenterOfMass - spritePosition); dir.y = MathF.Abs(dir.y) + 0.3f;
                    dir = dir.sqrMagnitude > 1E-06f ? dir.normalized : Vector3.up;
                    p.body.AddImpulse(dir * options.scatterImpulse + Vector3.up * (options.scatterImpulse * 0.3f));
                    p.body.AddTorque(new Vector3(0f, 0f, (p.body.instanceId % 2 == 0 ? 1f : -1f) * options.scatterImpulse * 0.2f), ForceMode.Impulse);
                }
            }
            return rag;
        }

        // ------------------------------------------------------------------ per frame

        protected override void Update()
        {
            _age += Time.deltaTime;
            var cam = Camera.main;
            foreach (var p in parts)
            {
                if (p.body == null || p.body.destroyed || p.renderer == null) continue;
                // the body's roll in the billboard plane: where its local up points, projected on the quad
                Vector3 toCam = cam != null ? cam.transform.position - p.body.transform.position : Vector3.back;
                toCam.y = 0f;
                Vector3 n = toCam.sqrMagnitude > 1E-06f ? -toCam.normalized : Vector3.forward;
                Quaternion rel = p.body.transform.rotation * Quaternion.Inverse(p.spawnRotation); // rotation since the spawn pose
                Vector3 up = rel * Vector3.up;
                Vector3 projected = up - n * Vector3.Dot(up, n);
                if (projected.sqrMagnitude > 1E-06f) p.renderer.billboardRoll = Vector3.SignedAngle(Vector3.up, projected.normalized, n);
                if (fadeOut && lifetime > 0f && _age > lifetime - 1f)
                {
                    var c = p.renderer.color; c.a = Mathf.Clamp01(lifetime - _age); p.renderer.color = c;
                }
            }
            if (lifetime > 0f && _age >= lifetime) GameObject.Destroy(gameObject);
        }

        protected override void OnDestroy()
        {
            foreach (var p in parts) if (p.joint != null) { Physics.World.RemoveJoint(p.joint); p.joint = null; }
        }
    }
}
