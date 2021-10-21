public enum ExcecutionState
{
    NONE,
    RUNNING,
    SUCCESS,
    FAILED
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
    DYING
}

public enum PlayerRageState
{
    NONE,
    NORMAL,
    ENRAGED,
    DEPRESSED
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
