using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Animation
{
    /// <summary>
    /// Sprite-swap animation clip: the data Unity stores in an AnimationClip with m_Sprite /
    /// m_Enabled PPtr curves plus AnimationEvents (which is exactly what the weapon clips contain).
    /// </summary>
    public sealed class SpriteAnimationClip
    {
        public string name;
        public float length;
        public bool loop;
        /// <summary>Authoring frame rate (keys snap to 1/frameRate in the animation editor); the clips shipped with the game use 60.</summary>
        public float frameRate = 60f;
        public readonly List<(float time, Sprite sprite)> spriteKeys = new List<(float, Sprite)>();
        public readonly List<(float time, bool enabled)> enabledKeys = new List<(float, bool)>();
        public readonly List<(float time, string function)> events = new List<(float, string)>();

        public SpriteAnimationClip(string name, float length, bool loop = false)
        {
            this.name = name;
            this.length = length;
            this.loop = loop;
        }

        public SpriteAnimationClip AddFrame(float time, Sprite sprite) { spriteKeys.Add((time, sprite)); return this; }
        public SpriteAnimationClip AddEnabled(float time, bool enabled) { enabledKeys.Add((time, enabled)); return this; }
        public SpriteAnimationClip AddEvent(float time, string function) { events.Add((time, function)); return this; }

        /// <summary>Convenience: evenly spaced frames at <paramref name="fps"/>.</summary>
        public static SpriteAnimationClip FromFrames(string name, float fps, bool loop, params Sprite[] frames)
        {
            var clip = new SpriteAnimationClip(name, frames.Length / fps, loop) { frameRate = fps };
            for (int i = 0; i < frames.Length; i++) clip.AddFrame(i / fps, frames[i]);
            return clip;
        }

        /// <summary>Keeps the key lists ordered by time (the samplers rely on it).</summary>
        public void SortKeys()
        {
            spriteKeys.Sort((a, b) => a.time.CompareTo(b.time));
            enabledKeys.Sort((a, b) => a.time.CompareTo(b.time));
            events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        /// <summary>Replaces this clip's content with another clip's (used by hot reload so references stay valid).</summary>
        public void CopyFrom(SpriteAnimationClip other)
        {
            length = other.length; loop = other.loop; frameRate = other.frameRate;
            spriteKeys.Clear(); spriteKeys.AddRange(other.spriteKeys);
            enabledKeys.Clear(); enabledKeys.AddRange(other.enabledKeys);
            events.Clear(); events.AddRange(other.events);
        }

        public Sprite SampleSprite(float t)
        {
            Sprite result = null;
            foreach (var (time, sprite) in spriteKeys)
            {
                if (time <= t + 1E-05f) result = sprite; else break;
            }
            return result ?? (spriteKeys.Count > 0 ? spriteKeys[0].sprite : null);
        }

        public bool? SampleEnabled(float t)
        {
            bool? result = null;
            foreach (var (time, enabled) in enabledKeys)
            {
                if (time <= t + 1E-05f) result = enabled; else break;
            }
            return result ?? (enabledKeys.Count > 0 ? enabledKeys[0].enabled : (bool?)null);
        }
    }

    public enum AnimatorConditionMode { If = 1, IfNot = 2, Greater = 3, Less = 4, Equals = 6, NotEqual = 7 }
    public enum AnimatorParameterType { Float = 1, Int = 3, Bool = 4, Trigger = 9 }

    public sealed class AnimatorCondition
    {
        public string parameter;
        public AnimatorConditionMode mode;
        public float threshold;
    }

    public sealed class AnimatorTransition
    {
        public AnimatorState destination;
        public readonly List<AnimatorCondition> conditions = new List<AnimatorCondition>();
        public bool hasExitTime;
        public float exitTime = 1f;

        public AnimatorTransition When(string param, AnimatorConditionMode mode, float threshold = 0f)
        {
            conditions.Add(new AnimatorCondition { parameter = param, mode = mode, threshold = threshold });
            return this;
        }
    }

    public sealed class AnimatorState
    {
        public string name;
        /// <summary>Name of the clip slot; the actual clip is resolved through the controller's override table.</summary>
        public string clipName;
        public float speed = 1f;
        public readonly List<AnimatorTransition> transitions = new List<AnimatorTransition>();

        public AnimatorTransition AddTransition(AnimatorState to)
        {
            var t = new AnimatorTransition { destination = to };
            transitions.Add(t);
            return t;
        }
    }

    /// <summary>
    /// State graph + clip table. Equivalent of RuntimeAnimatorController; <see cref="CreateOverride"/>
    /// mirrors AnimatorOverrideController (same graph, some clips swapped).
    /// </summary>
    public sealed class AnimatorController
    {
        public string name;
        public readonly List<AnimatorState> states = new List<AnimatorState>();
        /// <summary>Transitions evaluated from every state (Unity's "Any State"). Never re-enter the current state.</summary>
        public readonly List<AnimatorTransition> anyStateTransitions = new List<AnimatorTransition>();
        public AnimatorState defaultState;
        public readonly Dictionary<string, AnimatorParameterType> parameters = new Dictionary<string, AnimatorParameterType>();
        public readonly Dictionary<string, SpriteAnimationClip> clips = new Dictionary<string, SpriteAnimationClip>();
        /// <summary>Set on controllers made by <see cref="CreateOverride"/>: the graph comes from this controller.</summary>
        public AnimatorController baseController;
        /// <summary>Clip slot → clip for override controllers (the part that differs from the base).</summary>
        public readonly Dictionary<string, SpriteAnimationClip> overrides = new Dictionary<string, SpriteAnimationClip>();
        /// <summary>Incremented whenever the graph is rebuilt (hot reload); running Animators re-resolve their state by name.</summary>
        public int version;
        /// <summary>Optional preview rig hint for the animation editor (layer, local position/scale, billboard).</summary>
        public AnimationPreviewRig previewRig;

        public AnimatorController(string name) { this.name = name; }

        /// <summary>Rebuilds this controller as an override of <paramref name="baseCtrl"/> (in place, keeps the object identity).</summary>
        public void ApplyOverride(AnimatorController baseCtrl, IDictionary<string, SpriteAnimationClip> clipOverrides)
        {
            baseController = baseCtrl;
            states.Clear(); states.AddRange(baseCtrl.states);
            anyStateTransitions.Clear(); anyStateTransitions.AddRange(baseCtrl.anyStateTransitions);
            defaultState = baseCtrl.defaultState;
            parameters.Clear(); foreach (var kv in baseCtrl.parameters) parameters[kv.Key] = kv.Value;
            clips.Clear(); foreach (var kv in baseCtrl.clips) clips[kv.Key] = kv.Value;
            overrides.Clear();
            foreach (var kv in clipOverrides) { overrides[kv.Key] = kv.Value; clips[kv.Key] = kv.Value; }
            version++;
        }

        /// <summary>Replaces the whole graph with another controller's (hot reload).</summary>
        public void CopyFrom(AnimatorController other)
        {
            baseController = other.baseController;
            states.Clear(); states.AddRange(other.states);
            anyStateTransitions.Clear(); anyStateTransitions.AddRange(other.anyStateTransitions);
            defaultState = other.defaultState;
            parameters.Clear(); foreach (var kv in other.parameters) parameters[kv.Key] = kv.Value;
            clips.Clear(); foreach (var kv in other.clips) clips[kv.Key] = kv.Value;
            overrides.Clear(); foreach (var kv in other.overrides) overrides[kv.Key] = kv.Value;
            previewRig = other.previewRig ?? previewRig;
            version++;
        }

        public AnimatorState AddState(string stateName, string clipName = null)
        {
            var s = new AnimatorState { name = stateName, clipName = clipName ?? stateName };
            states.Add(s);
            if (defaultState == null) defaultState = s;
            return s;
        }

        public AnimatorState FindState(string stateName) => states.Find(s => s.name == stateName);

        public AnimatorTransition AddAnyStateTransition(AnimatorState to)
        {
            var t = new AnimatorTransition { destination = to };
            anyStateTransitions.Add(t);
            return t;
        }

        public AnimatorController AddParameter(string pname, AnimatorParameterType type) { parameters[pname] = type; return this; }

        public SpriteAnimationClip GetClip(string clipName) => clipName != null && clips.TryGetValue(clipName, out var c) ? c : null;

        public AnimatorController CreateOverride(string overrideName, IDictionary<string, SpriteAnimationClip> overrides)
        {
            var c = new AnimatorController(overrideName);
            c.ApplyOverride(this, overrides);
            return c;
        }
    }

    /// <summary>Where the animation editor places a preview sprite for a controller (mirrors the game's prefab hierarchy).</summary>
    public sealed class AnimationPreviewRig
    {
        public RenderLayer layer = RenderLayer.FirstPerson;
        public Vector3 position = new Vector3(0.017f, -0.65f, 2.36f);
        public Vector3 scale = new Vector3(0.1f, 0.1f, 0.1f);
        public bool billboard;
    }

    public struct AnimatorStateInfo
    {
        public string name;
        public float normalizedTime;
        public float length;
        public bool loop;
        public bool IsName(string n) => name == n;
    }
}
