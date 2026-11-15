// The inspector attributes the ported data classes still carry (metadata only — they document the tuning fields).
// 9.4: everything else that was here (MonoBehaviour, Prefab, ScriptableObject, UnityEvent) is gone with GameObject-land.
using System;

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
