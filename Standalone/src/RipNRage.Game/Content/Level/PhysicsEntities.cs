using System;
using System.Globalization;
using RipNRage.Engine.Graphics;
using RipNRage.Level;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs;

namespace RipNRage.Game.Content.Level
{
    /// <summary>
    /// The level's physics entities: rigid-body props and the joints that turn them into machines and puzzles.
    ///
    /// Self-contained pieces (each spawns its own prop and hangs it on the world): <c>prop_physics</c> (a loose
    /// body), <c>phys_door</c> (a panel hinged along one edge with limits, a closing spring and/or a motor),
    /// <c>phys_hinge</c> (a prop turning about an axis through a pivot — seesaws, levers, windmills, rotating
    /// platforms), <c>phys_slider</c> (a prop on a rail — pistons, lifts, sliding doors), <c>phys_pendulum</c> (a
    /// prop on a rod or rope from the origin), <c>phys_spring_prop</c> (a prop on a spring from the origin) and
    /// <c>phys_fixed</c> (a prop welded in place that breaks loose above a force).
    ///
    /// Connectors (spawned after everything else) join two props by <c>targetname</c>: <c>joint_hinge</c>,
    /// <c>joint_slider</c>, <c>joint_fixed</c>, <c>joint_ball</c>, <c>joint_distance</c>, <c>joint_spring</c>; the
    /// origin is the anchor, <c>body</c> names the prop the joint sits on, <c>connected</c> the other one (absent =
    /// the world). Every joint takes <c>break_force</c> / <c>break_torque</c> (N / N·m, 0 = unbreakable) and
    /// <c>collide 1</c> to let the two bodies touch.
    ///
    /// Props share <c>size "w h l"</c>, <c>mass</c>, <c>texture</c>, <c>shape box|sphere|capsule</c> and
    /// <c>angle</c>; their origin is the bottom centre (like the other props) unless said otherwise.
    /// </summary>
    public static class PhysicsEntities
    {
        private static readonly Color PropColor = new Color(1f, 0.6f, 0.2f, 1f);
        private static readonly Color JointColor = new Color(0.4f, 0.8f, 1f, 1f);

