public enum AmmoType
{
    LIGHT,
    HEAVY,
    SHELL,
    ENERGY,
    EXPLOSIVE
}



public enum EntityHealthState
{
    ALIVE,
    DEAD
}

public enum AnimationState
{
    IDLE,
    MOVING,
    ATTACKING,
    DYING,
    DEAD
}

public enum PlayerRageState
{
    NORMAL = 0,
    ENRAGED = 1,
    DEPRESSED = 2
}

public enum PlayerCombatState
{
    PACIFIST,
    VIOLENT
}

public enum ManagerInitState
{
    PENDING,
    IN_PROGRESS,
    COMPLETE,
    ERROR
}
