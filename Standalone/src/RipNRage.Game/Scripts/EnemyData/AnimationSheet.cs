using System;

[Serializable]
public struct AnimationSheet
{
    public int AnimationFrames;
    public int Sides;
    public int AnimationFramesPerSecond;
    public Texture2D TextureSheet;
}