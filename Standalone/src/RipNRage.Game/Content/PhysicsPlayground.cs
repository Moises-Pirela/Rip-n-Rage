using RipNRage.Engine.Graphics;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// The legacy arena's joint playground (east of the start, beside the loose crates): a swing door that closes
    /// on a spring, a pendulum, a crate on a bungee spring, a seesaw, a motorised windmill, a piston that patrols
    /// its rail, a hanging chain and a breakable shelf — every joint type the engine has, built with the
    /// <see cref="Joint"/> components the level entities use too.
    /// </summary>
    public static class PhysicsPlayground
    {
        public static void Build(GameObject parent, float floor)
        {
            var root = new GameObject("---PHYSICS PLAYGROUND---");
            if (parent != null) root.transform.SetParent(parent.transform, false);
            float x0 = 8.5f;

            // 1. a swing door in a frame: push it open, it swings back shut (spring), shoot it, run through it
            {
                float z = -5f;
                Static(root, new Vector3(x0 - 0.9f, floor, z), new Vector3(0.3f, 2.6f, 0.3f), GameAssets.GreenProto);
                Static(root, new Vector3(x0 + 0.9f, floor, z), new Vector3(0.3f, 2.6f, 0.3f), GameAssets.GreenProto);
                Static(root, new Vector3(x0, floor + 2.6f, z), new Vector3(2.1f, 0.25f, 0.3f), GameAssets.GreenProto);
                var door = Prop(root, "SwingDoor", new Vector3(x0, floor + 1.2f, z), new Vector3(1.4f, 2.4f, 0.1f), 25f, GameAssets.OrangeProto);
                var hinge = door.AddComponent<HingeJoint>();
                hinge.anchor = new Vector3(-0.7f, 0f, 0f); hinge.axis = Vector3.up;
                hinge.useLimits = true; hinge.limits = new JointLimits(-100f, 100f);
                hinge.useSpring = true; hinge.spring = new JointSpring(40f, 12f, 0f);
            }

            // 2. a pendulum on a gallows: a 15 kg ball on a 2.8 m rod, started 45° out
            {
                float z = -9f;
                Static(root, new Vector3(x0 + 2.5f, floor, z), new Vector3(0.25f, 4.2f, 0.25f), GameAssets.PurpleProto);
                Static(root, new Vector3(x0 + 1.25f, floor + 4.2f, z), new Vector3(2.7f, 0.2f, 0.2f), GameAssets.PurpleProto);
                Vector3 pivot = new Vector3(x0, floor + 4.1f, z);
                Vector3 bobAt = pivot + Quaternion.AngleAxis(45f, Vector3.forward) * Vector3.down * 2.8f;
                var bob = Ball(root, "PendulumBob", bobAt, 0.35f, 15f, GameAssets.RedProto);
                var rod = bob.AddComponent<DistanceJoint>();
                rod.anchor = Vector3.zero; rod.autoConfigureConnectedAnchor = false; rod.connectedAnchor = pivot;
                Rod(bob, pivot, 0.06f);
            }

            // 3. a crate on a bungee spring (spring joint): hit it and it bobs
            {
                float z = -13f;
                Static(root, new Vector3(x0 + 2.5f, floor, z), new Vector3(0.25f, 4.2f, 0.25f), GameAssets.PurpleProto);
                Static(root, new Vector3(x0 + 1.25f, floor + 4.2f, z), new Vector3(2.7f, 0.2f, 0.2f), GameAssets.PurpleProto);
                Vector3 hook = new Vector3(x0, floor + 4.1f, z);
                var crate = Prop(root, "BungeeCrate", hook + Vector3.down * 2.2f, Vector3.one * 0.8f, 10f, GameAssets.GreenProto);
                var spring = crate.AddComponent<SpringJoint>();
                spring.anchor = new Vector3(0f, 0.4f, 0f); spring.autoConfigureConnectedAnchor = false; spring.connectedAnchor = hook;
                spring.spring = 120f; spring.damper = 4f; spring.maxDistance = 3.2f;
                Rod(crate, hook, 0.035f);
            }

            // 4. a seesaw: a plank hinged on a block with a crate on one end
            {
                float z = -17.5f;
                Static(root, new Vector3(x0, floor, z), new Vector3(0.5f, 0.6f, 0.5f), GameAssets.GreenProto);
                var plank = Prop(root, "Seesaw", new Vector3(x0, floor + 0.7f, z), new Vector3(4f, 0.15f, 0.8f), 20f, GameAssets.OrangeProto);
                var pivot = plank.AddComponent<HingeJoint>();
                pivot.anchor = Vector3.zero; pivot.axis = Vector3.forward;
                pivot.useLimits = true; pivot.limits = new JointLimits(-18f, 18f);
                Prop(root, "SeesawLoad", new Vector3(x0 - 1.5f, floor + 1.25f, z), Vector3.one * 0.7f, 8f, GameAssets.PurpleProto);
            }

            // 5. a windmill: four blades on a motorised hinge (a moving machine; the blades shove whatever they hit)
            {
                float z = -21.5f;
                Static(root, new Vector3(x0, floor, z + 0.6f), new Vector3(0.3f, 3.2f, 0.3f), GameAssets.PurpleProto);
                Vector3 hub = new Vector3(x0, floor + 3.2f, z);
                var blades = Prop(root, "Windmill", hub, new Vector3(3.6f, 0.3f, 0.15f), 12f, GameAssets.RedProto);
                var cross = new GameObject("Blade2"); cross.transform.SetParent(blades.transform, false);
                cross.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward);
                var cm = cross.AddComponent<MeshRenderer>(); cm.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(3.6f, 0.3f, 0.15f), 1f / 3.6f); cm.texture = GameAssets.RedProto; cm.material = Materials.Crate;
                var motor = blades.AddComponent<HingeJoint>();
                motor.anchor = Vector3.zero; motor.axis = Vector3.forward;
                motor.useMotor = true; motor.motor = new JointMotor(70f, 150f);
            }

            // 6. a piston on a rail that patrols between its limits (slider + motor, reversed at the ends)
            {
                float z = -25.5f;
                Static(root, new Vector3(x0, floor, z), new Vector3(5.2f, 0.2f, 0.4f), GameAssets.GreenProto);
                var piston = Prop(root, "Piston", new Vector3(x0, floor + 0.75f, z), new Vector3(1f, 1f, 1f), 15f, GameAssets.OrangeProto);
                var rail = piston.AddComponent<SliderJoint>();
                rail.anchor = Vector3.zero; rail.axis = Vector3.right;
                rail.useLimits = true; rail.limits = new JointLimits(-2f, 2f);
                rail.useMotor = true; rail.motor = new JointMotor(1.2f, 400f);
                piston.AddComponent<SliderPatrol>();
            }

            // 7. a hanging chain of four links (ball joints) under a beam — swings, tangles, can be shot apart (breakable)
            {
                float z = -29.5f;
                Static(root, new Vector3(x0 + 2.5f, floor, z), new Vector3(0.25f, 4.6f, 0.25f), GameAssets.PurpleProto);
                Static(root, new Vector3(x0 + 1.25f, floor + 4.6f, z), new Vector3(2.7f, 0.2f, 0.2f), GameAssets.PurpleProto);
                Vector3 top = new Vector3(x0, floor + 4.5f, z);
                RigidBody above = null;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = top + Vector3.down * (0.45f + i * 0.8f);
                    var link = Prop(root, "ChainLink" + i, c, new Vector3(0.25f, 0.7f, 0.25f), 3f, GameAssets.GreenProto);
                    var ball = link.AddComponent<BallJoint>();
                    ball.anchor = new Vector3(0f, 0.4f, 0f);
                    ball.connectedBody = above;
                    ball.breakForce = 900f;
                    above = link.GetComponent<RigidBody>();
                }
                var weight = Ball(root, "ChainWeight", top + Vector3.down * 3.9f, 0.4f, 12f, GameAssets.RedProto);
                var last = weight.AddComponent<BallJoint>();
                last.anchor = new Vector3(0f, 0.4f, 0f); last.connectedBody = above; last.breakForce = 900f;
            }

            // 8. a breakable shelf: a plank welded to a post (fixed joint) that a rocket, or enough weight, snaps off
            {
                float z = -33.5f;
                Static(root, new Vector3(x0 + 1.2f, floor, z), new Vector3(0.3f, 2.2f, 0.3f), GameAssets.PurpleProto);
                var shelf = Prop(root, "Shelf", new Vector3(x0 + 0.2f, floor + 1.8f, z), new Vector3(1.8f, 0.12f, 0.8f), 6f, GameAssets.OrangeProto);
                var weld = shelf.AddComponent<FixedJoint>();
                weld.anchor = new Vector3(0.9f, 0f, 0f);
                weld.breakForce = 260f; weld.breakTorque = 220f;
                Prop(root, "ShelfCrate", new Vector3(x0 - 0.2f, floor + 2.2f, z), Vector3.one * 0.55f, 5f, GameAssets.GreenProto);
            }
        }

        // ------------------------------------------------------------------ pieces

        private static GameObject Static(GameObject parent, Vector3 basePos, Vector3 size, Texture2D tex)
        {
            var go = new GameObject("Frame");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos + new Vector3(0f, size.y * 0.5f, 0f);
            go.layer = Layers.Walkable;
            go.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, 1f / Mathf.Max(size.x, size.y));
            mr.texture = tex; mr.material = Materials.Crate;
            var col = go.AddComponent<BoxCollider>(); col.size = size; col.isStatic = true;
            return go;
        }

        public static GameObject Prop(GameObject parent, string name, Vector3 center, Vector3 size, float mass, Texture2D tex)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.transform.position = center;
            go.layer = Layers.Default;
            go.AddComponent<SurfaceMaterial>().surface = Surfaces.Wood;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, 1f / Mathf.Max(size.x, 0.01f));
            mr.texture = tex; mr.material = Materials.Crate;
            var col = go.AddComponent<BoxCollider>(); col.size = size;
            var rb = go.AddComponent<RigidBody>();
            rb.mass = mass; rb.friction = 0.55f; rb.restitution = 0.05f;
            rb.collisionDetection = CollisionDetectionMode.Continuous;
            return go;
        }

        public static GameObject Ball(GameObject parent, string name, Vector3 center, float radius, float mass, Texture2D tex)
        {
            var go = TestArea.PhysicsBall(parent, center, radius, mass, tex);
            go.name = name;
            return go;
        }

        /// <summary>A thin bar drawn from the hook to the body, stretched every frame.</summary>
        private static void Rod(GameObject body, Vector3 hook, float width)
        {
            var rod = new GameObject("Rod");
            rod.transform.SetParent(body.transform, false);
            var mr = rod.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(new Vector3(0f, 0.5f, 0f), Vector3.one, 1f);
            mr.texture = Texture2D.White; mr.color = new Color(0.35f, 0.33f, 0.3f, 1f); mr.castsShadow = false;
            var f = rod.AddComponent<Level.PhysicsEntities.RodFollower>(); f.prop = body.transform; f.hook = hook; f.width = width;
            Level.PhysicsEntities.RodFollower.Place(rod.transform, body.transform.position, hook, width);
        }
    }

    /// <summary>Reverses a <see cref="SliderJoint"/>'s motor whenever the piston reaches a limit: a platform that patrols its rail.</summary>
    public sealed class SliderPatrol : MonoBehaviour
    {
        /// <summary>How close to a limit (m) counts as arrived.</summary>
        public float margin = 0.05f;
        /// <summary>Seconds to wait at each end.</summary>
        public float dwell = 0.4f;
        private SliderJoint _slider;
        private float _wait;

        protected override void FixedUpdate()
        {
            _slider ??= GetComponent<SliderJoint>();
            if (_slider == null || !_slider.useLimits || !_slider.useMotor) return;
            if (_wait > 0f) { _wait -= Time.fixedDeltaTime; return; }
            float p = _slider.position, v = _slider.motor.targetVelocity;
            if ((v > 0f && p >= _slider.limits.max - margin) || (v < 0f && p <= _slider.limits.min + margin))
            {
                var m = _slider.motor; m.targetVelocity = -v; _slider.motor = m;
                _slider.body?.WakeUp();
                _wait = dwell;
            }
        }
    }
}
