using System.Collections;
using System.Collections.Generic;

// Ported from Assets/Tools/EnragedInjector.cs (the `where T : Component` constraint became `class`).
public static class EnragedInjector<T> where T : class
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

    /// <summary>Clears all registrations (used when restarting a level).</summary>
    public static void Reset() => Instances.Clear();
}
