using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RipNRage.Engine.Graphics;

namespace RipNRage.Engine.Animation
{
    /// <summary>
    /// Registry + file format for sprite animation clips and animator controllers.
    ///
    /// Layout on disk (<c>animations/</c> next to <c>levels/</c>):
    /// <code>
    /// animations/clips/GlockFire.json          { "name", "frameRate", "length", "loop", "sprites": [{"time","sprite"}], "enabled": [...], "events": [...] }
    /// animations/controllers/LeftFist.json     { "name", "parameters", "states": [...], "anyState": [...], "defaultState", "clips": {slot: clipName} }
    /// animations/controllers/Glock.json        { "name", "base": "BaseWeaponController", "overrides": {slot: clipName}, "preview": {...} }
    /// </code>
    /// Sprites are referenced through <see cref="SpriteLibrary"/> ("Atlas/name"). Loading updates the registered clip /
    /// controller objects <b>in place</b>, so everything holding a reference (weapon data, running animators) picks the
    /// new data up immediately — this is what makes hot reload and the animation editor's live preview work.
    /// </summary>
    public static class AnimationLibrary
    {
        public static readonly Dictionary<string, SpriteAnimationClip> Clips = new Dictionary<string, SpriteAnimationClip>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, AnimatorController> Controllers = new Dictionary<string, AnimatorController>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Folder being watched / saved to (null = code-built clips only).</summary>
        public static string Directory { get; private set; }
        /// <summary>Raised after files were (re)loaded.</summary>
        public static event Action Reloaded;

        private static readonly Dictionary<string, DateTime> _stamps = new Dictionary<string, DateTime>();
        private static double _nextPoll;
        private static bool _suppressPoll;

        public static string ClipsDir => Directory == null ? null : Path.Combine(Directory, "clips");
        public static string ControllersDir => Directory == null ? null : Path.Combine(Directory, "controllers");

        public static void Register(SpriteAnimationClip clip) { if (clip != null && clip.name != null) Clips[clip.name] = clip; }
        public static void Register(AnimatorController controller)
        {
            if (controller == null || controller.name == null) return;
            Controllers[controller.name] = controller;
            foreach (var c in controller.clips.Values) if (c != null && c.name != null && !Clips.ContainsKey(c.name)) Clips[c.name] = c;
        }

        public static SpriteAnimationClip GetClip(string name) => name != null && Clips.TryGetValue(name, out var c) ? c : null;
        public static AnimatorController GetController(string name) => name != null && Controllers.TryGetValue(name, out var c) ? c : null;

        /// <summary>Finds the animations folder: explicit path, RIPNRAGE_ANIMATIONS, or an "animations" directory up the tree.</summary>
        public static string Locate(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && System.IO.Directory.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_ANIMATIONS");
            if (!string.IsNullOrEmpty(env) && System.IO.Directory.Exists(env)) return env;
            foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "animations");
                    if (System.IO.Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ DTOs

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
        };

        public sealed class KeyDto { public float time; public string sprite; }
        public sealed class EnabledDto { public float time; public bool enabled; }
        public sealed class EventDto { public float time; public string function; }
        public sealed class ClipDto
        {
            public string name;
            public float frameRate = 60f;
            public float length;
            public bool loop;
            public List<KeyDto> sprites = new List<KeyDto>();
            public List<EnabledDto> enabled = new List<EnabledDto>();
            public List<EventDto> events = new List<EventDto>();
        }
        public sealed class ConditionDto { public string parameter; public string mode; public float threshold; }
        public sealed class TransitionDto
        {
            public string to;
            public bool hasExitTime;
            public float exitTime = 1f;
            public List<ConditionDto> conditions = new List<ConditionDto>();
        }
        public sealed class StateDto
        {
            public string name;
            public string clip;
            public float speed = 1f;
            public List<TransitionDto> transitions = new List<TransitionDto>();
        }
        public sealed class PreviewDto { public string layer = "FirstPerson"; public float[] position; public float[] scale; public bool billboard; }
        public sealed class ControllerDto
        {
            public string name;
            public string @base;
            public Dictionary<string, string> overrides;
            public Dictionary<string, string> parameters;
            public List<StateDto> states;
            public List<TransitionDto> anyState;
            public string defaultState;
            public Dictionary<string, string> clips;
            public PreviewDto preview;
        }

        // ------------------------------------------------------------------ conversion

        public static ClipDto ToDto(SpriteAnimationClip clip)
        {
            var d = new ClipDto { name = clip.name, frameRate = clip.frameRate, length = clip.length, loop = clip.loop };
            foreach (var (t, sp) in clip.spriteKeys) d.sprites.Add(new KeyDto { time = R(t), sprite = SpriteLibrary.ReferenceOf(sp) ?? sp?.name });
            foreach (var (t, en) in clip.enabledKeys) d.enabled.Add(new EnabledDto { time = R(t), enabled = en });
            foreach (var (t, fn) in clip.events) d.events.Add(new EventDto { time = R(t), function = fn });
            return d;
        }