        public static void Register(Func<EntityDefinition, EntityDefinition> add)
        {
            add(new EntityDefinition
            {
                classname = "phys_door", description = "Hinged door (size w h thick; hinge left|right|top; limits \"min max\" deg; spring N·m/rad, damper, closed_angle; motor deg/s, motor_torque; break_force)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.6f, 1.2f, 0.2f),
                defaults = { ["size"] = "1.2 2.4 0.12", ["mass"] = "25", ["texture"] = "planks", ["hinge"] = "left", ["limits"] = "-110 110", ["spring"] = "0", ["damper"] = "3", ["closed_angle"] = "0", ["motor"] = "0", ["motor_torque"] = "60", ["break_force"] = "0" },
                numericProps = { ("spring", 5f), ("motor", 15f) },
                spawnEntity = e => DoorEntity(e), preview = e => Door(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_hinge", description = "Prop on a hinge (axis \"x y z\" local, pivot \"x y z\" offset from the centre; limits; motor deg/s, motor_torque; spring, damper, target)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.6f, 0.6f, 0.6f),
                defaults = { ["size"] = "3 0.2 0.6", ["mass"] = "20", ["texture"] = "planks", ["axis"] = "0 0 1", ["pivot"] = "0 0 0", ["limits"] = "", ["motor"] = "0", ["motor_torque"] = "100", ["spring"] = "0", ["damper"] = "2", ["target"] = "0", ["break_force"] = "0" },
                numericProps = { ("motor", 15f), ("spring", 5f) },
                spawnEntity = e => HingedPropEntity(e), preview = e => HingedProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_slider", description = "Prop on a rail (axis \"x y z\" local; limits \"min max\" m; motor m/s, motor_force, patrol 1 = reverse at the limits; spring N/m, damper, target)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.6f, 0.6f, 0.6f),
                defaults = { ["size"] = "1 1 1", ["mass"] = "15", ["texture"] = "orange", ["axis"] = "1 0 0", ["limits"] = "-2 2", ["motor"] = "0", ["motor_force"] = "300", ["patrol"] = "0", ["spring"] = "0", ["damper"] = "10", ["target"] = "0", ["break_force"] = "0" },
                numericProps = { ("motor", 0.25f), ("spring", 25f) },
                spawnEntity = e => SliderPropEntity(e), preview = e => SliderProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_pendulum", description = "Prop hanging on a rod (or rope 1) from the origin (length; start_angle deg; size, mass, shape)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.3f, 0.3f, 0.3f),
                defaults = { ["size"] = "0.6 0.6 0.6", ["mass"] = "15", ["texture"] = "red", ["shape"] = "sphere", ["length"] = "3", ["rope"] = "0", ["start_angle"] = "0", ["break_force"] = "0" },
                numericProps = { ("length", 0.25f), ("start_angle", 5f) },
                spawnEntity = e => PendulumEntity(e), preview = e => Pendulum(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_spring_prop", description = "Prop hanging on a spring from the origin (length = rest length; stiffness N/m, damper N·s/m; max_length rope limit, 0 = none)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.3f, 0.3f, 0.3f),
                defaults = { ["size"] = "0.8 0.8 0.8", ["mass"] = "10", ["texture"] = "green", ["length"] = "2", ["stiffness"] = "150", ["damper"] = "6", ["max_length"] = "0", ["break_force"] = "0" },
                numericProps = { ("stiffness", 25f), ("length", 0.25f) },
                spawnEntity = e => SpringPropEntity(e), preview = e => SpringProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_fixed", description = "Prop welded in place; breaks loose above break_force (N) / break_torque (N·m)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.5f, 0.5f, 0.5f),
                defaults = { ["size"] = "1 1 1", ["mass"] = "10", ["texture"] = "purple", ["break_force"] = "400", ["break_torque"] = "0" },
                numericProps = { ("break_force", 50f), ("mass", 1f) },
                spawnEntity = e => FixedPropEntity(e), preview = e => FixedProp(e, true),
            });

            // connectors
            foreach (var (cls, desc) in new[]
            {
                ("joint_hinge", "Hinge between body and connected (targetnames) at the origin about axis (limits, motor deg/s, motor_torque, spring, damper, target)"),
                ("joint_slider", "Slider between body and connected along axis (limits m, motor m/s, motor_force, spring, damper, target)"),
                ("joint_fixed", "Weld between body and connected at the origin"),
                ("joint_ball", "Ball-and-socket between body and connected at the origin"),
                ("joint_distance", "Rod / rope between body (anchor = origin) and connected (connected_anchor; min, max, rope 1)"),
                ("joint_spring", "Spring between body (anchor = origin) and connected (connected_anchor; stiffness, damper, rest, min, max)"),
            })
            {
                string c = cls;
                add(new EntityDefinition
                {
                    classname = c, description = desc, gizmoColor = JointColor, pickExtents = new Vector3(0.25f, 0.25f, 0.25f), deferred = true,
                    defaults = JointDefaults(c),
                    spawnEntity = e => Connector(c, e),
                    preview = e => JointGizmo(c, e),
                });
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> JointDefaults(string cls)
        {
            var d = new System.Collections.Generic.Dictionary<string, string> { ["body"] = "", ["connected"] = "", ["break_force"] = "0", ["break_torque"] = "0", ["collide"] = "0" };
            switch (cls)
            {
                case "joint_hinge": d["axis"] = "0 1 0"; d["limits"] = ""; d["motor"] = "0"; d["motor_torque"] = "100"; d["spring"] = "0"; d["damper"] = "2"; d["target"] = "0"; break;
                case "joint_slider": d["axis"] = "1 0 0"; d["limits"] = ""; d["motor"] = "0"; d["motor_force"] = "300"; d["spring"] = "0"; d["damper"] = "10"; d["target"] = "0"; break;
                case "joint_distance": d["connected_anchor"] = ""; d["min"] = ""; d["max"] = ""; d["rope"] = "0"; break;
                case "joint_spring": d["connected_anchor"] = ""; d["stiffness"] = "100"; d["damper"] = "5"; d["rest"] = ""; d["min"] = "0"; d["max"] = "0"; break;
            }
            return d;
        }

        // ------------------------------------------------------------------ props

        /// <summary>prop_physics: a box / sphere / capsule rigid body; the origin is the bottom centre.</summary>
        public static Entity PropEntity(MapEntity e)
        {
            Vector3 size = Size(e);
            return BodyEntity(e, e.origin + Vector3.up * (size.y * 0.5f + 0.02f), Quaternion.Euler(0f, e.angle, 0f));
        }

        /// <summary>The editor's preview prop (a GameObject until the tools are ported).</summary>
        public static GameObject Prop(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            return PreviewBody(e, e.origin + Vector3.up * (size.y * 0.5f + 0.02f), Quaternion.Euler(0f, e.angle, 0f), "prop_physics");
        }

        /// <summary>
        /// The prop itself (9.2: no GameObject half — the entity renders, simulates and is the level's named anchor,
        /// its spawn pose recorded for the connectors' pose math). The box mesh is cached per size and UV: a fresh one
        /// was interned per prop per build since layer 2 — 13 meshes leaked on every rebuild of the dock.
        /// </summary>
        public static Entity BodyEntity(MapEntity e, Vector3 center, Quaternion rotation)
        {
            Vector3 size = Size(e);
            string shapeName = e.Get("shape", "box");
            bool sphere = shapeName.Equals("sphere", StringComparison.OrdinalIgnoreCase);
            bool capsule = shapeName.Equals("capsule", StringComparison.OrdinalIgnoreCase);
            float mass = Mathf.Max(e.GetFloat("mass", 10f), 0.1f);
            var tex = LevelTextures.Get(e.Get("texture", "orange"));
            var shape = sphere ? new ColliderShape { Kind = ShapeKind.Sphere, Radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f }
                : capsule ? new ColliderShape { Kind = ShapeKind.Capsule, Radius = Mathf.Max(size.x, size.z) * 0.5f, Height = Mathf.Max(size.y, Mathf.Max(size.x, size.z)) }
                : new ColliderShape { Kind = ShapeKind.Box, Size = size };
            var world = Application.Current.World;
            var ent = RipNRage.Engine.Ecs.Physics.PhysicsEntities.Dynamic(world, new LocalTransform { Position = center, Rotation = rotation, Scale = Vector3.one }, shape, mass);
            world.Commands.Add(ent, new BodyConfig { mass = mass, collisionDetection = CollisionDetectionMode.Continuous }.Bake());
            Surfaces.Tag(world, ent, tex.surface); // what a bullet kicks up off it
            world.Commands.Add(ent, LevelVisuals.Render(LevelVisuals.Box(Vector3.zero, size, 1f / Mathf.Max(size.x, 0.01f)),
                                                        tex.Texture ?? Texture2D.White, tex.tint));
            LevelLoader.Loading?.RegisterNamed(e.Get("targetname"), ent, center, rotation);
            return ent;
        }

        /// <summary>The preview's box body (renderer only; a GameObject until the tools are ported).</summary>
        private static GameObject PreviewBody(MapEntity e, Vector3 center, Quaternion rotation, string name)
        {
            Vector3 size = Size(e);
            var tex = LevelTextures.Get(e.Get("texture", "orange"));
            var go = new GameObject(name);
            go.transform.position = center;
            go.transform.rotation = rotation;
            go.layer = Layers.Default;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, 1f / Mathf.Max(size.x, 0.01f));
            mr.texture = tex.Texture; mr.color = tex.tint;
            return go;
        }

        private static void AddJoint(Entity e, in Joint j)
        {
            if (!e.IsNull) Application.Current.World.Commands.Add(e, j);
        }

        private static Joint Hinge(Vector3 anchor, Vector3 axis, MapEntity e, string targetKey)
        {
            var j = new JointConfig { kind = JointKind.Hinge }.Bake();
            j.Anchor = anchor; j.Axis = axis;
            if (Limits(e, out float lo, out float hi)) { j.UseLimits = true; j.LowerLimit = lo; j.UpperLimit = hi; }
            float motor = e.GetFloat("motor", 0f);
            if (motor != 0f) { j.UseMotor = true; j.MotorSpeed = motor; j.MotorForce = Mathf.Max(e.GetFloat("motor_torque", 60f), 0f); }
            float spring = e.GetFloat("spring", 0f);
            if (spring > 0f) { j.UseSpring = true; j.SpringStiffness = spring; j.SpringDamping = Mathf.Max(e.GetFloat("damper", 2f), 0f); j.SpringTarget = e.GetFloat(targetKey, 0f); }
            return Breakable(j, e);
        }

        private static Joint Slider(Vector3 anchor, Vector3 axis, MapEntity e)
        {
            var j = new JointConfig { kind = JointKind.Slider }.Bake();
            j.Anchor = anchor; j.Axis = axis;
            if (Limits(e, out float lo, out float hi)) { j.UseLimits = true; j.LowerLimit = lo; j.UpperLimit = hi; }
            float motor = e.GetFloat("motor", 0f);
            if (motor != 0f) { j.UseMotor = true; j.MotorSpeed = motor; j.MotorForce = Mathf.Max(e.GetFloat("motor_force", 300f), 0f); }
            float spring = e.GetFloat("spring", 0f);
            if (spring > 0f) { j.UseSpring = true; j.SpringStiffness = spring; j.SpringDamping = Mathf.Max(e.GetFloat("damper", 10f), 0f); j.SpringTarget = e.GetFloat("target", 0f); }
            return Breakable(j, e);
        }

        private static Joint Breakable(Joint j, MapEntity e)
        {
            float f = e.GetFloat("break_force", 0f), t = e.GetFloat("break_torque", 0f);
            j.BreakForce = f > 0f ? f : float.PositiveInfinity;
            j.BreakTorque = t > 0f ? t : float.PositiveInfinity;
            return j;
        }

        private static Vector3 Size(MapEntity e)
        {
            var size = e.GetVector("size", new Vector3(1f, 1f, 1f));
            if (size.x <= 0f) size.x = 1f; if (size.y <= 0f) size.y = size.x; if (size.z <= 0f) size.z = size.x;
            return size;
        }

        // ------------------------------------------------------------------ self-contained machines

        /// <summary>phys_door: the panel stands on its origin, hinged along the chosen edge to the world.</summary>
        private static Entity DoorEntity(MapEntity e)
        {
            Vector3 size = Size(e);
            Vector3 center = e.origin + Vector3.up * (size.y * 0.5f + 0.01f);
            var ent = BodyEntity(e, center, Quaternion.Euler(0f, e.angle, 0f));
            string hinge = e.Get("hinge", "left").ToLowerInvariant();
            Vector3 localAnchor, localAxis;
            switch (hinge)
            {
                case "right": localAnchor = new Vector3(size.x * 0.5f, 0f, 0f); localAxis = Vector3.up; break;
                case "top": localAnchor = new Vector3(0f, size.y * 0.5f, 0f); localAxis = Vector3.right; break;
                case "bottom": localAnchor = new Vector3(0f, -size.y * 0.5f, 0f); localAxis = Vector3.right; break;
                default: localAnchor = new Vector3(-size.x * 0.5f, 0f, 0f); localAxis = Vector3.up; break;
            }
            AddJoint(ent, Hinge(localAnchor, localAxis, e, "closed_angle"));
            return ent;
        }

        private static GameObject Door(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            return PreviewBody(e, e.origin + Vector3.up * (size.y * 0.5f + 0.01f), Quaternion.Euler(0f, e.angle, 0f), "phys_door");
        }

        /// <summary>phys_hinge: a prop turning about a local axis through its centre (+ pivot offset), hinged to the world.</summary>
        private static Entity HingedPropEntity(MapEntity e)
        {
            Vector3 size = Size(e);
            var ent = BodyEntity(e, e.origin + Vector3.up * (size.y * 0.5f + 0.01f), Quaternion.Euler(0f, e.angle, 0f));
            AddJoint(ent, Hinge(e.GetVector("pivot", Vector3.zero), Axis(e, Vector3.forward), e, "target"));
            return ent;
        }

        private static GameObject HingedProp(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            return PreviewBody(e, e.origin + Vector3.up * (size.y * 0.5f + 0.01f), Quaternion.Euler(0f, e.angle, 0f), "phys_hinge");
        }

        /// <summary>phys_slider: a prop on a rail through its centre along a local axis, fixed to the world.</summary>
        private static Entity SliderPropEntity(MapEntity e)
        {
            Vector3 size = Size(e);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 center = e.origin + Vector3.up * (size.y * 0.5f + 0.01f);
            var ent = BodyEntity(e, center, rot);
            var sj = Slider(Vector3.zero, Axis(e, Vector3.right), e);
            AddJoint(ent, sj);
            if (sj.UseMotor && sj.UseLimits && e.GetInt("patrol", 0) != 0)
                Application.Current.World.Commands.Add(ent, new SliderPatrol { Origin = center, Axis = (rot * sj.Axis).normalized, Lower = sj.LowerLimit, Upper = sj.UpperLimit });
            return ent;
        }

        private static GameObject SliderProp(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            return PreviewBody(e, e.origin + Vector3.up * (size.y * 0.5f + 0.01f), Quaternion.Euler(0f, e.angle, 0f), "phys_slider");
        }

        /// <summary>phys_pendulum: the origin is the pivot; the bob hangs <c>length</c> below it on a rod (or a rope), optionally swung out by <c>start_angle</c>.</summary>
        private static Entity PendulumEntity(MapEntity e)
        {
            float length = Mathf.Max(e.GetFloat("length", 3f), 0.1f);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 down = Quaternion.AngleAxis(e.GetFloat("start_angle", 0f), rot * Vector3.forward) * Vector3.down;
            Vector3 center = e.origin + down * length;
            var ent = BodyEntity(e, center, rot);
            RodEntity(ent, e.origin, center);
            var pj = new JointConfig { kind = JointKind.Distance, connectedAnchor = new[] { e.origin.x, e.origin.y, e.origin.z }, rope = e.GetInt("rope", 0) != 0 }.Bake();
            AddJoint(ent, Breakable(pj, e));
            return ent;
        }

        private static GameObject Pendulum(MapEntity e, bool preview)
        {
            float length = Mathf.Max(e.GetFloat("length", 3f), 0.1f);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 down = Quaternion.AngleAxis(e.GetFloat("start_angle", 0f), rot * Vector3.forward) * Vector3.down;
            Vector3 center = e.origin + down * length;
            var go = PreviewBody(e, center, rot, "phys_pendulum");
            PreviewRod(go, e.origin, center);
            return go;
        }

        /// <summary>phys_spring_prop: the origin is the hook; the prop hangs on a spring at its rest length.</summary>
        private static Entity SpringPropEntity(MapEntity e)
        {
            float length = Mathf.Max(e.GetFloat("length", 2f), 0.1f);
            Vector3 center = e.origin + Vector3.down * length;
            var ent = BodyEntity(e, center, Quaternion.Euler(0f, e.angle, 0f));
            RodEntity(ent, e.origin, center, thin: true);
            float max = e.GetFloat("max_length", 0f);
            var spj = new JointConfig
            {
                kind = JointKind.Spring, connectedAnchor = new[] { e.origin.x, e.origin.y, e.origin.z },
                stiffness = Mathf.Max(e.GetFloat("stiffness", 150f), 0f), damping = Mathf.Max(e.GetFloat("damper", 6f), 0f), maxDistance = max > 0f ? max : -1f,
            }.Bake();
            AddJoint(ent, Breakable(spj, e));
            return ent;
        }

        private static GameObject SpringProp(MapEntity e, bool preview)
        {
            float length = Mathf.Max(e.GetFloat("length", 2f), 0.1f);
            Vector3 center = e.origin + Vector3.down * length;
            var go = PreviewBody(e, center, Quaternion.Euler(0f, e.angle, 0f), "phys_spring_prop");
            PreviewRod(go, e.origin, center, thin: true);
            return go;
        }

        /// <summary>phys_fixed: welded where it stands; a strong enough hit (break_force) frees it.</summary>
        private static Entity FixedPropEntity(MapEntity e)
        {
            var ent = PropEntity(e);
            AddJoint(ent, Breakable(new JointConfig { kind = JointKind.Fixed }.Bake(), e));
            return ent;
        }

        private static GameObject FixedProp(MapEntity e, bool preview)
        {
            var go = Prop(e, preview);
            go.name = "phys_fixed";
            return go;
        }

        // ------------------------------------------------------------------ connectors

        /// <summary>Joints between named props (9.2: the names map to entities; the pose math uses the spawn poses the
        /// level recorded — the entities themselves go through the command buffer and are not alive until playback).</summary>
        private static Entity Connector(string cls, MapEntity e)
        {
            var level = LevelLoader.Loading;
            if (level == null || !level.namedEntities.TryGetValue(e.Get("body") ?? "", out var body) || body.entity.IsNull)
            { Debug.LogWarning($"{cls} at {e.origin}: body '{e.Get("body")}' not found or not a physics prop (set targetname on a prop)"); return Entity.Null; }
            Entity connected = Entity.Null;
            Vector3 connectedPos = default; Quaternion connectedRot = Quaternion.identity;
            string connectedName = e.Get("connected");
            if (!string.IsNullOrEmpty(connectedName))
            {
                if (level.namedEntities.TryGetValue(connectedName, out var c) && !c.entity.IsNull)
                { connected = c.entity; connectedPos = c.position; connectedRot = c.rotation; }
                else Debug.LogWarning($"{cls}: connected '{connectedName}' not found or not a physics prop; joining to the world");
            }
            Vector3 anchor = e.origin;
            var bodyInverse = Quaternion.Inverse(body.rotation);
            Vector3 localAnchor = bodyInverse * (anchor - body.position);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 worldAxis = rot * e.GetVector("axis", cls == "joint_slider" ? Vector3.right : Vector3.up);
            if (worldAxis.sqrMagnitude < 1E-06f) worldAxis = Vector3.up;
            Vector3 localAxis = bodyInverse * worldAxis.normalized;
            bool hasConnectedAnchor = e.properties.ContainsKey("connected_anchor") && !string.IsNullOrWhiteSpace(e.Get("connected_anchor"));
            Vector3 connectedAnchor = hasConnectedAnchor ? e.GetVector("connected_anchor") : (!connected.IsNull ? connectedPos : anchor);

            Joint j;
            switch (cls)
            {
                case "joint_hinge": j = Hinge(localAnchor, localAxis, e, "target"); break;
                case "joint_slider": j = Slider(localAnchor, localAxis, e); break;
                case "joint_fixed": j = Breakable(new JointConfig { kind = JointKind.Fixed }.Bake(), e); j.Anchor = localAnchor; break;
                case "joint_ball": j = Breakable(new JointConfig { kind = JointKind.Ball }.Bake(), e); j.Anchor = localAnchor; break;
                case "joint_distance":
                {
                    j = Breakable(new JointConfig { kind = JointKind.Distance, rope = e.GetInt("rope", 0) != 0 }.Bake(), e);
                    bool hasMin = !string.IsNullOrWhiteSpace(e.Get("min")), hasMax = !string.IsNullOrWhiteSpace(e.Get("max"));
                    if (hasMin || hasMax)
                    {
                        float cur = (connectedAnchor - anchor).magnitude;
                        j.AutoDistance = false;
                        j.MinDistance = hasMin ? e.GetFloat("min") : (j.Rope ? 0f : cur);
                        j.MaxDistance = hasMax ? e.GetFloat("max") : cur;
                    }
                    break;
                }
                case "joint_spring":
                {
                    float max = e.GetFloat("max", 0f);
                    j = Breakable(new JointConfig
                    {
                        kind = JointKind.Spring, stiffness = Mathf.Max(e.GetFloat("stiffness", 100f), 0f), damping = Mathf.Max(e.GetFloat("damper", 5f), 0f),
                        restLength = string.IsNullOrWhiteSpace(e.Get("rest")) ? -1f : e.GetFloat("rest"), maxDistance = max > 0f ? max : -1f,
                    }.Bake(), e);
                    j.MinDistance = Mathf.Max(e.GetFloat("min", 0f), 0f);
                    break;
                }
                default: return Entity.Null;
            }
            j.Anchor = localAnchor;
            j.Connected = connected;
            // ball / fixed / hinge / slider join at the anchor where the bodies stand now; only rods and springs
            // reach to a separate connected anchor (the other body's centre by default)
            bool reaches = cls == "joint_distance" || cls == "joint_spring";
            j.AutoConnectedAnchor = !reaches;
            if (reaches) j.ConnectedAnchor = !connected.IsNull ? Quaternion.Inverse(connectedRot) * (connectedAnchor - connectedPos) : connectedAnchor;
            j.CollideConnected = e.GetInt("collide", 0) != 0;
            Application.Current.World.Commands.Add(body.entity, j);
            return Entity.Null; // the joint rides on the body; there is no connector entity to key
        }

        private static Vector3 Axis(MapEntity e, Vector3 def)
        {
            Vector3 a = e.GetVector("axis", def);
            return a.sqrMagnitude > 1E-06f ? a.normalized : def;
        }

        private static bool Limits(MapEntity e, out float min, out float max)
        {
            min = max = 0f;
            string v = e.Get("limits");
            if (string.IsNullOrWhiteSpace(v)) return false;
            var p = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max)) return false;
            if (min > max) { float t = min; min = max; max = t; }
            return true;
        }

