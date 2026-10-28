using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Environment/SkyboxRotator.cs.
public class SkyboxRotator : MonoBehaviour
{
    protected override void Update()
    {
        RenderSettings.skyboxRotation = Time.time * 1.2f;
    }
}