        public static SpriteAnimationClip FromDto(ClipDto d, Action<string> warn = null)
        {
            var clip = new SpriteAnimationClip(d.name, d.length, d.loop) { frameRate = d.frameRate > 0f ? d.frameRate : 60f };
            foreach (var k in d.sprites ?? new List<KeyDto>())
            {
                var sp = SpriteLibrary.Resolve(k.sprite);
                if (sp == null) { warn?.Invoke($"clip {d.name}: unknown sprite '{k.sprite}'"); continue; }
                clip.AddFrame(k.time, sp);
            }
            foreach (var k in d.enabled ?? new List<EnabledDto>()) clip.AddEnabled(k.time, k.enabled);
            foreach (var k in d.events ?? new List<EventDto>()) if (!string.IsNullOrEmpty(k.function)) clip.AddEvent(k.time, k.function);
            clip.SortKeys();
            return clip;
        }

        public static ControllerDto ToDto(AnimatorController c)
        {
            var d = new ControllerDto { name = c.name };
            if (c.previewRig != null)
                d.preview = new PreviewDto
                {
                    layer = c.previewRig.layer.ToString(),
                    position = new[] { c.previewRig.position.x, c.previewRig.position.y, c.previewRig.position.z },
                    scale = new[] { c.previewRig.scale.x, c.previewRig.scale.y, c.previewRig.scale.z },
                    billboard = c.previewRig.billboard,
                };
            if (c.baseController != null)
            {
                d.@base = c.baseController.name;
                d.overrides = new Dictionary<string, string>();
                foreach (var kv in c.overrides) d.overrides[kv.Key] = kv.Value?.name;
                return d;
            }
            d.parameters = new Dictionary<string, string>();
            foreach (var kv in c.parameters) d.parameters[kv.Key] = kv.Value.ToString();
            d.states = new List<StateDto>();
            foreach (var s in c.states)
            {
                var sd = new StateDto { name = s.name, clip = s.clipName, speed = s.speed };
                foreach (var t in s.transitions) sd.transitions.Add(ToDto(t));
                d.states.Add(sd);
            }
            d.anyState = new List<TransitionDto>();
            foreach (var t in c.anyStateTransitions) d.anyState.Add(ToDto(t));
            d.defaultState = c.defaultState?.name;
            d.clips = new Dictionary<string, string>();
            foreach (var kv in c.clips) d.clips[kv.Key] = kv.Value?.name;
            return d;
        }

        private static TransitionDto ToDto(AnimatorTransition t)
        {
            var td = new TransitionDto { to = t.destination?.name, hasExitTime = t.hasExitTime, exitTime = t.exitTime };
            foreach (var c in t.conditions) td.conditions.Add(new ConditionDto { parameter = c.parameter, mode = c.mode.ToString(), threshold = c.threshold });
            return td;
        }

