using System;

[Serializable]
public struct EnemyAnimationData
{
    public AnimationSheet IdleAnimation;
    public AnimationSheet AttackAnimation;
    public AnimationSheet MoveAnimation;
    public AnimationSheet DeathAnimation;
    public AnimationSheet DeadAnimation;
}