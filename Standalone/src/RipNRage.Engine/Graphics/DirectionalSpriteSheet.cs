namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Grid sprite sheet used for 2.5D enemies: columns are animation frames, rows are view
    /// directions (row 0 = front/facing the camera, then counter-clockwise). Replaces the Unity
    /// "8SidedSprite" shader graph + MaterialPropertyBlock driven by AnimationStateController.
    /// </summary>
    public sealed class DirectionalSpriteSheet
    {
        public Texture2D texture;
        public int frames = 1;
        public int sides = 1;
        public float fps = 1f;
        public float pixelsPerUnit = 100f;
        private readonly Sprite[,] _cache;

        public DirectionalSpriteSheet(Texture2D texture, int frames, int sides, float fps, float pixelsPerUnit = 100f)
        {
            this.texture = texture; this.frames = Mathf.Max(1, frames); this.sides = Mathf.Max(1, sides); this.fps = fps; this.pixelsPerUnit = pixelsPerUnit;
            _cache = new Sprite[this.sides, this.frames];
        }

        public float Length => frames / Mathf.Max(fps, 0.0001f);

        public Sprite GetSprite(int side, int frame)
        {
            side = ((side % sides) + sides) % sides;
            frame = Mathf.Clamp(frame, 0, frames - 1);
            if (_cache[side, frame] == null)
            {
                float cw = (float)texture.width / frames, ch = (float)texture.height / sides;
                // row 0 is the top row of the image; Rect uses a bottom-left origin.
                var rect = new Rect(frame * cw, texture.height - (side + 1) * ch, cw, ch);
                _cache[side, frame] = new Sprite(texture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit, $"{texture.name}[{side},{frame}]");
            }
            return _cache[side, frame];
        }

        /// <summary>Frame for a given animation time (looping when <paramref name="loop"/>).</summary>
        public int FrameAt(float time, bool loop)
        {
            int f = (int)(time * fps);
            return loop ? ((f % frames) + frames) % frames : Mathf.Clamp(f, 0, frames - 1);
        }

        /// <summary>
        /// Picks the row for a sprite facing <paramref name="facing"/> (world forward of the entity)
        /// seen from <paramref name="viewer"/>. Sides are assumed evenly distributed over 360 degrees.
        /// </summary>
        public int SideFor(Vector3 entityPosition, Vector3 facing, Vector3 viewer)
        {
            if (sides <= 1) return 0;
            Vector3 toViewer = viewer - entityPosition; toViewer.y = 0f;
            facing.y = 0f;
            if (toViewer.sqrMagnitude < 1E-06f || facing.sqrMagnitude < 1E-06f) return 0;
            float angle = Vector3.SignedAngle(facing.normalized, toViewer.normalized, Vector3.up); // -180..180
            float step = 360f / sides;
            int side = Mathf.RoundToInt(angle / step);
            return ((side % sides) + sides) % sides;
        }
    }
}