        /// <summary>Builds a controller from its DTO. Clip and base-controller names are resolved through the registry.</summary>
        public static AnimatorController FromDto(ControllerDto d, Action<string> warn = null)
        {
            var c = new AnimatorController(d.name);
            if (d.preview != null)
            {
                c.previewRig = new AnimationPreviewRig
                {
                    layer = Enum.TryParse<RenderLayer>(d.preview.layer, true, out var layer) ? layer : RenderLayer.FirstPerson,
                    billboard = d.preview.billboard,
                };
                if (d.preview.position != null && d.preview.position.Length == 3) c.previewRig.position = new Vector3(d.preview.position[0], d.preview.position[1], d.preview.position[2]);
                if (d.preview.scale != null && d.preview.scale.Length == 3) c.previewRig.scale = new Vector3(d.preview.scale[0], d.preview.scale[1], d.preview.scale[2]);
            }
            if (!string.IsNullOrEmpty(d.@base))
            {
                var baseCtrl = GetController(d.@base);
                if (baseCtrl == null) { warn?.Invoke($"controller {d.name}: unknown base controller '{d.@base}'"); return c; }
                var ov = new Dictionary<string, SpriteAnimationClip>();
                foreach (var kv in d.overrides ?? new Dictionary<string, string>())
                {
                    var clip = GetClip(kv.Value);
                    if (clip == null) { warn?.Invoke($"controller {d.name}: unknown clip '{kv.Value}'"); continue; }
                    ov[kv.Key] = clip;
                }
                var rig = c.previewRig;
                c.ApplyOverride(baseCtrl, ov);
                c.previewRig = rig;
                return c;
            }
            foreach (var kv in d.parameters ?? new Dictionary<string, string>())
                if (Enum.TryParse<AnimatorParameterType>(kv.Value, true, out var pt)) c.parameters[kv.Key] = pt;
            foreach (var sd in d.states ?? new List<StateDto>())
            {
                var s = c.AddState(sd.name, sd.clip ?? sd.name);
                s.speed = sd.speed;
            }
            foreach (var sd in d.states ?? new List<StateDto>())
            {
                var s = c.FindState(sd.name);
                foreach (var td in sd.transitions ?? new List<TransitionDto>())
                {
                    var dest = c.FindState(td.to);
                    if (dest == null) { warn?.Invoke($"controller {d.name}: transition to unknown state '{td.to}'"); continue; }
                    FillTransition(s.AddTransition(dest), td);
                }
            }
            foreach (var td in d.anyState ?? new List<TransitionDto>())
            {
                var dest = c.FindState(td.to);
                if (dest == null) { warn?.Invoke($"controller {d.name}: any-state transition to unknown state '{td.to}'"); continue; }
                FillTransition(c.AddAnyStateTransition(dest), td);
            }
            if (d.defaultState != null) c.defaultState = c.FindState(d.defaultState) ?? c.defaultState;
            foreach (var kv in d.clips ?? new Dictionary<string, string>())
            {
                var clip = GetClip(kv.Value);
                if (clip == null) { warn?.Invoke($"controller {d.name}: unknown clip '{kv.Value}' for slot {kv.Key}"); continue; }
                c.clips[kv.Key] = clip;
            }
            return c;
        }

        private static void FillTransition(AnimatorTransition t, TransitionDto td)
        {
            t.hasExitTime = td.hasExitTime; t.exitTime = td.exitTime;
            foreach (var cd in td.conditions ?? new List<ConditionDto>())
                if (Enum.TryParse<AnimatorConditionMode>(cd.mode, true, out var mode))
                    t.conditions.Add(new AnimatorCondition { parameter = cd.parameter, mode = mode, threshold = cd.threshold });
        }

        private static float R(float t) => MathF.Round(t, 6);

        // ------------------------------------------------------------------ files

        public static string ClipPath(string clipName) => Path.Combine(ClipsDir, Safe(clipName) + ".json");
        public static string ControllerPath(string ctrlName) => Path.Combine(ControllersDir, Safe(ctrlName) + ".json");
        private static string Safe(string n) { foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return n; }

        public static string ToJson(SpriteAnimationClip clip) => JsonSerializer.Serialize(ToDto(clip), JsonOpts);
        public static string ToJson(AnimatorController c) => JsonSerializer.Serialize(ToDto(c), JsonOpts);
        public static ClipDto ParseClip(string json) => JsonSerializer.Deserialize<ClipDto>(json, JsonOpts);
        public static ControllerDto ParseController(string json) => JsonSerializer.Deserialize<ControllerDto>(json, JsonOpts);

        /// <summary>Points the library at a folder (created on demand) without loading anything.</summary>
        public static void SetDirectory(string dir)
        {
            Directory = dir == null ? null : Path.GetFullPath(dir);
            _stamps.Clear();
        }

        /// <summary>Writes one clip; the file's timestamp is remembered so the watcher does not reload our own write.</summary>
        public static void SaveClip(SpriteAnimationClip clip)
        {
            if (Directory == null) throw new InvalidOperationException("AnimationLibrary.Directory not set");
            System.IO.Directory.CreateDirectory(ClipsDir);
            string path = ClipPath(clip.name);
            File.WriteAllText(path, ToJson(clip));
            _stamps[path] = File.GetLastWriteTimeUtc(path);
            Register(clip);
        }

        public static void SaveController(AnimatorController c)
        {
            if (Directory == null) throw new InvalidOperationException("AnimationLibrary.Directory not set");
            System.IO.Directory.CreateDirectory(ControllersDir);
            string path = ControllerPath(c.name);
            File.WriteAllText(path, ToJson(c));
            _stamps[path] = File.GetLastWriteTimeUtc(path);
            Register(c);
        }

        public static void DeleteClipFile(string clipName)
        {
            if (Directory == null) return;
            string path = ClipPath(clipName);
            if (File.Exists(path)) File.Delete(path);
            _stamps.Remove(path);
        }

        /// <summary>Writes every registered clip and controller (base controllers before overrides is irrelevant for writing).</summary>
        public static void SaveAll()
        {
            foreach (var c in Clips.Values) SaveClip(c);
            foreach (var c in Controllers.Values) SaveController(c);
        }

