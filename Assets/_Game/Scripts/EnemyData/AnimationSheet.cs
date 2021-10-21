using System;
using UnityEngine;

[Serializable]
public struct AnimationSheet
{
    public int AnimationFrames;
    public int Sides;
    public int AnimationFramesPerSecond;
    public Texture TextureSheet;
}