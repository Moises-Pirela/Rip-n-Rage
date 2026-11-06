using System;
using System.Globalization;
using RipNRage.Engine.Graphics;
using RipNRage.Level;

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
                spawn = e => Door(e, false), preview = e => Door(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_hinge", description = "Prop on a hinge (axis \"x y z\" local, pivot \"x y z\" offset from the centre; limits; motor deg/s, motor_torque; spring, damper, target)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.6f, 0.6f, 0.6f),
                defaults = { ["size"] = "3 0.2 0.6", ["mass"] = "20", ["texture"] = "planks", ["axis"] = "0 0 1", ["pivot"] = "0 0 0", ["limits"] = "", ["motor"] = "0", ["motor_torque"] = "100", ["spring"] = "0", ["damper"] = "2", ["target"] = "0", ["break_force"] = "0" },
                numericProps = { ("motor", 15f), ("spring", 5f) },
                spawn = e => HingedProp(e, false), preview = e => HingedProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_slider", description = "Prop on a rail (axis \"x y z\" local; limits \"min max\" m; motor m/s, motor_force, patrol 1 = reverse at the limits; spring N/m, damper, target)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.6f, 0.6f, 0.6f),
                defaults = { ["size"] = "1 1 1", ["mass"] = "15", ["texture"] = "orange", ["axis"] = "1 0 0", ["limits"] = "-2 2", ["motor"] = "0", ["motor_force"] = "300", ["patrol"] = "0", ["spring"] = "0", ["damper"] = "10", ["target"] = "0", ["break_force"] = "0" },
                numericProps = { ("motor", 0.25f), ("spring", 25f) },
                spawn = e => SliderProp(e, false), preview = e => SliderProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_pendulum", description = "Prop hanging on a rod (or rope 1) from the origin (length; start_angle deg; size, mass, shape)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.3f, 0.3f, 0.3f),
                defaults = { ["size"] = "0.6 0.6 0.6", ["mass"] = "15", ["texture"] = "red", ["shape"] = "sphere", ["length"] = "3", ["rope"] = "0", ["start_angle"] = "0", ["break_force"] = "0" },
                numericProps = { ("length", 0.25f), ("start_angle", 5f) },
                spawn = e => Pendulum(e, false), preview = e => Pendulum(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_spring_prop", description = "Prop hanging on a spring from the origin (length = rest length; stiffness N/m, damper N·s/m; max_length rope limit, 0 = none)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.3f, 0.3f, 0.3f),
                defaults = { ["size"] = "0.8 0.8 0.8", ["mass"] = "10", ["texture"] = "green", ["length"] = "2", ["stiffness"] = "150", ["damper"] = "6", ["max_length"] = "0", ["break_force"] = "0" },
                numericProps = { ("stiffness", 25f), ("length", 0.25f) },
                spawn = e => SpringProp(e, false), preview = e => SpringProp(e, true),
            });
            add(new EntityDefinition
            {
                classname = "phys_fixed", description = "Prop welded in place; breaks loose above break_force (N) / break_torque (N·m)",
                gizmoColor = PropColor, pickExtents = new Vector3(0.5f, 0.5f, 0.5f),
                defaults = { ["size"] = "1 1 1", ["mass"] = "10", ["texture"] = "purple", ["break_force"] = "400", ["break_torque"] = "0" },
                numericProps = { ("break_force", 50f), ("mass", 1f) },
                spawn = e => FixedProp(e, false), preview = e => FixedProp(e, true),
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
                    spawn = e => Connector(c, e),
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
        public static GameObject Prop(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            return Body(e, preview, e.origin + Vector3.up * (size.y * 0.5f + 0.02f), rot, "prop_physics");
        }

        /// <summary>Builds the prop body (renderer, collider, rigid body) centred at <paramref name="center"/>.</summary>
        public static GameObject Body(MapEntity e, bool preview, Vector3 center, Quaternion rotation, string name)
        {
            Vector3 size = Size(e);
            string shapeName = e.Get("shape", "box");
            bool sphere = shapeName.Equals("sphere", StringComparison.OrdinalIgnoreCase);
            bool capsule = shapeName.Equals("capsule", StringComparison.OrdinalIgnoreCase);
            float mass = Mathf.Max(e.GetFloat("mass", 10f), 0.1f);
            var tex = LevelTextures.Get(e.Get("texture", "orange"));
            var go = new GameObject(name);
            go.transform.position = center;
            go.transform.rotation = rotation;
            go.layer = Layers.Default;
            go.AddComponent<SurfaceMaterial>().surface = tex.surface;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, 1f / Mathf.Max(size.x, 0.01f));
            mr.texture = tex.Texture; mr.color = tex.tint;
            if (preview) return go;
            if (sphere) { var sc = go.AddComponent<SphereCollider>(); sc.radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f; }
            else if (capsule) { var cc = go.AddComponent<CapsuleCollider>(); cc.radius = Mathf.Max(size.x, size.z) * 0.5f; cc.height = Mathf.Max(size.y, cc.radius * 2f); }
            else { var bc = go.AddComponent<BoxCollider>(); bc.size = size; }
            var rb = go.AddComponent<RigidBody>();
            rb.mass = mass;
            rb.collisionDetection = CollisionDetectionMode.Continuous;
            return go;
        }

        private static Vector3 Size(MapEntity e)
        {
            var size = e.GetVector("size", new Vector3(1f, 1f, 1f));
            if (size.x <= 0f) size.x = 1f; if (size.y <= 0f) size.y = size.x; if (size.z <= 0f) size.z = size.x;
            return size;
        }

        // ------------------------------------------------------------------ self-contained machines

        /// <summary>phys_door: the panel stands on its origin, hinged along the chosen edge to the world.</summary>
        private static GameObject Door(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 center = e.origin + Vector3.up * (size.y * 0.5f + 0.01f);
            var go = Body(e, preview, center, rot, "phys_door");
            if (preview) return go;
            string hinge = e.Get("hinge", "left").ToLowerInvariant();
            Vector3 localAnchor, localAxis;
            switch (hinge)
            {
                case "right": localAnchor = new Vector3(size.x * 0.5f, 0f, 0f); localAxis = Vector3.up; break;
                case "top": localAnchor = new Vector3(0f, size.y * 0.5f, 0f); localAxis = Vector3.right; break;
                case "bottom": localAnchor = new Vector3(0f, -size.y * 0.5f, 0f); localAxis = Vector3.right; break;
                default: localAnchor = new Vector3(-size.x * 0.5f, 0f, 0f); localAxis = Vector3.up; break;
            }
            var j = go.AddComponent<HingeJoint>();
            j.anchor = localAnchor; j.axis = localAxis;
            ApplyHingeKeys(j, e, "closed_angle");
            Breakable(j, e);
            return go;
        }

        /// <summary>phys_hinge: a prop turning about a local axis through its centre (+ pivot offset), hinged to the world.</summary>
        private static GameObject HingedProp(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 center = e.origin + Vector3.up * (size.y * 0.5f + 0.01f);
            var go = Body(e, preview, center, rot, "phys_hinge");
            if (preview) return go;
            var j = go.AddComponent<HingeJoint>();
            j.anchor = e.GetVector("pivot", Vector3.zero);
            j.axis = Axis(e, Vector3.forward);
            ApplyHingeKeys(j, e, "target");
            Breakable(j, e);
            return go;
        }

        /// <summary>phys_slider: a prop on a rail through its centre along a local axis, fixed to the world.</summary>
        private static GameObject SliderProp(MapEntity e, bool preview)
        {
            Vector3 size = Size(e);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 center = e.origin + Vector3.up * (size.y * 0.5f + 0.01f);
            var go = Body(e, preview, center, rot, "phys_slider");
            if (preview) return go;
            var j = go.AddComponent<SliderJoint>();
            j.anchor = Vector3.zero;
            j.axis = Axis(e, Vector3.right);
            ApplySliderKeys(j, e);
            Breakable(j, e);
            if (j.useMotor && j.useLimits && e.GetInt("patrol", 0) != 0) go.AddComponent<SliderPatrol>();
            return go;
        }

        /// <summary>phys_pendulum: the origin is the pivot; the bob hangs <c>length</c> below it on a rod (or a rope), optionally swung out by <c>start_angle</c>.</summary>
        private static GameObject Pendulum(MapEntity e, bool preview)
        {
            float length = Mathf.Max(e.GetFloat("length", 3f), 0.1f);
            float startAngle = e.GetFloat("start_angle", 0f);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 down = Quaternion.AngleAxis(startAngle, rot * Vector3.forward) * Vector3.down;
            Vector3 center = e.origin + down * length;
            var go = Body(e, preview, center, rot, "phys_pendulum");
            Rod(go, e.origin, center, preview);
            if (preview) return go;
            var j = go.AddComponent<DistanceJoint>();
            j.anchor = Vector3.zero;
            j.autoConfigureConnectedAnchor = false; j.connectedAnchor = e.origin;
            j.rope = e.GetInt("rope", 0) != 0;
            j.autoConfigureDistance = true;
            Breakable(j, e);
            return go;
        }

        /// <summary>phys_spring_prop: the origin is the hook; the prop hangs on a spring at its rest length.</summary>
        private static GameObject SpringProp(MapEntity e, bool preview)
        {
            float length = Mathf.Max(e.GetFloat("length", 2f), 0.1f);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 center = e.origin + Vector3.down * length;
            var go = Body(e, preview, center, rot, "phys_spring_prop");
            Rod(go, e.origin, center, preview, thin: true);
            if (preview) return go;
            var j = go.AddComponent<SpringJoint>();
            j.anchor = Vector3.zero;
            j.autoConfigureConnectedAnchor = false; j.connectedAnchor = e.origin;
            j.spring = Mathf.Max(e.GetFloat("stiffness", 150f), 0f);
            j.damper = Mathf.Max(e.GetFloat("damper", 6f), 0f);
            j.autoConfigureDistance = true;
            float max = e.GetFloat("max_length", 0f);
            j.maxDistance = max > 0f ? max : float.PositiveInfinity;
            Breakable(j, e);
            return go;
        }

        /// <summary>phys_fixed: welded where it stands; a strong enough hit (break_force) frees it.</summary>
        private static GameObject FixedProp(MapEntity e, bool preview)
        {
            var go = Prop(e, preview);
            go.name = "phys_fixed";
            if (preview) return go;
            var j = go.AddComponent<FixedJoint>();
            j.anchor = Vector3.zero;
            Breakable(j, e);
            return go;
        }

        // ------------------------------------------------------------------ connectors

        private static GameObject Connector(string cls, MapEntity e)
        {
            var level = LevelLoader.Loading;
            var bodyGo = level?.Find(e.Get("body"));
            if (bodyGo == null) { Debug.LogWarning($"{cls} at {e.origin}: body '{e.Get("body")}' not found (set targetname on a prop)"); return null; }
            var body = bodyGo.GetComponent<RigidBody>();
            if (body == null) { Debug.LogWarning($"{cls}: '{e.Get("body")}' has no RigidBody"); return null; }
            RigidBody connected = null;
            string connectedName = e.Get("connected");
            if (!string.IsNullOrEmpty(connectedName))
            {
                var cgo = level.Find(connectedName);
                connected = cgo != null ? cgo.GetComponent<RigidBody>() : null;
                if (connected == null) Debug.LogWarning($"{cls}: connected '{connectedName}' not found or has no RigidBody; joining to the world");
            }
            Vector3 anchor = e.origin;
            Vector3 localAnchor = bodyGo.transform.InverseTransformPoint(anchor);
            Quaternion rot = Quaternion.Euler(0f, e.angle, 0f);
            Vector3 worldAxis = rot * e.GetVector("axis", cls == "joint_slider" ? Vector3.right : Vector3.up);
            if (worldAxis.sqrMagnitude < 1E-06f) worldAxis = Vector3.up;
            Vector3 localAxis = bodyGo.transform.InverseTransformDirection(worldAxis.normalized);
            bool hasConnectedAnchor = e.properties.ContainsKey("connected_anchor") && !string.IsNullOrWhiteSpace(e.Get("connected_anchor"));
            Vector3 connectedAnchor = hasConnectedAnchor ? e.GetVector("connected_anchor") : (connected != null ? connected.transform.position : anchor);

            Joint j;
            switch (cls)
            {
                case "joint_hinge":
                {
                    var h = bodyGo.AddComponent<HingeJoint>();
                    h.anchor = localAnchor; h.axis = localAxis;
                    ApplyHingeKeys(h, e, "target");
                    j = h; break;
                }
                case "joint_slider":
                {
                    var s = bodyGo.AddComponent<SliderJoint>();
                    s.anchor = localAnchor; s.axis = localAxis;
                    ApplySliderKeys(s, e);
                    j = s; break;
                }
                case "joint_fixed":
                {
                    var f = bodyGo.AddComponent<FixedJoint>(); f.anchor = localAnchor; j = f; break;
                }
                case "joint_ball":
                {
                    var b = bodyGo.AddComponent<BallJoint>(); b.anchor = localAnchor; j = b; break;
                }
                case "joint_distance":
                {
                    var d = bodyGo.AddComponent<DistanceJoint>();
                    d.anchor = localAnchor;
                    d.autoConfigureConnectedAnchor = false;
                    d.connectedAnchor = connected != null ? connected.transform.InverseTransformPoint(connectedAnchor) : connectedAnchor;
                    d.rope = e.GetInt("rope", 0) != 0;
                    bool hasMin = !string.IsNullOrWhiteSpace(e.Get("min")), hasMax = !string.IsNullOrWhiteSpace(e.Get("max"));
                    if (hasMin || hasMax)
                    {
                        float cur = (connectedAnchor - anchor).magnitude;
                        d.autoConfigureDistance = false;
                        d.minDistance = hasMin ? e.GetFloat("min") : (d.rope ? 0f : cur);
                        d.maxDistance = hasMax ? e.GetFloat("max") : cur;
                    }
                    j = d; break;
                }
                case "joint_spring":
                {
                    var s = bodyGo.AddComponent<SpringJoint>();
                    s.anchor = localAnchor;
                    s.autoConfigureConnectedAnchor = false;
                    s.connectedAnchor = connected != null ? connected.transform.InverseTransformPoint(connectedAnchor) : connectedAnchor;
                    s.spring = Mathf.Max(e.GetFloat("stiffness", 100f), 0f); s.damper = Mathf.Max(e.GetFloat("damper", 5f), 0f);
                    if (!string.IsNullOrWhiteSpace(e.Get("rest"))) { s.autoConfigureDistance = false; s.restDistance = e.GetFloat("rest"); }
                    s.minDistance = Mathf.Max(e.GetFloat("min", 0f), 0f);
                    float max = e.GetFloat("max", 0f); s.maxDistance = max > 0f ? max : float.PositiveInfinity;
                    j = s; break;
                }
                default: return null;
            }
            j.connectedBody = connected;
            j.enableCollision = e.GetInt("collide", 0) != 0;
            Breakable(j, e);
            // the joint lives on the body's object; return a marker so the level keeps a handle on the entity
            var marker = new GameObject(cls);
            marker.transform.position = anchor;
            marker.AddComponent<JointMarker>().joint = j;
            return marker;
        }

        /// <summary>Keeps the connector entity's handle on the joint it created (the component sits on the body).</summary>
        public sealed class JointMarker : MonoBehaviour
        {
            public Joint joint;
            protected override void OnDestroy() { if (joint != null && !joint.IsDestroyed) GameObject.Destroy(joint); }
        }

        // ------------------------------------------------------------------ shared key handling

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

        private static void ApplyHingeKeys(HingeJoint j, MapEntity e, string targetKey)
        {
            if (Limits(e, out float lo, out float hi)) { j.useLimits = true; j.limits = new JointLimits(lo, hi); }
            float motor = e.GetFloat("motor", 0f);
            if (motor != 0f) { j.useMotor = true; j.motor = new JointMotor(motor, Mathf.Max(e.GetFloat("motor_torque", 60f), 0f)); }
            float spring = e.GetFloat("spring", 0f);
            if (spring > 0f) { j.useSpring = true; j.spring = new JointSpring(spring, Mathf.Max(e.GetFloat("damper", 2f), 0f), e.GetFloat(targetKey, 0f)); }
        }

        private static void ApplySliderKeys(SliderJoint j, MapEntity e)
        {
            if (Limits(e, out float lo, out float hi)) { j.useLimits = true; j.limits = new JointLimits(lo, hi); }
            float motor = e.GetFloat("motor", 0f);
            if (motor != 0f) { j.useMotor = true; j.motor = new JointMotor(motor, Mathf.Max(e.GetFloat("motor_force", 300f), 0f)); }
            float spring = e.GetFloat("spring", 0f);
            if (spring > 0f) { j.useSpring = true; j.spring = new JointSpring(spring, Mathf.Max(e.GetFloat("damper", 10f), 0f), e.GetFloat("target", 0f)); }
        }

        private static void Breakable(Joint j, MapEntity e)
        {
            float f = e.GetFloat("break_force", 0f), t = e.GetFloat("break_torque", 0f);
            j.breakForce = f > 0f ? f : float.PositiveInfinity;
            j.breakTorque = t > 0f ? t : float.PositiveInfinity;
        }

        /// <summary>A thin bar from the hook to the hanging prop (visual only): a unit box scaled to the span, re-stretched every frame in the game.</summary>
        private static void Rod(GameObject prop, Vector3 from, Vector3 to, bool preview, bool thin = false)
        {
            var rod = new GameObject("Rod");
            rod.transform.SetParent(prop.transform, false);
            var mr = rod.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(new Vector3(0f, 0.5f, 0f), Vector3.one, 1f);
            mr.texture = Texture2D.White; mr.color = new Color(0.35f, 0.33f, 0.3f, 1f); mr.castsShadow = false;
            float w = thin ? 0.04f : 0.07f;
            RodFollower.Place(rod.transform, to, from, w);
            if (!preview) { var f = rod.AddComponent<RodFollower>(); f.prop = prop.transform; f.hook = from; f.width = w; }
        }

        /// <summary>Keeps the rod stretched from the hook to the prop every frame.</summary>
        public sealed class RodFollower : MonoBehaviour
        {
            public Transform prop; public Vector3 hook; public float width = 0.05f;

            public static void Place(Transform rod, Vector3 from, Vector3 to, float width)
            {
                Vector3 d = to - from;
                float len = d.magnitude;
                if (len < 0.01f) return;
                rod.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
                rod.position = from;
                rod.localScale = new Vector3(width, len, width);
            }

            protected override void LateUpdate()
            {
                if (prop == null) return;
                Place(transform, prop.position, hook, width);
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
