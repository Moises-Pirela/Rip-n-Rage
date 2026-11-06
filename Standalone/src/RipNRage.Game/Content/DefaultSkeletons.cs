using System.Collections.Generic;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Code-built destruction skeletons, written to skeletons/ on first run as the starting point for the skeleton
    /// editor. Coordinates are normalised inside a sheet cell (bottom-left origin).
    /// </summary>
    public static class DefaultSkeletons
    {
        private static SkeletonPart Part(string name, string parent, float pivotX, float pivotY, float mass, PartShape shape, float breakImpulse, params float[] xy)
        {
            var p = new SkeletonPart { name = name, parent = parent, pivot = new Vector2(pivotX, pivotY), mass = mass, shape = shape, breakImpulse = breakImpulse };
            for (int i = 0; i + 1 < xy.Length; i += 2) p.polygon.Add(new Vector2(xy[i], xy[i + 1]));
            return p;
        }

        /// <summary>The AR grunt (512 px cells, 4 sides): head, torso, two arms (with the rifle), two legs.</summary>
        public static SpriteSkeleton Rifleman()
        {
            var s = new SpriteSkeleton
            {
                name = "rifleman", texture = "Sprites/Enemies/Rifleman/Rifleman_Idle.png", frames = 1, sides = 4,
                pixelsPerUnit = 100f, depth = 0.18f, lifetime = 25f,
            };
            var front = new SkeletonView { name = "front", side = 0, frame = 0 };
            front.parts.Add(Part("torso", "", 0.48f, 0.42f, 14f, PartShape.Box, 0f, 0.30f, 0.41f, 0.66f, 0.41f, 0.68f, 0.70f, 0.60f, 0.79f, 0.37f, 0.79f, 0.28f, 0.70f));
            front.parts.Add(Part("head", "torso", 0.49f, 0.77f, 4f, PartShape.Box, 30f, 0.37f, 0.75f, 0.61f, 0.75f, 0.63f, 0.92f, 0.56f, 0.995f, 0.42f, 0.995f, 0.36f, 0.92f));
            front.parts.Add(Part("arm_left", "torso", 0.33f, 0.70f, 4f, PartShape.Capsule, 45f, 0.22f, 0.44f, 0.49f, 0.44f, 0.49f, 0.72f, 0.22f, 0.72f));
            front.parts.Add(Part("arm_right", "torso", 0.64f, 0.70f, 4f, PartShape.Capsule, 45f, 0.49f, 0.42f, 0.74f, 0.42f, 0.74f, 0.70f, 0.49f, 0.70f));
            front.parts.Add(Part("leg_left", "torso", 0.42f, 0.42f, 6f, PartShape.Capsule, 60f, 0.33f, 0.04f, 0.49f, 0.04f, 0.49f, 0.43f, 0.33f, 0.43f));
            front.parts.Add(Part("leg_right", "torso", 0.56f, 0.42f, 6f, PartShape.Capsule, 60f, 0.49f, 0.03f, 0.66f, 0.03f, 0.66f, 0.43f, 0.49f, 0.43f));
            s.views.Add(front);
            // the other sides reuse the front layout (mirrored for the back is close enough for a 2.5D sprite)
            for (int side = 1; side < 4; side++)
            {
                var v = new SkeletonView { name = side == 2 ? "back" : side == 1 ? "left" : "right", side = side, frame = 0 };
                foreach (var p in front.parts)
                {
                    var c = new SkeletonPart { name = p.name, parent = p.parent, pivot = p.pivot, mass = p.mass, shape = p.shape, breakImpulse = p.breakImpulse, loose = p.loose };
                    c.polygon.AddRange(p.polygon);
                    v.parts.Add(c);
                }
                s.views.Add(v);
            }
            return s;
        }
    }
}
