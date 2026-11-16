using System;

namespace RipNRage.Engine.Ecs
{
    /// <summary>
    /// Hints for the editors' property grid (10.6), on the fields of configs and other authoring classes. None of
    /// them changes what a field means; they say how it is best edited. The particle editor's <c>Vfx*</c> attributes
    /// derive from these, so one grid reads both.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InspectRangeAttribute : Attribute
    {
        public readonly float min, max, step;
        public InspectRangeAttribute(float min, float max, float step = 0.01f) { this.min = min; this.max = max; this.step = step; }
    }

    /// <summary>One line shown as the field's tooltip.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InspectTipAttribute : Attribute
    {
        public readonly string text;
        public InspectTipAttribute(string text) { this.text = text; }
    }

    /// <summary>Starts a labelled section before this field.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InspectGroupAttribute : Attribute
    {
        public readonly string name;
        public InspectGroupAttribute(string name) { this.name = name; }
    }

    /// <summary>What a string field names, so the grid can offer the registry's names instead of a blank box.</summary>
    public enum AssetKind
    {
        Texture, Sprite, Material, Mesh, LevelTexture, Surface, Sound, Effect, Animator,
        /// <summary>An entity class (a prefab name / map classname).</summary>
        Classname,
        /// <summary>A prefab child or a level targetname (an <see cref="EntityRef"/>).</summary>
        Entity,
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class InspectAssetAttribute : Attribute
    {
        public readonly AssetKind kind;
        public InspectAssetAttribute(AssetKind kind) { this.kind = kind; }
    }

    /// <summary>A float[3] / float[4] that is a colour: edited with a colour picker.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InspectColorAttribute : Attribute { }

    /// <summary>Not shown in the grid (bookkeeping a human never edits).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InspectHiddenAttribute : Attribute { }
}
