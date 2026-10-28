namespace RipNRage.Engine
{
    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction.normalized; }
        public Vector3 GetPoint(float distance) => origin + direction * distance;
        public override string ToString() => $"Ray(origin {origin}, dir {direction})";
    }
}
