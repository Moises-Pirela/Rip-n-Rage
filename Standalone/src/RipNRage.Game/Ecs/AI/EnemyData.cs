namespace RipNRage.Game.Ecs.AI
{
    /// <summary>One sheet of an enemy animation: frames × sides on one texture.</summary>
    public struct AnimationSheet
    {
        public int AnimationFrames;
        public int Sides;
        public int AnimationFramesPerSecond;
        public Texture2D TextureSheet;
    }

    public struct EnemyAnimationData
    {
        public AnimationSheet IdleAnimation;
        public AnimationSheet AttackAnimation;
        public AnimationSheet MoveAnimation;
        public AnimationSheet DeathAnimation;
        public AnimationSheet DeadAnimation;
    }

    /// <summary>
    /// An enemy kind's definition — shared immutable data in <see cref="Enemies.All"/>, the weapons / projectiles
    /// pattern (was a ScriptableObject asset; 6.6 made it a plain record). State lives in components, behaviour in
    /// systems; these are the numbers they read.
    /// </summary>
    public sealed class EnemyData
    {
        /// <summary>Keys the death VFX binding (vfx/bindings: deaths).</summary>
        public string name;
        /// <summary>The level file's classname for this kind (the save file records monsters by it).</summary>
        public string classname;

        // movement and engagement
        public float movementSpeed;
        public float attackRange;
        public float meleeAttackRange;
        public float turnSpeed;
        public float fireRate;

        // attack readability
        /// <summary>Seconds the enemy telegraphs (flash + aim glow + sound) before the first shot.</summary>
        public float attackWindup = 0.35f;
        /// <summary>Shots per attack.</summary>
        public int burstCount = 3;
        /// <summary>Seconds between the shots of a burst.</summary>
        public float burstInterval = 0.09f;
        /// <summary>Seconds a hit interrupts the enemy (stops moving, delays the shot).</summary>
        public float staggerTime = 0.25f;
        /// <summary>Seconds between spotting the player and the first reaction.</summary>
        public float alertDelay = 0.2f;

        // state tree (the values the behaviour tree's nodes carried)
        /// <summary>Seconds the enemy idles before picking the next wander point.</summary>
        public float wanderWait = 1f;
        /// <summary>Wander points are picked inside this radius ...</summary>
        public float wanderRadius = 15f;
        /// <summary>... and rejected when closer than this.</summary>
        public float wanderMinDistance = 10f;
        /// <summary>A move point counts as reached within this (the old agent's 5 m).</summary>
        public float arriveDistance = 5f;

        // senses
        /// <summary>Metres the enemy sees (the old vision trigger's sphere).</summary>
        public float sightRange = 10f;
        /// <summary>Full field of view, degrees, about the body's facing.</summary>
        public float sightFov = 180f;
        /// <summary>Seconds a target stays known after it was last seen or heard.</summary>
        public float memory = 5f;
        /// <summary>Metres the enemy's own shot carries to other ears.</summary>
        public float noise = 30f;
        /// <summary>Where the barrel is, in the enemy's own frame (turned by its Heading): the old ProjectileSpawnPostion child.</summary>
        public Vector3 muzzleOffset = new Vector3(0f, 0.225f, 0.45f);

        // status
        public float maxHealth;
        public float maxArmor;

        public EnemyAnimationData enemyAnimationData;

        /// <summary>skeletons/&lt;name&gt;.json used to slice the sprite into a ragdoll on death (empty = death animation only).</summary>
        public string skeleton = "";
    }
}
