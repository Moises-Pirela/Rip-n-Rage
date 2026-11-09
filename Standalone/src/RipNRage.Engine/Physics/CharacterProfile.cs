namespace RipNRage.Engine
{
    /// <summary>
    /// Tunables of a force-driven character body (tunables.cfg [character] / [enemy_character]): a capsule floating
    /// rideHeight above the ground on a spring, steered by accelerations. The ECS character body (layer 2.6) reads it.
    /// </summary>
    public sealed class CharacterProfile
    {
        public float rideHeight = 0.5f;
        public float rideFrequency = 6f;
        public float rideDamping = 0.8f;
        public float maxRideAcceleration = 90f;
        public float groundTolerance = 0.25f;
        public float maxSlopeAngle = 50f;
        public float acceleration = 70f;
        public float airAcceleration = 20f;
        public float stopDeceleration = 60f;
        public float jumpGrace = 0.25f;
        public float mass = 80f;

        public CharacterProfile Clone() => (CharacterProfile)MemberwiseClone();
        public void CopyFrom(CharacterProfile o)
        {
            rideHeight = o.rideHeight; rideFrequency = o.rideFrequency; rideDamping = o.rideDamping; maxRideAcceleration = o.maxRideAcceleration;
            groundTolerance = o.groundTolerance; maxSlopeAngle = o.maxSlopeAngle; acceleration = o.acceleration; airAcceleration = o.airAcceleration;
            stopDeceleration = o.stopDeceleration; jumpGrace = o.jumpGrace; mass = o.mass;
        }
    }

    public enum CharacterProfileKind { Custom, Player, Enemy }

    public static class CharacterProfiles
    {
        /// <summary>The tuned player / enemy profiles (PhysicsSettings fills them from tunables.cfg).</summary>
        public static readonly CharacterProfile Player = new CharacterProfile();
        public static readonly CharacterProfile Enemy = new CharacterProfile { rideHeight = 0.35f, acceleration = 40f, airAcceleration = 8f, stopDeceleration = 40f, mass = 85f };
    }
}
