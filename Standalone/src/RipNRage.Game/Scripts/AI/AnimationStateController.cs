using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/AI/AnimationStateController.cs.
// The Unity version fed a MaterialPropertyBlock (_SpriteSheet/_AnimFrameCount/_SideCount/_AnimFPS)
// to the "8SidedSprite" shader graph. Here the same sheet parameters drive a DirectionalSpriteSheet
// on the CPU: the frame is picked from the animation time and the row from the angle between the
// enemy's facing and the camera, then assigned to the SpriteRenderer.
public class AnimationStateController : MonoBehaviour
{
    public AnimationState AnimationState;
    public SpriteRenderer SpriteRenderer;
    public EnemyAnimationData AnimationData;

    private readonly Dictionary<Texture2D, DirectionalSpriteSheet> _sheets = new Dictionary<Texture2D, DirectionalSpriteSheet>();
    private AnimationState _lastState;
    private float _stateTime;

    protected override void Awake()
    {
        // The renderer may live on a child (sprite holder); keep an explicit assignment if one was made.
        if (SpriteRenderer == null) SpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (SpriteRenderer != null) SpriteRenderer.billboard = true;
    }

    protected override void Update()
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
                break;
            case AnimationState.DEAD:
                sheet = AnimationData.DeadAnimation;
                break;
        }

        if (AnimationState != _lastState) { _lastState = AnimationState; _stateTime = 0f; }
        _stateTime += Time.deltaTime;

        SetAnimation(sheet);
    }

    private void SetAnimation(AnimationSheet animationSheet)
    {
        if (SpriteRenderer == null || animationSheet.TextureSheet == null) return;
        if (!_sheets.TryGetValue(animationSheet.TextureSheet, out var sheet))
        {
            sheet = new DirectionalSpriteSheet(animationSheet.TextureSheet, animationSheet.AnimationFrames, animationSheet.Sides, animationSheet.AnimationFramesPerSecond);
            _sheets[animationSheet.TextureSheet] = sheet;
        }
        bool loop = AnimationState == AnimationState.MOVING || AnimationState == AnimationState.IDLE || AnimationState == AnimationState.ATTACKING;
        int frame = sheet.FrameAt(_stateTime, loop);
        var cam = Camera.main;
        int side = cam != null ? sheet.SideFor(transform.position, transform.forward, cam.transform.position) : 0;
        SpriteRenderer.sprite = sheet.GetSprite(side, frame);
    }
}
