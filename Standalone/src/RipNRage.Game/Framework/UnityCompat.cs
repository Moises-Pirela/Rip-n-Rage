// Tiny compatibility layer so the ported gameplay scripts keep their original shape.
// Nothing here references Unity — these are plain replacements built on RipNRage.Engine.
using System;
using RipNRage.Engine;

#region Inspector attributes (metadata only — kept so the scripts document their tuning fields)

[AttributeUsage(AttributeTargets.Field)] public sealed class SerializeFieldAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspectorAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class SpaceAttribute : Attribute { public SpaceAttribute() { } public SpaceAttribute(float height) { } }
[AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
[AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string menuName; public string fileName; }
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponentAttribute : Attribute { public RequireComponentAttribute(Type t) { } }

#endregion

/// <summary>
/// A factory that builds a GameObject hierarchy — what a Unity prefab asset was.
/// Prefabs are defined in code (see Content/) from the values stored in the .prefab files.
/// </summary>
public sealed class Prefab
{
    public readonly string name;
    private readonly Func<GameObject> _build;

    public Prefab(string name, Func<GameObject> build) { this.name = name; _build = build; }

    public GameObject Instantiate()
    {
        var go = _build();
        go.name = name;
        return go;
    }

    public GameObject Instantiate(Vector3 position, Quaternion rotation)
    {
        var go = Instantiate();
        go.transform.position = position;
        go.transform.rotation = rotation;
        return go;
    }

    public GameObject Instantiate(Transform parent)
    {
        var go = Instantiate();
        go.transform.SetParent(parent, false);
        return go;
    }
}

/// <summary>Replacement for UnityEngine.MonoBehaviour: engine Behaviour + the static helpers scripts call.</summary>
public abstract class MonoBehaviour : Behaviour
{
    protected static GameObject Instantiate(Prefab prefab) => prefab.Instantiate();
    protected static GameObject Instantiate(Prefab prefab, Transform parent) => prefab.Instantiate(parent);
    protected static GameObject Instantiate(Prefab prefab, Vector3 position, Quaternion rotation) => prefab.Instantiate(position, rotation);
    protected static T Instantiate<T>(T original) where T : ScriptableObject => (T)original.Clone();

    protected static void Destroy(EngineObject obj, float delay = 0f) => GameObject.Destroy(obj, delay);
    protected static void DestroyImmediate(EngineObject obj) => GameObject.DestroyImmediate(obj);
}

/// <summary>Replacement for UnityEngine.ScriptableObject: plain data assets defined in code.</summary>
public abstract class ScriptableObject : EngineObject
{
    /// <summary>Shallow copy (what Object.Instantiate does for a ScriptableObject).</summary>
    public virtual ScriptableObject Clone() => (ScriptableObject)MemberwiseClone();

    public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
}

/// <summary>UnityEngine.Events.UnityEvent stand-in.</summary>
public sealed class UnityEvent
{
    private Action _listeners;
    public void AddListener(Action a) => _listeners += a;
    public void RemoveListener(Action a) => _listeners -= a;
    public void Invoke() => _listeners?.Invoke();
}