        /// <summary>
        /// Loads (or reloads) every file in the folder. Existing clips/controllers with the same name are updated in place,
        /// new ones are registered. Returns the number of files read.
        /// </summary>
        public static int LoadAll(bool quiet = false)
        {
            if (Directory == null) return 0;
            int n = 0;
            var warnings = new List<string>();
            void Warn(string w) => warnings.Add(w);
            if (System.IO.Directory.Exists(ClipsDir))
                foreach (var f in System.IO.Directory.GetFiles(ClipsDir, "*.json"))
                {
                    try { LoadClipFile(f, Warn); n++; }
                    catch (Exception e) { warnings.Add($"{Path.GetFileName(f)}: {e.Message}"); }
                }
            if (System.IO.Directory.Exists(ControllersDir))
            {
                // base controllers first so overrides can resolve them
                var files = new List<(string path, ControllerDto dto)>();
                foreach (var f in System.IO.Directory.GetFiles(ControllersDir, "*.json"))
                {
                    try { files.Add((f, ParseController(File.ReadAllText(f)))); }
                    catch (Exception e) { warnings.Add($"{Path.GetFileName(f)}: {e.Message}"); }
                }
                files.Sort((a, b) => (a.dto.@base != null).CompareTo(b.dto.@base != null));
                foreach (var (f, dto) in files)
                {
                    try { ApplyController(dto, Warn); _stamps[f] = File.GetLastWriteTimeUtc(f); n++; }
                    catch (Exception e) { warnings.Add($"{Path.GetFileName(f)}: {e.Message}"); }
                }
            }
            foreach (var w in warnings) Debug.LogWarning("animations: " + w);
            if (!quiet) Debug.LogWarning($"Animations: {Directory} ({n} files, hot reload on)");
            Reloaded?.Invoke();
            return n;
        }

        private static void LoadClipFile(string path, Action<string> warn)
        {
            var dto = ParseClip(File.ReadAllText(path));
            if (string.IsNullOrEmpty(dto.name)) dto.name = Path.GetFileNameWithoutExtension(path);
            var fresh = FromDto(dto, warn);
            if (Clips.TryGetValue(dto.name, out var existing)) existing.CopyFrom(fresh);
            else Clips[dto.name] = fresh;
            _stamps[path] = File.GetLastWriteTimeUtc(path);
        }

        private static void ApplyController(ControllerDto dto, Action<string> warn)
        {
            var fresh = FromDto(dto, warn);
            if (Controllers.TryGetValue(dto.name, out var existing)) existing.CopyFrom(fresh);
            else Controllers[dto.name] = fresh;
            RefreshOverrides(dto.name);
        }

        /// <summary>Override controllers share their base's state objects: call after a base controller's graph changed.</summary>
        public static void RefreshOverrides(string baseName)
        {
            foreach (var c in Controllers.Values)
                if (c.baseController != null && c.baseController.name == baseName)
                    c.ApplyOverride(c.baseController, new Dictionary<string, SpriteAnimationClip>(c.overrides));
        }

        /// <summary>Controllers using a clip (directly or through an override slot).</summary>
        public static IEnumerable<AnimatorController> ControllersUsing(SpriteAnimationClip clip)
        {
            foreach (var c in Controllers.Values)
                foreach (var v in c.clips.Values)
                    if (v == clip) { yield return c; break; }
        }

        /// <summary>The controller whose preview rig should be used for a clip: an override that swaps it in wins over its base.</summary>
        public static AnimatorController RigOwner(SpriteAnimationClip clip)
        {
            AnimatorController best = null;
            foreach (var c in Controllers.Values)
            {
                foreach (var v in c.overrides.Values) if (v == clip) return c;
                if (best == null) foreach (var v in c.clips.Values) if (v == clip) { best = c; break; }
            }
            return best;
        }

        /// <summary>Call once per frame: reloads changed/new files (polled twice a second).</summary>
        public static void Poll(double now)
        {
            if (Directory == null || _suppressPoll || now < _nextPoll) return;
            _nextPoll = now + 0.5;
            try
            {
                bool changed = false;
                foreach (var dir in new[] { ClipsDir, ControllersDir })
                {
                    if (!System.IO.Directory.Exists(dir)) continue;
                    foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
                    {
                        var stamp = File.GetLastWriteTimeUtc(f);
                        if (!_stamps.TryGetValue(f, out var seen) || seen != stamp) { changed = true; break; }
                    }
                    if (changed) break;
                }
                if (changed) { LoadAll(quiet: true); Debug.LogWarning("animations reloaded"); }
            }
            catch (IOException) { }
        }

        /// <summary>The editor owns the files: it disables the watcher while it is the writer.</summary>
        public static bool SuppressPolling { get => _suppressPoll; set => _suppressPoll = value; }
    }
}
