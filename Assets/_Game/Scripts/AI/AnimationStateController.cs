using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationStateController : MonoBehaviour
{
    public AnimationState AnimationState;
    public SpriteRenderer SpriteRenderer;
    public EnemyAnimationData AnimationData;

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
        var sheet = new AnimationSheet();
        
        switch (AnimationState)
        {
            case AnimationState.IDLE:
                sheet = AnimationData.IdleAnimation;
                break;
            case AnimationState.MOVING:
                sheet = AnimationData.MoveAnimation;
                break;
            case AnimationState.ATTACKING:
                sheet = AnimationData.AttackAnimation;
                break;
            case AnimationState.DYING:
                sheet = AnimationData.DeathAnimation;
                ResetSheet();
                break;
            case AnimationState.DEAD:
                sheet = AnimationData.DeadAnimation;
                break;
        }

        SetAnimation(sheet);
    }

    private void SetAnimation(AnimationSheet animationSheet)
    {
        PropertyBlock.SetTexture(SpriteSheet, animationSheet.TextureSheet);
        PropertyBlock.SetFloat(AnimFrameCount, animationSheet.AnimationFrames);
        PropertyBlock.SetFloat(SideCount, animationSheet.Sides);
        PropertyBlock.SetFloat(AnimFPS, animationSheet.AnimationFramesPerSecond);
        
        SpriteRenderer.SetPropertyBlock(PropertyBlock);
    }

    private void ResetSheet()
    {
        PropertyBlock.SetFloat(AnimFrameCount, 1);
        PropertyBlock.SetFloat(AnimFPS, 1);
        
        SpriteRenderer.SetPropertyBlock(PropertyBlock);
    }
}
