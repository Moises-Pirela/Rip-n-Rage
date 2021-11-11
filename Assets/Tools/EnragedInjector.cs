using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.PerformanceData;
using UnityEngine;

public static class EnragedInjector<T> where T : Component
{
    private static List<T> Instances = new List<T>();

    public static T GetInstance()
    {
        foreach (var currentInstance in Instances)
        {
            if (currentInstance.GetType() == typeof(T))
                return currentInstance;
        }

        return null;
    }

    public static void SetInstance(T instance)
    {
        if (Instances.Contains(instance)) return;

        T newInstance = instance;
        
        Instances.Add(newInstance);
    }
}
