using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Environment/EnterWaterCollision.cs.
// RenderSettings.skybox swapping became toggling the engine's procedural sky.
public class EnterWaterCollision : MonoBehaviour, ITriggerListener
{
    private bool startingSky;
    public void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        startingSky = RenderSettings.drawSky;
        
        RenderSettings.drawSky = false;
    }

    public void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        RenderSettings.drawSky = startingSky;
    }
}
