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

    /// <summary>
    /// Drives a <see cref="SpriteRenderer"/> on the same GameObject from an <see cref="AnimatorController"/>.
    /// Supports bool / trigger / float parameters, condition transitions, exit-time transitions,
    /// animation events (dispatched via SendMessage like Unity) and sprite-enabled curves.
    /// </summary>
    public sealed class Animator : Behaviour
    {
        private AnimatorController _controller;
        private AnimatorState _state;
        private float _time;
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        private readonly Dictionary<string, bool> _bools = new Dictionary<string, bool>();
        private readonly HashSet<string> _triggers = new HashSet<string>();
        private SpriteRenderer _renderer;
        private bool _dirty = true;
        private int _controllerVersion;

        public float speed = 1f;

        public AnimatorController runtimeAnimatorController
        {
            get => _controller;
            set
            {
                if (_controller == value) return;
                _controller = value;
                _state = value?.defaultState;
                _controllerVersion = value?.version ?? 0;
                _time = 0f;
                _dirty = true;
                ApplyFrame(-1f, _time);
            }
        }

        public string currentStateName => _state?.name;
        /// <summary>Seconds into the current state's clip (editor/debug).</summary>
        public float currentTime => _time;
        public AnimatorState currentState => _state;
        public IReadOnlyDictionary<string, bool> Bools => _bools;
        public IReadOnlyDictionary<string, float> Floats => _floats;
        public bool IsTriggerSet(string pname) => _triggers.Contains(pname);

        protected override void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        // ---- Unity-style hashed parameter ids (Animator.StringToHash) ----
        private static readonly Dictionary<int, string> HashNames = new Dictionary<int, string>();

        public static int StringToHash(string pname)
        {
            int h = pname.GetHashCode();
            HashNames[h] = pname;
            return h;
        }

        private static string NameOf(int id) => HashNames.TryGetValue(id, out var n) ? n : id.ToString();

        public void SetBool(int id, bool value) => SetBool(NameOf(id), value);
        public bool GetBool(int id) => GetBool(NameOf(id));
        public void SetFloat(int id, float value) => SetFloat(NameOf(id), value);
        public float GetFloat(int id) => GetFloat(NameOf(id));
        public void SetTrigger(int id) => SetTrigger(NameOf(id));
        public void ResetTrigger(int id) => ResetTrigger(NameOf(id));

        public void SetBool(string pname, bool value) { _bools[pname] = value; }
        public bool GetBool(string pname) => _bools.TryGetValue(pname, out var v) && v;
        public void SetFloat(string pname, float value) { _floats[pname] = value; }
        public float GetFloat(string pname) => _floats.TryGetValue(pname, out var v) ? v : 0f;
        public void SetInteger(string pname, int value) { _floats[pname] = value; }
        public int GetInteger(string pname) => (int)GetFloat(pname);
        public void SetTrigger(string pname) { _triggers.Add(pname); }
        public void ResetTrigger(string pname) { _triggers.Remove(pname); }

        /// <summary>Forces a state (Animator.Play).</summary>
        public void Play(string stateName)
        {
            var s = _controller?.FindState(stateName);
            if (s == null) return;
            _state = s;
            _time = 0f;
            ApplyFrame(-1f, 0f);
        }

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer = 0)
        {
            var clip = CurrentClip;
            float len = clip?.length ?? 0f;
            return new AnimatorStateInfo
            {
                name = _state?.name,
                length = len,
                loop = clip?.loop ?? false,
                normalizedTime = len > 0f ? _time / len : 1f
            };
        }

        private SpriteAnimationClip CurrentClip => _state != null ? _controller?.GetClip(_state.clipName) : null;

        protected override void Update()
        {
            if (_controller == null || _state == null) return;
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_controller.version != _controllerVersion)
            {
                // the graph was rebuilt (animation hot reload): keep playing the state with the same name
                _controllerVersion = _controller.version;
                var again = _controller.FindState(_state.name) ?? _controller.defaultState;
                if (again != _state) { _state = again; _time = 0f; }
                if (_state == null) return;
            }

            // Transitions are evaluated first (Unity evaluates them at the start of the frame).
            if (TryTransition()) { ApplyFrame(-1f, _time); }

            var clip = CurrentClip;
            float prev = _time;
            float dt = Time.deltaTime * speed * _state.speed;
            _time += dt;
            if (clip != null)
            {
                if (clip.loop && clip.length > 0f)
                {
                    if (_time >= clip.length)
                    {
                        // fire events for the tail of the loop, then wrap
                        FireEvents(clip, prev, clip.length + 1E-04f);
                        _time = Mathf.Repeat(_time, clip.length);
                        prev = -1f;
                    }
                }
                else if (_time > clip.length) _time = clip.length;
            }
            ApplyFrame(prev, _time);
        }

        private bool TryTransition()
        {
            var clip = CurrentClip;
            float len = clip?.length ?? 0f;
            float normalized = len > 0f ? _time / len : 1f;
            foreach (var t in _controller.anyStateTransitions)
            {
                if (t.destination == _state) continue;
                if (t.hasExitTime && normalized < t.exitTime) continue;
                if (!ConditionsMet(t)) continue;
                ConsumeTriggers(t);
                _state = t.destination;
                _time = 0f;
                return true;
            }
            foreach (var t in _state.transitions)
            {
                if (t.hasExitTime && normalized < t.exitTime) continue;
                if (!ConditionsMet(t)) continue;
                ConsumeTriggers(t);
                _state = t.destination;
                _time = 0f;
                return true;
            }
            return false;
        }

        private void ConsumeTriggers(AnimatorTransition t)
        {
            foreach (var c in t.conditions)
                if (_controller.parameters.TryGetValue(c.parameter, out var type) && type == AnimatorParameterType.Trigger)
                    _triggers.Remove(c.parameter);
        }

        private bool ConditionsMet(AnimatorTransition t)
        {
            if (t.conditions.Count == 0) return t.hasExitTime;
            foreach (var c in t.conditions)
            {
                bool isTrigger = _controller.parameters.TryGetValue(c.parameter, out var type) && type == AnimatorParameterType.Trigger;
                switch (c.mode)
                {
                    case AnimatorConditionMode.If:
                        if (isTrigger ? !_triggers.Contains(c.parameter) : !GetBool(c.parameter)) return false;
                        break;
                    case AnimatorConditionMode.IfNot:
                        if (isTrigger ? _triggers.Contains(c.parameter) : GetBool(c.parameter)) return false;
                        break;
                    case AnimatorConditionMode.Greater: if (!(GetFloat(c.parameter) > c.threshold)) return false; break;
                    case AnimatorConditionMode.Less: if (!(GetFloat(c.parameter) < c.threshold)) return false; break;
                    case AnimatorConditionMode.Equals: if (GetFloat(c.parameter) != c.threshold) return false; break;
                    case AnimatorConditionMode.NotEqual: if (GetFloat(c.parameter) == c.threshold) return false; break;
                }
            }
            return true;
        }

        private void ApplyFrame(float prevTime, float time)
        {
            var clip = CurrentClip;
            if (clip == null) return;
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null)
            {
                var s = clip.SampleSprite(time);
                if (s != null) _renderer.sprite = s;
                var en = clip.SampleEnabled(time);
                if (en.HasValue) _renderer.enabled = en.Value;
            }
            FireEvents(clip, prevTime, time);
        }

        private void FireEvents(SpriteAnimationClip clip, float from, float to)
        {
            if (clip.events.Count == 0) return;
            foreach (var (t, fn) in clip.events)
                if (t > from && t <= to) gameObject.SendMessage(fn);
        }
    }
}
