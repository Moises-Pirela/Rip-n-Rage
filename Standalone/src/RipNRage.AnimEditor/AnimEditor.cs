using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Graphics;

namespace RipNRage.AnimEditor
{
    /// <summary>
    /// Sprite animation editor: clip timeline (sprite keys, enabled keys, events), sprite palette, animator
    /// controller editing and a live preview rendered by the real <see cref="RenderPipeline"/> (viewmodel pass,
    /// lighting, post-fx) through the same <see cref="Animator"/> the game uses.
    ///
    /// Data lives in <see cref="AnimationLibrary"/> (animations/*.json); everything is edited in place so a running
    /// game that watches the folder picks up saved changes immediately.
    /// </summary>
    public sealed partial class AnimEditor : Behaviour, IUiDrawer
    {
        // ---- scene rig (set up by Program) ----
        public Camera cam;
        public Transform arms;          // = PlayerArms node of the player prefab (child of the camera)
        public Transform worldAnchor;   // a few metres in front of the camera for world-layer clips
        private GameObject _previewGo;
        private SpriteRenderer _previewSr, _ghostPrev, _ghostNext;
        private Animator _previewAnim;

        // ---- document ----
        private SpriteAnimationClip _clip;
        private AnimatorController _controller;
        private readonly List<string> _clipNames = new List<string>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly HashSet<string> _dirtyClips = new HashSet<string>();
        private readonly HashSet<string> _dirtyControllers = new HashSet<string>();
        private readonly List<string> _deletedClips = new List<string>();
        public bool Dirty => _dirtyClips.Count > 0 || _dirtyControllers.Count > 0 || _deletedClips.Count > 0;

        // ---- playback ----
        private float _time;
        private bool _playing;
        private bool _controllerMode;
        private bool _onion = true;
        private float _previewSpeed = 1f;
        private readonly List<(string text, float until)> _flashes = new List<(string, float)>();

        // ---- selection / interaction ----
        private enum KeyKind { None, Sprite, Enabled, Event }
        private KeyKind _selKind = KeyKind.None;
        private int _selIndex = -1;
        private KeyKind _dragKind = KeyKind.None;
        private int _dragIndex = -1;
        private bool _dragPlayhead, _dragEnd, _dragMoved;
        private int _tlScroll;                 // first visible frame
        private float _clipScroll, _ctrlScroll, _paletteScroll;
        private int _atlasIndex;
        private int _leftTab;                   // 0 = clips, 1 = controller
        private int _selState = -1, _selTransition = -1; // controller panel selection (state index, transition index; -1 = any-state list)
        private readonly ImUi _ui = new ImUi();
        private readonly TextPrompt _prompt = new TextPrompt();
        private bool _showHelp;
        /// <summary>Raises the viewmodel rig so the weapon sits in the middle of the viewport instead of under the timeline (G toggles the true game framing).</summary>
        private bool _fitView = true;
        private Vector3 _armsHome;
        private bool _layoutKnown;
        private string _status = "";
        private float _statusUntil;

        // ---- undo ----
        private sealed class UndoEntry { public bool isClip; public string name; public string json; }
        private Stack<UndoEntry> _undo = new Stack<UndoEntry>();
        private readonly Stack<UndoEntry> _redo = new Stack<UndoEntry>();

        public SpriteAnimationClip Clip => _clip;
        public AnimatorController Controller => _controller;
        public float Time01 => _clip != null && _clip.length > 0f ? _time / _clip.length : 0f;
        public int CurrentFrame => _clip == null ? 0 : Mathf.RoundToInt(_time * Fps);
        private float Fps => _clip != null && _clip.frameRate > 0f ? _clip.frameRate : 60f;
        private int Frames => _clip == null ? 0 : Math.Max(0, Mathf.RoundToInt(_clip.length * Fps));
        public bool ControllerMode => _controllerMode;
        public bool Playing => _playing;

        // ------------------------------------------------------------------ lifecycle

        protected override void Start()
        {
            _armsHome = arms.localPosition;
            RefreshLists();
            BuildPreviewObject();
            if (_clipNames.Count > 0) SelectClip(_clipNames[0]);
            AnimationLibrary.SuppressPolling = true; // the editor is the writer
            AnimationLibrary.Reloaded += RefreshLists;
        }

        protected override void OnDestroy()
        {
            AnimationLibrary.Reloaded -= RefreshLists;
            AnimationLibrary.SuppressPolling = false;
        }

