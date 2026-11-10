using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Environment/EnterWaterCollision.cs.
// RenderSettings.skybox swapping became toggling the engine's procedural sky.
public class EnterWaterCollision : MonoBehaviour, RipNRage.Game.Ecs.ITriggerListener
{
    private bool startingSky;
    public void OnTriggerEnter(GameObject other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        startingSky = RenderSettings.drawSky;
        
        RenderSettings.drawSky = false;
    }

    public void OnTriggerExit(GameObject other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        RenderSettings.drawSky = startingSky;
    }
}
