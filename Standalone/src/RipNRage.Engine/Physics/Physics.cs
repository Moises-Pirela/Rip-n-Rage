using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
        public static Bounds FromCenterExtents(Vector3 center, Vector3 halfExtents) => new Bounds(center - halfExtents, center + halfExtents);
        public Vector3 center => (min + max) * 0.5f;
        public Vector3 size => max - min;
        /// <summary>Half size.</summary>
        public Vector3 extents => (max - min) * 0.5f;
        public bool Intersects(Bounds o) =>
            min.x <= o.max.x && max.x >= o.min.x && min.y <= o.max.y && max.y >= o.min.y && min.z <= o.max.z && max.z >= o.min.z;
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public Bounds Expanded(float r) => new Bounds(min - new Vector3(r, r, r), max + new Vector3(r, r, r));
        public Bounds Expanded(Vector3 r) => new Bounds(min - r, max + r);
        public Bounds Translated(Vector3 v) => new Bounds(min + v, max + v);
        public void Encapsulate(Vector3 p) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        public void Encapsulate(Bounds b) { min = Vector3.Min(min, b.min); max = Vector3.Max(max, b.max); }
        /// <summary>Smallest box containing this box at its start and end of a translation (the swept volume's bounds).</summary>
        public Bounds Swept(Vector3 motion) => new Bounds(Vector3.Min(min, min + motion), Vector3.Max(max, max + motion));
        public override string ToString() => $"Bounds({min} - {max})";
    }

    /// <summary>Result of a raycast (UnityEngine.RaycastHit subset).</summary>
    /// <summary>A ray / shape-cast hit: the ECS entity whose collider was touched.</summary>
    public struct RaycastHit
    {
        public Ecs.Entity entity;
        public PhysicsProxy proxy;
        public Vector3 point;
        public Vector3 normal;
        public float distance;
        public bool hit => !proxy.IsNone;
    }

    /// <summary>
    /// Which colliders a query or mover considers. <see cref="layerMask"/> bits select collider layers; with
    /// <see cref="useLayerMatrix"/> the row of <see cref="selfLayer"/> in the collision matrix applies too.
    /// <see cref="self"/> (and every collider of <see cref="ignore"/>) is never returned; <see cref="predicate"/>
    /// is an optional final veto by entity.
    /// </summary>
    public struct QueryFilter
    {
        public int layerMask;
        public PhysicsProxy self;
        public Ecs.Entity ignore;
        public bool useLayerMatrix;
        public int selfLayer;
        public bool includeTriggers;
        public Func<Ecs.Entity, bool> predicate;
        /// <summary>Only colliders that do not move this step (no body, kinematic or sleeping): the solver's continuous collision.</summary>
        internal bool staticOnly;

        public static QueryFilter Everything => new QueryFilter { layerMask = ~0 };
        public static QueryFilter Solids(int layerMask = ~0) => new QueryFilter { layerMask = layerMask };
        /// <summary>What a collider-bearing mover should collide with: its layer's matrix row, never itself.</summary>
        public static QueryFilter For(PhysicsProxy self, int layer, int layerMask = ~0) =>
            new QueryFilter { layerMask = layerMask, self = self, selfLayer = layer, useLayerMatrix = true };

        internal bool Accepts(ColliderTable.Slot s)
        {
            if (s == null || !s.alive) return false;
            if (s.isTrigger && !includeTriggers) return false;
            if (((1 << s.layer) & layerMask) == 0) return false;
            if (!self.IsNone && s.index == self.Index && s.generation == self.Generation) return false;
            if (!ignore.IsNull && s.entity == ignore) return false;
            if (useLayerMatrix && Physics.GetIgnoreLayerCollision(selfLayer & 31, s.layer)) return false;
            if (staticOnly && s.body != null && !s.body.isKinematic && !s.body.sleeping) return false;
            if (predicate != null && !predicate(s.entity)) return false;
            return true;
        }
    }

    /// <summary>Result of a shape sweep: the first collider the moving shape touches.</summary>
    public struct ShapeHit
    {
        public Ecs.Entity entity;
        public PhysicsProxy proxy;
        internal ColliderTable.Slot slot;
        /// <summary>Fraction of the motion completed before the contact (0..1); 1 when nothing was hit.</summary>
        public float fraction;
        /// <summary>Surface normal at the contact (points away from the collider).</summary>
        public Vector3 normal;
        public Vector3 point;
        /// <summary>The shape already overlapped the collider at the start of the sweep (fraction 0, normal = the way out).</summary>
        public bool startSolid;
        /// <summary>The plane that was hit (world space).</summary>
        public Plane plane;
        public bool hit => slot != null;

        internal void Set(ColliderTable.Slot s) { slot = s; entity = s.entity; proxy = ColliderTable.ProxyOf(s); }
    }

    /// <summary>
    /// Scene queries plus the engine's movement API, all served by <see cref="World"/>. Every collider is an ECS
    /// entity; hits name the entity.
    /// </summary>
    public static class Physics
    {
        public static Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        public const int DefaultRaycastLayers = ~0;
        public const float DefaultSkin = 0.01f;
        public static float defaultSkin = DefaultSkin;
        private static float Skin(float skin) => skin > 0f ? skin : defaultSkin;
        /// <summary>Shape casts given an infinite distance are capped to this.</summary>
        public const float MaxCastDistance = 1000f;

        public static PhysicsWorld World { get; } = new PhysicsWorld();

        /// <summary>One fixed step of the simulation (the ECS PhysicsStepSystem calls this).</summary>
        public static void FixedStep(float dt) => World.StepFixed(dt);
        /// <summary>Once per rendered frame: query counters roll over.</summary>
        public static void Step() => World.StepFrame(Time.deltaTime);

        // ---- layer collision matrix ----
        private static readonly uint[] CollisionMatrix = InitMatrix();
        private static uint[] InitMatrix() { var m = new uint[32]; for (int i = 0; i < 32; i++) m[i] = 0xFFFFFFFFu; return m; }
        public static void SetCollisionMatrix(uint[] rows) { for (int i = 0; i < 32 && i < rows.Length; i++) CollisionMatrix[i] = rows[i]; }
        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore = true)
        {
            if (ignore) { CollisionMatrix[layer1] &= ~(1u << layer2); CollisionMatrix[layer2] &= ~(1u << layer1); }
            else { CollisionMatrix[layer1] |= 1u << layer2; CollisionMatrix[layer2] |= 1u << layer1; }
        }
        public static bool GetIgnoreLayerCollision(int layer1, int layer2) => (CollisionMatrix[layer1 & 31] & (1u << (layer2 & 31))) == 0;
        public static int CollisionMaskOf(int layer) => unchecked((int)CollisionMatrix[layer & 31]);

        // ---- ray queries ----

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers, bool includeTriggers = false) =>
            World.Raycast(new Ray(origin, direction), maxDistance, new QueryFilter { layerMask = layerMask, includeTriggers = includeTriggers }, out hit);
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(origin, direction, out _, maxDistance, layerMask);
        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(ray.origin, ray.direction, out hit, maxDistance, layerMask);
        public static bool Raycast(Ray ray, float maxDistance, QueryFilter filter, out RaycastHit hit) => World.Raycast(ray, maxDistance, filter, out hit);

        // ---- shape casts (continuous: the exact time of impact along the whole motion) ----

        private static float CastDistance(float maxDistance) => float.IsInfinity(maxDistance) || maxDistance > MaxCastDistance ? MaxCastDistance : maxDistance;

        public static bool ShapeCast(in CollisionShape shape, Vector3 direction, QueryFilter filter, out RaycastHit hit, float maxDistance = float.PositiveInfinity)
        {
            hit = default;
            if (direction.sqrMagnitude < 1E-12f) return false;
            float dist = CastDistance(maxDistance);
            if (!World.Sweep(in shape, direction.normalized * dist, filter, defaultSkin, out ShapeHit sh)) return false;
            hit = new RaycastHit { entity = sh.entity, proxy = sh.proxy, point = sh.point, normal = sh.normal, distance = sh.fraction * dist };
            return true;
        }
        public static bool ShapeCast(in CollisionShape shape, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(in shape, direction, QueryFilter.Solids(layerMask), out hit, maxDistance);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.AxisAlignedBox(center, halfExtents), direction, out hit, maxDistance, layerMask);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.Box(center, orientation, halfExtents), direction, out hit, maxDistance, layerMask);
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.Sphere(origin, radius), direction, out hit, maxDistance, layerMask);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CapsuleShape(point1, point2, radius), direction, out hit, maxDistance, layerMask);

        public static CollisionShape CapsuleShape(Vector3 point1, Vector3 point2, float radius)
        {
            Vector3 axis = point2 - point1;
            float len = axis.magnitude;
            Quaternion rot = len > 1E-06f ? Quaternion.FromToRotation(Vector3.up, axis / len) : Quaternion.identity;
            return CollisionShape.Capsule((point1 + point2) * 0.5f, rot, radius, len * 0.5f);
        }

        // ---- overlap queries (exact shapes) ----

        /// <summary>Entities whose colliders overlap <paramref name="shape"/>; <paramref name="results"/> may be null to only count.</summary>
        public static int OverlapShape(in CollisionShape shape, List<Ecs.Entity> results, QueryFilter filter) => World.Overlap(in shape, filter, results);
        public static bool CheckShape(in CollisionShape shape, QueryFilter filter) => World.Overlap(in shape, filter, null) > 0;
        public static bool CheckSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers) => CheckShape(CollisionShape.Sphere(position, radius), QueryFilter.Solids(layerMask));
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, int layerMask = DefaultRaycastLayers) => CheckShape(CollisionShape.AxisAlignedBox(center, halfExtents), QueryFilter.Solids(layerMask));
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, QueryFilter filter) => CheckShape(CollisionShape.AxisAlignedBox(center, halfExtents), filter);
        public static bool CheckCapsule(Vector3 point1, Vector3 point2, float radius, int layerMask = DefaultRaycastLayers) => CheckShape(CapsuleShape(point1, point2, radius), QueryFilter.Solids(layerMask));
        public static List<Ecs.Entity> OverlapSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Ecs.Entity>();
            World.Overlap(CollisionShape.Sphere(position, radius), new QueryFilter { layerMask = layerMask, includeTriggers = true }, list);
            return list;
        }

        public static bool ComputeDistance(in CollisionShape a, in CollisionShape b, out DistanceResult result) => ConvexDistance.Distance(in a, in b, out result);

        // ---- movement API ----

        public static bool Sweep(in CollisionShape shape, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f) =>
            World.Sweep(in shape, motion, filter, Skin(skin), out hit);
        public static bool Sweep(Vector3 center, Vector3 halfExtents, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f) =>
            World.Sweep(CollisionShape.AxisAlignedBox(center, halfExtents), motion, filter, Skin(skin), out hit);

        public static Vector3 MoveAndCollide(in CollisionShape shape, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f)
        {
            World.Sweep(in shape, motion, filter, Skin(skin), out hit);
            return shape.center + motion * hit.fraction;
        }

        public static MoveResult MoveAndSlide(in CollisionShape shape, Vector3 motion, MoveSettings settings, MoveResult result = null)
        {
            result ??= new MoveResult();
            KinematicSolver.MoveAndSlide(World, in shape, motion, settings, result);
            return result;
        }
        public static MoveResult MoveAndSlide(Vector3 center, Vector3 halfExtents, Vector3 motion, MoveSettings settings, MoveResult result = null) =>
            MoveAndSlide(CollisionShape.AxisAlignedBox(center, halfExtents), motion, settings, result);

        /// <summary>Pushes the rigid bodies a kinematic move ran into (a character walking into crates), up to <paramref name="pushMass"/>.</summary>
        public static void PushRigidBodies(MoveResult result, Vector3 moverVelocity, float pushMass)
        {
            if (pushMass <= 0f || result == null) return;
            var contacts = result.contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c.kind == ContactKind.Floor) continue; // standing on a body is not a push
                var rb = World.Table.BodyOf(c.proxy);
                if (rb == null || !rb.IsLive || rb.isKinematic || rb.sleeping && moverVelocity.sqrMagnitude < 0.01f) continue;
                Vector3 dir = -c.normal; // into the body
                float into = Vector3.Dot(moverVelocity, dir);
                if (into <= 0.05f) continue;
                float current = Vector3.Dot(rb.GetPointVelocity(c.point), dir);
                float dv = (into - current) * MathF.Min(1f, pushMass / MathF.Max(rb.mass, 1E-03f));
                if (dv <= 0f) continue;
                rb.AddImpulseAtPosition(dir * (dv * rb.mass), c.point);
            }
        }

        /// <summary>All rigid bodies in the world.</summary>
        public static IReadOnlyList<SolverBody> Bodies => World.Bodies;

        /// <summary>Explosion impulse on every rigid body within <paramref name="radius"/> whose collider layer is in <paramref name="layerMask"/>.</summary>
        public static void AddExplosionForce(float impulse, Vector3 origin, float radius, float upwardsModifier = 0f, int layerMask = DefaultRaycastLayers)
        {
            var bodies = World.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (!b.IsLive) continue;
                if (b.collider != null && ((1 << b.collider.layer) & layerMask) == 0) continue;
                if ((b.worldCenterOfMass - origin).sqrMagnitude > (radius + 5f) * (radius + 5f)) continue;
                b.AddExplosionForce(impulse, origin, radius, upwardsModifier);
            }
        }

        /// <summary>Pushes a shape out of every solid it overlaps; returns the distance it had to move.</summary>
        public static float Depenetrate(ref CollisionShape shape, QueryFilter filter, float skin = 0f)
        {
            var candidates = World.RentList();
            skin = Skin(skin);
            World.QueryBounds(shape.bounds.Expanded(skin + 0.05f), filter, candidates);
            float moved = KinematicSolver.Depenetrate(candidates, ref shape, skin, out _);
            World.ReturnList(candidates);
            return moved;
        }

        // ---- primitives (distance + normal; callers fill in who was hit) ----

        public static bool RayAabb(Ray ray, Bounds b, float maxDistance, out float distance, out Vector3 normal)
        {
            distance = 0f; normal = -ray.direction;
            float tmin = 0f, tmax = maxDistance;
            int hitAxis = -1; float hitSign = 0f;
            for (int i = 0; i < 3; i++)
            {
                float o = ray.origin[i], d = ray.direction[i], mn = b.min[i], mx = b.max[i];
                if (Mathf.Abs(d) < 1E-08f) { if (o < mn || o > mx) return false; }
                else
                {
                    float inv = 1f / d;
                    float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
                    float sign = -1f;
                    if (t1 > t2) { var t = t1; t1 = t2; t2 = t; sign = 1f; }
                    if (t1 > tmin) { tmin = t1; hitAxis = i; hitSign = sign; }
                    if (t2 < tmax) tmax = t2;
                    if (tmin > tmax) return false;
                }
            }
            if (hitAxis < 0) return true; // origin inside the box
            normal = Vector3.zero; normal[hitAxis] = hitSign;
            distance = tmin;
            return true;
        }

        public static bool RayCapsule(Ray ray, Vector3 a, Vector3 b, float radius, float maxDistance, out float distance, out Vector3 normal)
        {
            distance = 0f; normal = Vector3.up;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1E-12f) return RaySphere(ray, a, radius, maxDistance, out distance, out normal);
            Vector3 axis = ab / MathF.Sqrt(len2);
            Vector3 m = ray.origin - a;
            Vector3 dPerp = ray.direction - axis * Vector3.Dot(ray.direction, axis);
            Vector3 mPerp = m - axis * Vector3.Dot(m, axis);
            float best = maxDistance; bool found = false; Vector3 bestNormal = Vector3.up;
            float qa = dPerp.sqrMagnitude, qb = 2f * Vector3.Dot(dPerp, mPerp), qc = mPerp.sqrMagnitude - radius * radius;
            if (qa > 1E-12f)
            {
                float disc = qb * qb - 4f * qa * qc;
                if (disc >= 0f)
                {
                    float t = (-qb - MathF.Sqrt(disc)) / (2f * qa);
                    if (t < 0f && qc <= 0f) t = 0f; // origin inside the cylinder
                    if (t >= 0f && t < best)
                    {
                        Vector3 p = ray.GetPoint(t);
                        float along = Vector3.Dot(p - a, axis);
                        if (along >= 0f && along * along <= len2) { best = t; found = true; bestNormal = (p - (a + axis * along)).normalized; }
                    }
                }
            }
            if (RaySphere(ray, a, radius, best, out float da, out Vector3 na) && da < best) { best = da; found = true; bestNormal = na; }
            if (RaySphere(ray, b, radius, best, out float db, out Vector3 nb) && db < best) { best = db; found = true; bestNormal = nb; }
            if (!found) return false;
            distance = best; normal = bestNormal;
            return true;
        }

        public static bool RaySphere(Ray ray, Vector3 center, float radius, float maxDistance, out float distance, out Vector3 normal)
        {
            distance = 0f; normal = Vector3.up;
            Vector3 m = ray.origin - center;
            float bq = Vector3.Dot(m, ray.direction);
            float cq = Vector3.Dot(m, m) - radius * radius;
            if (cq > 0f && bq > 0f) return false;
            float disc = bq * bq - cq;
            if (disc < 0f) return false;
            float t = -bq - Mathf.Sqrt(disc);
            if (t < 0f) t = 0f;
            if (t > maxDistance) return false;
            distance = t; normal = (ray.GetPoint(t) - center).normalized;
            return true;
        }
    }
}