        /// <summary>The game's rod (9.2): a render entity <see cref="RodSystem"/> keeps stretched from the hook to the prop.</summary>
        private static void RodEntity(Entity prop, Vector3 hook, Vector3 propCenter, bool thin = false)
        {
            float w = thin ? 0.04f : 0.07f;
            var world = Application.Current.World;
            var rod = world.CreateEntity();
            var lt = new LocalTransform { Position = propCenter, Rotation = Quaternion.identity, Scale = Vector3.one };
            RodSystem.Place(ref lt, propCenter, hook, w);
            world.Add(rod, lt);
            world.Add(rod, LevelVisuals.Render(LevelVisuals.Box(new Vector3(0f, 0.5f, 0f), Vector3.one),
                                               Texture2D.White, new Color(0.35f, 0.33f, 0.3f, 1f), castsShadow: false));
            world.Add(rod, new RodVisual { Target = prop, Hook = hook, Width = w });
        }

        /// <summary>The preview's rod (static; a GameObject until the tools are ported).</summary>
        private static void PreviewRod(GameObject prop, Vector3 from, Vector3 to, bool thin = false)
        {
            var rod = new GameObject("Rod");
            rod.transform.SetParent(prop.transform, false);
            var mr = rod.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(new Vector3(0f, 0.5f, 0f), Vector3.one, 1f);
            mr.texture = Texture2D.White; mr.color = new Color(0.35f, 0.33f, 0.3f, 1f); mr.castsShadow = false;
            float w = thin ? 0.04f : 0.07f;
            Vector3 d = to - from;
            if (d.magnitude >= 0.01f)
            {
                rod.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                rod.transform.position = from;
                rod.transform.localScale = new Vector3(w, d.magnitude, w);
            }
        }

        // ------------------------------------------------------------------ editor gizmos

        private static GameObject JointGizmo(string cls, MapEntity e)
        {
            var go = new GameObject(cls);
            go.transform.position = e.origin;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one * 0.3f, 1f);
            mr.texture = Texture2D.White; mr.color = JointColor; mr.emissive = 0.8f; mr.castsShadow = false;
            if (cls == "joint_hinge" || cls == "joint_slider")
            {
                Vector3 axis = Quaternion.Euler(0f, e.angle, 0f) * e.GetVector("axis", cls == "joint_slider" ? Vector3.right : Vector3.up);
                if (axis.sqrMagnitude < 1E-06f) axis = Vector3.up;
                var bar = new GameObject("Axis"); bar.transform.SetParent(go.transform, false);
                var bm = bar.AddComponent<MeshRenderer>();
                bm.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.08f, 1.2f, 0.08f), 1f);
                bm.texture = Texture2D.White; bm.color = JointColor; bm.emissive = 0.8f; bm.castsShadow = false;
                bar.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            }
            return go;
        }
    }
}
