using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnterWaterCollision : MonoBehaviour
{
    private Material startingSkybox;
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        startingSkybox = new Material(RenderSettings.skybox);
        
        RenderSettings.skybox = null;
        DynamicGI.UpdateEnvironment();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;
        
        RenderSettings.skybox = startingSkybox;
        DynamicGI.UpdateEnvironment();
    }
}
