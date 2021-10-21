using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationStateController : MonoBehaviour
{
    public AnimationState AnimationState;
    public SpriteRenderer SpriteRenderer;
    public EnemyData AnimationData;

    private MaterialPropertyBlock PropertyBlock;
    
    private static readonly int SpriteSheet = Shader.PropertyToID("_SpriteSheet");
    private static readonly int AnimFrameCount = Shader.PropertyToID("_AnimFrameCount");
    private static readonly int SideCount = Shader.PropertyToID("_SideCount");
    private static readonly int AnimFPS = Shader.PropertyToID("_AnimFPS");

    private void Awake()
    {
        SpriteRenderer = GetComponent<SpriteRenderer>();
        PropertyBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        switch (AnimationState)
        {
            case AnimationState.IDLE:
                SetAnimation(AnimationData.EnemyAnimationData.IdleAnimation);
                break;
            case AnimationState.MOVING:
                SetAnimation(AnimationData.EnemyAnimationData.MoveAnimation);
                break;
            case AnimationState.ATTACKING:
                SetAnimation(AnimationData.EnemyAnimationData.AttackAnimation);
                break;
            case AnimationState.DYING:
                SetAnimation(AnimationData.EnemyAnimationData.DeathAnimation);
                break;
        }
    }

    private void SetAnimation(AnimationSheet animationSheet)
    {
        PropertyBlock.SetTexture(SpriteSheet, animationSheet.TextureSheet);
        PropertyBlock.SetFloat(AnimFrameCount, animationSheet.AnimationFrames);
        PropertyBlock.SetFloat(SideCount, animationSheet.Sides);
        PropertyBlock.SetFloat(AnimFPS, animationSheet.AnimationFramesPerSecond);
        
        SpriteRenderer.SetPropertyBlock(PropertyBlock);
    }
}