        private void RefreshLists()
        {
            _clipNames.Clear(); _clipNames.AddRange(AnimationLibrary.Clips.Keys); _clipNames.Sort(StringComparer.OrdinalIgnoreCase);
            _controllerNames.Clear(); _controllerNames.AddRange(AnimationLibrary.Controllers.Keys); _controllerNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void BuildPreviewObject()
        {
            _previewGo = new GameObject("Preview");
            _previewSr = _previewGo.AddComponent<SpriteRenderer>();
            _previewSr.renderLayer = RenderLayer.FirstPerson;
            _previewAnim = _previewGo.AddComponent<Animator>();
            _previewAnim.enabled = false;
            _previewGo.AddComponent<EventCatcher>().editor = this;
            _ghostPrev = MakeGhost("GhostPrev", -2);
            _ghostNext = MakeGhost("GhostNext", -1);
        }

        private SpriteRenderer MakeGhost(string n, int order)
        {
            var go = new GameObject(n);
            go.transform.SetParent(_previewGo.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.renderLayer = RenderLayer.FirstPerson;
            sr.sortingOrder = order;
            sr.color = new Color(1f, 1f, 1f, 0.3f);
            sr.fullBright = true;
            sr.enabled = false;
            return sr;
        }

        /// <summary>Moves the preview object to the rig (viewmodel under PlayerArms, or the world anchor) of the clip's controller.</summary>
        private void ApplyRig()
        {
            arms.localPosition = _armsHome;
            var owner = _controllerMode ? _controller : (_clip != null ? AnimationLibrary.RigOwner(_clip) : null);
            var rig = owner?.previewRig ?? new AnimationPreviewRig();
            bool world = rig.layer == RenderLayer.World;
            _previewGo.transform.SetParent(world ? worldAnchor : arms, false);
            _previewGo.transform.localPosition = rig.position + new Vector3(0f, world ? 0f : CentringLift(owner, rig), 0f);
            _previewGo.transform.localRotation = Quaternion.identity;
            _previewGo.transform.localScale = rig.scale;
            foreach (var sr in new[] { _previewSr, _ghostPrev, _ghostNext })
            {
                sr.renderLayer = rig.layer;
                sr.billboard = world && rig.billboard;
                sr.lit = world;
            }
            _ghostPrev.transform.localScale = _ghostNext.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Centred view: vertical offset that puts the average centre of the controller's sprites at the middle of the
        /// viewport (the timeline covers the bottom of the window where the weapon normally sits).
        /// </summary>
        private float CentringLift(AnimatorController owner, AnimationPreviewRig rig)
        {
            if (!_fitView || cam == null) return 0f;
            float z = MathF.Max(0.2f, _armsHome.z + rig.position.z);
            // average local centre of every sprite the controller can show (stable while states change)
            float cy = 0f; int n = 0;
            var clips = owner != null ? owner.clips.Values.ToList() : new List<SpriteAnimationClip>();
            if (_clip != null && !clips.Contains(_clip)) clips.Add(_clip);
            foreach (var c in clips)
                foreach (var (_, sp) in c.spriteKeys)
                {
                    if (sp == null) continue;
                    sp.GetLocalCorners(out var mn, out var mx);
                    cy += (mn.y + mx.y) * 0.5f; n++;
                }
            if (n > 0) cy /= n;
            float centreY = _armsHome.y + rig.position.y + cy * rig.scale.y;
            float viewportMid = (_viewport.height > 0f ? (_viewport.y + _viewport.height * 0.5f) / MathF.Max(1f, _ui.Batch?.Height ?? 720f) : 0.5f) * 2f - 1f;
            float fov = Application.Current?.Renderer.CameraConfig?.viewmodelFov ?? cam.fieldOfView;
            float targetY = viewportMid * z * MathF.Tan(fov * 0.5f * Mathf.Deg2Rad);
            return targetY - centreY;
        }

        // ------------------------------------------------------------------ selection

        public void SelectClip(string name)
        {
            var c = AnimationLibrary.GetClip(name);
            if (c == null) return;
            _clip = c;
            _time = 0f; _playing = false;
            _selKind = KeyKind.None; _selIndex = -1;
            _tlScroll = 0;
            // palette follows the clip's atlas
            var first = c.spriteKeys.Count > 0 ? SpriteLibrary.ReferenceOf(c.spriteKeys[0].sprite) : null;
            if (first != null && first.Contains('/'))
            {
                string atlasKey = first.Substring(0, first.IndexOf('/'));
                int i = SpriteLibrary.Atlases.Select(a => a.key).ToList().FindIndex(k => string.Equals(k, atlasKey, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) { _atlasIndex = i; _paletteScroll = 0f; }
            }
            if (!_controllerMode) { ApplyRig(); SampleClip(); }
        }

        public void SelectController(string name)
        {
            var c = AnimationLibrary.GetController(name);
            if (c == null) return;
            _controller = c;
            _selState = -1; _selTransition = -1;
            if (_controllerMode) StartControllerPreview();
        }

        private void SetControllerMode(bool on)
        {
            if (on && _controller == null && _controllerNames.Count > 0) SelectController(_controllerNames[0]);
            if (on && _controller == null) return;
            _controllerMode = on;
            if (on) StartControllerPreview();
            else
            {
                _previewAnim.enabled = false;
                _previewAnim.runtimeAnimatorController = null;
                _previewSr.enabled = true;
                _playing = false; _time = 0f;
                ApplyRig(); SampleClip();
            }
        }

        private void StartControllerPreview()
        {
            _previewAnim.runtimeAnimatorController = null;
            _previewAnim.runtimeAnimatorController = _controller;
            _previewAnim.enabled = true;
            _previewAnim.speed = _previewSpeed;
            _previewSr.enabled = true;
            _playing = true;
            ApplyRig();
        }

        // ------------------------------------------------------------------ update / playback

        protected override void Update()
        {
            bool wasPrompt = _prompt.Active;
            _prompt.Update();
            _ui.Modal = _prompt.Active;
            if (_prompt.Active || wasPrompt) return; // the key that closed the prompt must not reach the shortcuts
            HandleKeys();
            if (_controllerMode) UpdateControllerPreview();
            else UpdateClipPlayback();
            _flashes.RemoveAll(f => f.until < Time.unscaledTime);
        }

        private void UpdateClipPlayback()
        {
            if (_clip == null) return;
            if (_playing)
            {
                float prev = _time;
                _time += Time.unscaledDeltaTime * _previewSpeed;
                if (_clip.length <= 0f) { _time = 0f; _playing = false; }
                else if (_time >= _clip.length)
                {
                    FireFlashes(prev, _clip.length + 1E-4f);
                    if (_clip.loop) { _time = Mathf.Repeat(_time, _clip.length); prev = -1f; }
                    else { _time = _clip.length; _playing = false; }
                }
                FireFlashes(prev, _time);
            }
            SampleClip();
        }

        private void FireFlashes(float from, float to)
        {
            foreach (var (t, fn) in _clip.events) if (t > from && t <= to) Flash("EVENT " + fn);
        }

        public void Flash(string text) { _flashes.Add((text, Time.unscaledTime + 0.8f)); }

        /// <summary>Clip mode: the timeline drives the sprite directly (no Animator).</summary>
        private void SampleClip()
        {
            if (_clip == null || _controllerMode) return;
            var sp = _clip.SampleSprite(_time);
            _previewSr.sprite = sp;
            var en = _clip.SampleEnabled(_time);
            _previewSr.enabled = sp != null && (en ?? true);
            // onion skin: previous / next sprite keys around the playhead
            int cur = CurrentSpriteKey();
            _ghostPrev.enabled = _ghostNext.enabled = false;
            if (_onion && cur >= 0)
            {
                if (cur > 0) { _ghostPrev.sprite = _clip.spriteKeys[cur - 1].sprite; _ghostPrev.enabled = _ghostPrev.sprite != null; }
                if (cur + 1 < _clip.spriteKeys.Count) { _ghostNext.sprite = _clip.spriteKeys[cur + 1].sprite; _ghostNext.enabled = _ghostNext.sprite != null; }
            }
        }

        private void UpdateControllerPreview()
        {
            _ghostPrev.enabled = _ghostNext.enabled = false;
            _previewAnim.speed = _playing ? _previewSpeed : 0f;
            // the timeline follows the animator
            var st = _previewAnim.currentState;
            var c = st != null ? _controller.GetClip(st.clipName) : null;
            if (c != null && c != _clip) { _clip = c; _selKind = KeyKind.None; _selIndex = -1; _tlScroll = 0; }
            _time = _previewAnim.currentTime;
        }

        /// <summary>Index of the sprite key active at the playhead (-1 when none).</summary>
        private int CurrentSpriteKey()
        {
            int idx = -1;
            for (int i = 0; i < _clip.spriteKeys.Count; i++) if (_clip.spriteKeys[i].time <= _time + 1E-5f) idx = i; else break;
            return idx;
        }

        private void SetFrame(int frame)
        {
            frame = Mathf.Clamp(frame, 0, Math.Max(0, Frames));
            _time = frame / Fps;
            if (_controllerMode) return;
            SampleClip();
        }

        // ------------------------------------------------------------------ keyboard

        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.F1)) _showHelp = !_showHelp;
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_showHelp) _showHelp = false; else Application.Quit(); }
            if (Ctrl && Input.GetKeyDown(KeyCode.S)) SaveAll();
            if (Ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (Ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (Ctrl && Input.GetKeyDown(KeyCode.N)) PromptNewClip();
            if (Ctrl && Input.GetKeyDown(KeyCode.D)) PromptDuplicateClip();
            if (Input.GetKeyDown(KeyCode.F2)) PromptRenameClip();
            if (Input.GetKeyDown(KeyCode.P)) SetControllerMode(!_controllerMode);
            if (Input.GetKeyDown(KeyCode.O)) { _onion = !_onion; SampleClip(); }
            if (Input.GetKeyDown(KeyCode.G)) { _fitView = !_fitView; ApplyRig(); Status(_fitView ? "VIEW: CENTRED IN VIEWPORT" : "VIEW: GAME FRAMING"); }
            if (Input.GetKeyDown(KeyCode.Tab)) _leftTab = 1 - _leftTab;
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) _previewSpeed = MathF.Max(0.1f, _previewSpeed - 0.25f);
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) _previewSpeed = MathF.Min(4f, _previewSpeed + 0.25f);

