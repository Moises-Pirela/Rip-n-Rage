using System;
using System.Collections;

// Ported from Assets/_Game/Scripts/PlayerSystems/PlayerState.cs.
// Standalone additions: damage is ignored once dead (the death itself is PlayerDeathSystem's since 8.3;
// the save reads and writes the components directly since 8.5), Init() keeps the maximum health/armour fixed at 100 (the original used
// the saved value as the maximum, so healing after a checkpoint load capped at the saved health),
// Current returns null once the player object was destroyed (level restart), and the per-frame
// Debug.Log calls in Update were removed (they built two strings every frame).
[System.Serializable]
public class PlayerState : EntityState
{
    public const float MaxHealthValue = 100f;
    public const float MaxArmorValue = 100f;

    private static PlayerState _current;
    public static PlayerState Current {
        get
        {
            return _current != null && !_current.IsDestroyed ? _current : null;
        }
    }

    public bool IsDead => HealthState == EntityHealthState.DEAD;

    protected override void Awake()
    {
        _current = this;
    }

    protected override void Start() => Init();

    public override void Init()
    {
        // the Health component sits on the character entity PlayerLink built; this facade reads and writes it
        Bind(RipNRage.Game.Ecs.PlayerLink.PlayerEntity);
        // the new game's values, fixed maximums; a load overwrites the components afterwards (8.5)
        InitializeArmor(MaxArmorValue, 1f); // the player's armour takes a hit one for one
        InitializeHealth(MaxHealthValue);
        HealthState = EntityHealthState.ALIVE;
    }

    internal override void NotifyHit(in RipNRage.Game.Ecs.Combat.HitEvent e)
    {
        base.NotifyHit(in e);
        if (!IsDead) RipNRage.Game.Ecs.PlayerCamera.Kick(0.5f);
    }

}