            if (_clip == null) return;
            int step = Shift ? 5 : 1;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { _playing = false; SetFrame(CurrentFrame - step); }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { _playing = false; SetFrame(CurrentFrame + step); }
            if (Input.GetKeyDown(KeyCode.Home)) { _playing = false; SetFrame(0); }
            if (Input.GetKeyDown(KeyCode.End)) { _playing = false; SetFrame(Frames); }
            if (Input.GetKeyDown(KeyCode.UpArrow)) JumpToKey(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) JumpToKey(1);

            if (_controllerMode) return;
            if (Input.GetKeyDown(KeyCode.L)) { PushUndoClip(); _clip.loop = !_clip.loop; MarkDirty(_clip); }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) SetLength(Frames - step);
            if (Input.GetKeyDown(KeyCode.RightBracket)) SetLength(Frames + step);
            if (Input.GetKeyDown(KeyCode.E)) PromptEvent();
            if (Input.GetKeyDown(KeyCode.N) && !Ctrl) ToggleEnabledKey();
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                if (Shift) PromptDeleteClip(); else DeleteSelectedKey();
            }
            if (Input.GetKeyDown(KeyCode.Return) && _selKind == KeyKind.Event) PromptRenameEvent();
            if (Ctrl && Input.GetKeyDown(KeyCode.C)) CopyKey();
            if (Ctrl && Input.GetKeyDown(KeyCode.V)) PasteKey();
            if (Input.GetKeyDown(KeyCode.I)) InsertKeyFromPalette();
        }

        private void TogglePlay()
        {
            if (_controllerMode) { _playing = !_playing; return; }
            if (_clip == null) return;
            if (!_playing && _time >= _clip.length && !_clip.loop) _time = 0f;
            _playing = !_playing;
        }

        private void JumpToKey(int dir)
        {
            if (_clip == null || _clip.spriteKeys.Count == 0) return;
            _playing = false;
            int cur = CurrentFrame;
            var frames = _clip.spriteKeys.Select(k => Mathf.RoundToInt(k.time * Fps)).ToList();
            int target = dir > 0 ? frames.FirstOrDefault(f => f > cur, frames[frames.Count - 1]) : frames.LastOrDefault(f => f < cur, frames[0]);
            SetFrame(target);
        }

        // ------------------------------------------------------------------ clip edits

        private void MarkDirty(SpriteAnimationClip c) { if (c != null) _dirtyClips.Add(c.name); }
        private void MarkDirty(AnimatorController c) { if (c != null) _dirtyControllers.Add(c.name); }

        private void PushUndoClip()
        {
            if (_clip == null) return;
            _undo.Push(new UndoEntry { isClip = true, name = _clip.name, json = AnimationLibrary.ToJson(_clip) });
            _redo.Clear();
            if (_undo.Count > 200) _undo = TrimStack(_undo, 150);
        }

        private void PushUndoController()
        {
            if (_controller == null) return;
            var c = _controller.baseController ?? _controller; // graph edits apply to the base
            _undo.Push(new UndoEntry { isClip = false, name = c.name, json = AnimationLibrary.ToJson(c) });
            _redo.Clear();
        }

        private static Stack<UndoEntry> TrimStack(Stack<UndoEntry> s, int keep)
        {
            var arr = s.ToArray(); // top first
            var r = new Stack<UndoEntry>();
            for (int i = Math.Min(keep, arr.Length) - 1; i >= 0; i--) r.Push(arr[i]);
            return r;
        }

        private void Undo() => Swap(_undo, _redo);
        private void Redo() => Swap(_redo, _undo);

        private void Swap(Stack<UndoEntry> from, Stack<UndoEntry> to)
        {
            if (from.Count == 0) return;
            var e = from.Pop();
            if (e.isClip)
            {
                var c = AnimationLibrary.GetClip(e.name);
                if (c == null) return;
                to.Push(new UndoEntry { isClip = true, name = e.name, json = AnimationLibrary.ToJson(c) });
                c.CopyFrom(AnimationLibrary.FromDto(AnimationLibrary.ParseClip(e.json)));
                MarkDirty(c);
                if (_clip == c) { _selKind = KeyKind.None; _selIndex = -1; SetFrame(Math.Min(CurrentFrame, Frames)); }
            }
            else
            {
                var c = AnimationLibrary.GetController(e.name);
                if (c == null) return;
                to.Push(new UndoEntry { isClip = false, name = e.name, json = AnimationLibrary.ToJson(c) });
                c.CopyFrom(AnimationLibrary.FromDto(AnimationLibrary.ParseController(e.json)));
                AnimationLibrary.RefreshOverrides(c.name);
                MarkDirty(c);
                _selState = -1; _selTransition = -1;
            }
            Status(from == _undo ? "UNDO" : "REDO");
        }

        private void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 2.5f; }

        private void SetLength(int frames)
        {
            frames = Math.Max(0, frames);
            PushUndoClip();
            _clip.length = frames / Fps;
            MarkDirty(_clip);
            SetFrame(Math.Min(CurrentFrame, frames));
        }

        private void SetFrameRate(float fps)
        {
            fps = Mathf.Clamp(fps, 1f, 240f);
            PushUndoClip();
            _clip.frameRate = fps;
            MarkDirty(_clip);
        }

        /// <summary>Palette click: replaces the sprite key at the playhead or inserts a new one there.</summary>
        public void PlaceSprite(Sprite sprite)
        {
            if (_clip == null || sprite == null || _controllerMode) return;
            PushUndoClip();
            int frame = CurrentFrame;
            float t = frame / Fps;
            int existing = _clip.spriteKeys.FindIndex(k => Mathf.RoundToInt(k.time * Fps) == frame);
            if (existing >= 0) _clip.spriteKeys[existing] = (t, sprite);
            else
            {
                _clip.spriteKeys.Add((t, sprite));
                _clip.SortKeys();
                existing = _clip.spriteKeys.FindIndex(k => Mathf.RoundToInt(k.time * Fps) == frame);
                if (frame >= Frames) _clip.length = (frame + 1) / Fps; // keep the new key inside the clip
            }
            _selKind = KeyKind.Sprite; _selIndex = existing;
            MarkDirty(_clip);
            SampleClip();
        }

        private Sprite _paletteSprite; // last sprite highlighted in the palette (for 'I')
        private void InsertKeyFromPalette() { if (_paletteSprite != null) PlaceSprite(_paletteSprite); }

        private void ToggleEnabledKey()
        {
            if (_clip == null) return;
            PushUndoClip();
            int frame = CurrentFrame; float t = frame / Fps;
            int i = _clip.enabledKeys.FindIndex(k => Mathf.RoundToInt(k.time * Fps) == frame);
            if (i >= 0) _clip.enabledKeys[i] = (t, !_clip.enabledKeys[i].enabled);
            else { _clip.enabledKeys.Add((t, !(_clip.SampleEnabled(t) ?? true))); _clip.SortKeys(); i = _clip.enabledKeys.FindIndex(k => Mathf.RoundToInt(k.time * Fps) == frame); }
            _selKind = KeyKind.Enabled; _selIndex = i;
            MarkDirty(_clip); SampleClip();
        }

        private void PromptEvent()
        {
            if (_clip == null) return;
            int frame = CurrentFrame;
            _prompt.Open($"EVENT FUNCTION AT FRAME {frame}", "OnFire", name =>
            {
                if (string.IsNullOrEmpty(name)) return;
                PushUndoClip();
                _clip.events.Add((frame / Fps, name));
                _clip.SortKeys();
                _selKind = KeyKind.Event; _selIndex = _clip.events.FindIndex(e => e.function == name && Mathf.RoundToInt(e.time * Fps) == frame);
                MarkDirty(_clip);
            });
        }

        private void PromptRenameEvent()
        {
            if (_selKind != KeyKind.Event || _selIndex < 0 || _selIndex >= _clip.events.Count) return;
            var (t, fn) = _clip.events[_selIndex];
            int idx = _selIndex;
            _prompt.Open("RENAME EVENT", fn, name =>
            {
                if (string.IsNullOrEmpty(name)) return;
                PushUndoClip();
                _clip.events[idx] = (t, name);
                MarkDirty(_clip);
            });
        }

        private void DeleteSelectedKey()
        {
            if (_clip == null || _selKind == KeyKind.None || _selIndex < 0) return;
            PushUndoClip();
            switch (_selKind)
            {
                case KeyKind.Sprite: if (_selIndex < _clip.spriteKeys.Count) _clip.spriteKeys.RemoveAt(_selIndex); break;
                case KeyKind.Enabled: if (_selIndex < _clip.enabledKeys.Count) _clip.enabledKeys.RemoveAt(_selIndex); break;
                case KeyKind.Event: if (_selIndex < _clip.events.Count) _clip.events.RemoveAt(_selIndex); break;
            }
            _selKind = KeyKind.None; _selIndex = -1;
            MarkDirty(_clip); SampleClip();
        }

        private (KeyKind kind, Sprite sprite, bool enabled, string fn) _clipboard = (KeyKind.None, null, false, null);

        private void CopyKey()
        {
            if (_selKind == KeyKind.None || _selIndex < 0) return;
            switch (_selKind)
            {
                case KeyKind.Sprite: if (_selIndex < _clip.spriteKeys.Count) _clipboard = (KeyKind.Sprite, _clip.spriteKeys[_selIndex].sprite, false, null); break;
                case KeyKind.Enabled: if (_selIndex < _clip.enabledKeys.Count) _clipboard = (KeyKind.Enabled, null, _clip.enabledKeys[_selIndex].enabled, null); break;
                case KeyKind.Event: if (_selIndex < _clip.events.Count) _clipboard = (KeyKind.Event, null, false, _clip.events[_selIndex].function); break;
            }
            Status("COPIED KEY");
        }

        private void PasteKey()
        {
            if (_clip == null || _clipboard.kind == KeyKind.None) return;
            int frame = CurrentFrame; float t = frame / Fps;
            PushUndoClip();
            switch (_clipboard.kind)
            {
                case KeyKind.Sprite: PlaceSprite(_clipboard.sprite); return;
                case KeyKind.Enabled:
                    _clip.enabledKeys.RemoveAll(k => Mathf.RoundToInt(k.time * Fps) == frame);
                    _clip.enabledKeys.Add((t, _clipboard.enabled)); break;
                case KeyKind.Event: _clip.events.Add((t, _clipboard.fn)); break;
            }
            _clip.SortKeys(); MarkDirty(_clip); SampleClip();
        }

        /// <summary>Moves a key to a frame (drag). Keys are re-sorted on release.</summary>
        private void MoveKey(KeyKind kind, int index, int frame)
        {
            frame = Mathf.Clamp(frame, 0, Math.Max(0, Frames));
            float t = frame / Fps;
            switch (kind)
            {
                case KeyKind.Sprite: if (index < _clip.spriteKeys.Count) _clip.spriteKeys[index] = (t, _clip.spriteKeys[index].sprite); break;
                case KeyKind.Enabled: if (index < _clip.enabledKeys.Count) _clip.enabledKeys[index] = (t, _clip.enabledKeys[index].enabled); break;
                case KeyKind.Event: if (index < _clip.events.Count) _clip.events[index] = (t, _clip.events[index].function); break;
            }
            MarkDirty(_clip);
        }

        private void EndKeyDrag()
        {
            if (_dragKind == KeyKind.None) return;
            // re-sort and keep the moved key selected
            object moved = null;
            switch (_dragKind)
            {
                case KeyKind.Sprite: if (_dragIndex < _clip.spriteKeys.Count) moved = _clip.spriteKeys[_dragIndex]; break;
                case KeyKind.Enabled: if (_dragIndex < _clip.enabledKeys.Count) moved = _clip.enabledKeys[_dragIndex]; break;
                case KeyKind.Event: if (_dragIndex < _clip.events.Count) moved = _clip.events[_dragIndex]; break;
            }
            _clip.SortKeys();
            if (moved != null)
            {
                switch (_dragKind)
                {
                    case KeyKind.Sprite: _selIndex = _clip.spriteKeys.IndexOf(((float, Sprite))moved); break;
                    case KeyKind.Enabled: _selIndex = _clip.enabledKeys.IndexOf(((float, bool))moved); break;
                    case KeyKind.Event: _selIndex = _clip.events.IndexOf(((float, string))moved); break;
                }
                _selKind = _dragKind;
            }
            _dragKind = KeyKind.None; _dragIndex = -1;
            SampleClip();
        }

        // ------------------------------------------------------------------ clip management

        private bool ClipNameFree(string n) => !string.IsNullOrEmpty(n) && AnimationLibrary.GetClip(n) == null;

        private void PromptNewClip()
        {
            _prompt.Open("NEW CLIP NAME", "NewClip", name =>
            {
                if (!ClipNameFree(name)) { Status("NAME IN USE OR EMPTY"); return; }
                var c = new SpriteAnimationClip(name, 10f / 60f) { frameRate = 60f };
                AnimationLibrary.Register(c);
                RefreshLists(); MarkDirty(c);
                if (_controllerMode) SetControllerMode(false);
                SelectClip(name);
            });
        }

        private void PromptDuplicateClip()
        {
            if (_clip == null) return;
            var src = _clip;
            _prompt.Open("DUPLICATE CLIP AS", src.name + "_copy", name =>
            {
                if (!ClipNameFree(name)) { Status("NAME IN USE OR EMPTY"); return; }
                var c = new SpriteAnimationClip(name, src.length, src.loop);
                c.CopyFrom(src);
                AnimationLibrary.Register(c);
                RefreshLists(); MarkDirty(c);
                if (_controllerMode) SetControllerMode(false);
                SelectClip(name);
            });
        }

        private void PromptRenameClip()
        {
            if (_clip == null) return;
            var c = _clip;
            _prompt.Open("RENAME CLIP", c.name, name =>
            {
                if (name == c.name) return;
                if (!ClipNameFree(name)) { Status("NAME IN USE OR EMPTY"); return; }
                _deletedClips.Add(c.name);
                _dirtyClips.Remove(c.name);
                AnimationLibrary.Clips.Remove(c.name);
                c.name = name;
                AnimationLibrary.Register(c);
                foreach (var ctrl in AnimationLibrary.ControllersUsing(c)) MarkDirty(ctrl);
                RefreshLists(); MarkDirty(c);
            });
        }

        private void PromptDeleteClip()
        {
            if (_clip == null) return;
            var c = _clip;
            var users = AnimationLibrary.ControllersUsing(c).Select(x => x.name).ToList();
            string warn = users.Count > 0 ? $" (USED BY {string.Join(", ", users)})" : "";
            _prompt.Open($"DELETE CLIP {c.name}{warn}? TYPE YES", "", answer =>
            {
                if (!answer.Equals("yes", StringComparison.OrdinalIgnoreCase)) return;
                foreach (var ctrl in AnimationLibrary.Controllers.Values)
                {
                    foreach (var k in ctrl.clips.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) ctrl.clips.Remove(k);
                    foreach (var k in ctrl.overrides.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) ctrl.overrides.Remove(k);
                    if (users.Contains(ctrl.name)) MarkDirty(ctrl);
                }
                AnimationLibrary.Clips.Remove(c.name);
                _dirtyClips.Remove(c.name);
                _deletedClips.Add(c.name);
                RefreshLists();
                _clip = null;
                if (_clipNames.Count > 0) SelectClip(_clipNames[0]);
            });
        }

        public void SaveAll()
        {
            try
            {
                if (AnimationLibrary.Directory == null) { Status("NO ANIMATIONS FOLDER"); return; }
                foreach (var n in _deletedClips) AnimationLibrary.DeleteClipFile(n);
                _deletedClips.Clear();
                AnimationLibrary.SaveAll();
                _dirtyClips.Clear(); _dirtyControllers.Clear();
                Status($"SAVED {AnimationLibrary.Clips.Count} CLIPS + {AnimationLibrary.Controllers.Count} CONTROLLERS");
                Debug.LogWarning("Saved animations to " + AnimationLibrary.Directory);
            }
            catch (Exception e) { Status("SAVE FAILED: " + e.Message); Debug.LogError(e.ToString()); }
        }

        // ------------------------------------------------------------------ controller edits (graph = base controller when overriding)

        private AnimatorController Graph => _controller?.baseController ?? _controller;

        private void AfterGraphEdit()
        {
            var g = Graph;
            g.version++;
            AnimationLibrary.RefreshOverrides(g.name);
            MarkDirty(g);
            if (_controllerMode) { _previewAnim.runtimeAnimatorController = null; _previewAnim.runtimeAnimatorController = _controller; }
        }

        private void PromptNewState()
        {
            if (_controller == null) return;
            _prompt.Open("NEW STATE NAME (CLIP SLOT = NAME)", "NewState", name =>
            {
                if (string.IsNullOrEmpty(name) || Graph.FindState(name) != null) { Status("NAME IN USE OR EMPTY"); return; }
                PushUndoController();
                var st = Graph.AddState(name);
                if (_clip != null) { Graph.clips[name] = _clip; }
                AfterGraphEdit();
                _selState = Graph.states.IndexOf(st); _selTransition = -1;
            });
        }

        private void PromptNewParameter()
        {
            if (_controller == null) return;
            _prompt.Open("NEW PARAMETER (NAME OR NAME:BOOL|TRIGGER|FLOAT|INT)", "Param:Bool", text =>
            {
                if (string.IsNullOrEmpty(text)) return;
                string n = text, ty = "Bool";
                int colon = text.IndexOf(':');
                if (colon > 0) { n = text.Substring(0, colon).Trim(); ty = text.Substring(colon + 1).Trim(); }
                if (!Enum.TryParse<AnimatorParameterType>(ty, true, out var pt)) pt = AnimatorParameterType.Bool;
                PushUndoController();
                Graph.parameters[n] = pt;
                AfterGraphEdit();
            });
        }

        private void RemoveState(int index)
        {
            var g = Graph;
            if (index < 0 || index >= g.states.Count) return;
            PushUndoController();
            var st = g.states[index];
            g.states.RemoveAt(index);
            foreach (var s in g.states) s.transitions.RemoveAll(t => t.destination == st);
            g.anyStateTransitions.RemoveAll(t => t.destination == st);
            if (g.defaultState == st) g.defaultState = g.states.Count > 0 ? g.states[0] : null;
            AfterGraphEdit();
            _selState = -1; _selTransition = -1;
        }

        private void AddTransition(int stateIndex)
        {
            var g = Graph;
            if (g.states.Count == 0) return;
            PushUndoController();
            var dest = g.states[0];
            var t = stateIndex < 0 ? g.AddAnyStateTransition(dest) : g.states[stateIndex].AddTransition(dest);
            // default: exit-time transition when there are no parameters, else the first parameter
            if (g.parameters.Count > 0) { var p = g.parameters.First(); t.When(p.Key, p.Value == AnimatorParameterType.Float ? AnimatorConditionMode.Greater : AnimatorConditionMode.If); }
            else t.hasExitTime = true;
            AfterGraphEdit();
            _selState = stateIndex;
            _selTransition = (stateIndex < 0 ? g.anyStateTransitions.Count : g.states[stateIndex].transitions.Count) - 1;
        }

        private void RemoveTransition(int stateIndex, int tIndex)
        {
            var g = Graph;
            var list = stateIndex < 0 ? g.anyStateTransitions : (stateIndex < g.states.Count ? g.states[stateIndex].transitions : null);
            if (list == null || tIndex < 0 || tIndex >= list.Count) return;
            PushUndoController();
            list.RemoveAt(tIndex);
            AfterGraphEdit();
            _selTransition = -1;
        }

        /// <summary>Cycles a clip reference (state slot or override slot) through the library.</summary>
        private SpriteAnimationClip CycleClip(SpriteAnimationClip current, int dir)
        {
            if (_clipNames.Count == 0) return current;
            int i = current != null ? _clipNames.FindIndex(n => string.Equals(n, current.name, StringComparison.OrdinalIgnoreCase)) : -1;
            i = ((i + dir) % (_clipNames.Count + 1) + (_clipNames.Count + 1)) % (_clipNames.Count + 1); // +1 = "none"
            return i == _clipNames.Count ? null : AnimationLibrary.GetClip(_clipNames[i]);
        }

        private static readonly AnimatorConditionMode[] Modes = { AnimatorConditionMode.If, AnimatorConditionMode.IfNot, AnimatorConditionMode.Greater, AnimatorConditionMode.Less, AnimatorConditionMode.Equals, AnimatorConditionMode.NotEqual };

        /// <summary>Preview parameter toggles (controller mode): bool flips, trigger fires, float/int +/-.</summary>
        private void PokeParameter(string n, AnimatorParameterType t, int dir)
        {
            if (!_controllerMode) { Status("PRESS P FOR CONTROLLER PREVIEW"); return; }
            switch (t)
            {
                case AnimatorParameterType.Bool: _previewAnim.SetBool(n, !_previewAnim.GetBool(n)); break;
                case AnimatorParameterType.Trigger: _previewAnim.SetTrigger(n); Flash("TRIGGER " + n); break;
                default: _previewAnim.SetFloat(n, _previewAnim.GetFloat(n) + dir); break;
            }
        }

        private string ParamValue(string n, AnimatorParameterType t)
        {
            if (!_controllerMode) return "-";
            switch (t)
            {
                case AnimatorParameterType.Bool: return _previewAnim.GetBool(n) ? "TRUE" : "FALSE";
                case AnimatorParameterType.Trigger: return _previewAnim.IsTriggerSet(n) ? "SET" : "FIRE";
                default: return _previewAnim.GetFloat(n).ToString("0.##");
            }
        }
    }

    /// <summary>Receives the animation events the preview Animator dispatches (SendMessage) and shows them as flashes.</summary>
    public sealed class EventCatcher : Behaviour, GameObject.IMessageSink
    {
        public AnimEditor editor;
        public void OnMessage(string methodName, object value) => editor?.Flash("EVENT " + methodName);
        protected override void Update() { }
    }
}
