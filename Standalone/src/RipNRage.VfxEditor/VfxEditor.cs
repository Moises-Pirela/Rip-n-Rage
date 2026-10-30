using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Particles;

namespace RipNRage.VfxEditor
{
    /// <summary>
    /// Particle effect editor: the effect list and the emitter list on the left, a reflection-driven property grid
    /// on the right (every public field of <see cref="ParticleEmitter"/> / <see cref="ParticleEffect"/> becomes a
    /// widget, guided by the Vfx* attributes), and a live preview in the middle rendered by the real engine —
    /// the same <see cref="ParticleWorld"/> / <see cref="ParticleRenderer"/> the game uses, on a wall, the floor or
    /// in the air, looping. Edits apply to the running instance immediately; files are written with Ctrl+S and a
    /// running game hot-reloads them.
    /// </summary>
    public sealed partial class VfxEditor : Behaviour, IUiDrawer
    {
        public PreviewRig rig;
        public string startEffect;

        // ---- document ----
        private ParticleEffect _effect;
        private readonly List<string> _names = new List<string>();
        private readonly HashSet<string> _dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _selEmitter = -1;          // -1 = effect properties
        public bool Dirty => _dirty.Count > 0;
        public ParticleEffect Effect => _effect;
        public ParticleEmitter SelectedEmitter => _effect != null && _selEmitter >= 0 && _selEmitter < _effect.emitters.Count ? _effect.emitters[_selEmitter] : null;

        // ---- preview ----
        private ParticleEffectInstance _instance;
        private int _surfaceMode;              // 0 wall, 1 floor, 2 air
        private Vector3 _anchorPos, _anchorNormal;
        private bool _anchorCustom;
        private bool _loop = true;
        private bool _paused;
        private float _timeScale = 1f;
        private bool _respawn;
        private float _replayTimer;
        private int _effectLabelTimer;

        // ---- camera orbit ----
        private float _yaw = 180f, _pitch = 12f, _distance = 4.2f;
        private Vector3 _orbitTarget = new Vector3(0f, 1.3f, 4f);
        private bool _orbiting;
        private Vector2 _lastMouse;

        // ---- ui ----
        private readonly ImUi _ui = new ImUi();
        private readonly TextPrompt _prompt = new TextPrompt();
        private bool _showHelp;
        private float _listScroll, _emitterScroll, _gridScroll, _paletteScroll;
        private string _status = "";
        private float _statusUntil;
        private Rect _viewport;
        private bool _changedThisFrame, _changedLastFrame;
        private string _frameStartJson;
        private readonly ParticleEmitter _emitterDefaults = new ParticleEmitter();
        private readonly ParticleEffect _effectDefaults = new ParticleEffect();

        // palette (sprite picker)
        private bool _paletteOpen;
        private Action<string> _paletteAccept;
        private int _paletteAtlas;

        // ---- undo ----
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();

        // ------------------------------------------------------------------ lifecycle

        protected override void Start()
        {
            VfxLibrary.SuppressPolling = true; // the editor is the writer
            VfxLibrary.Reloaded += RefreshNames;
            RefreshNames();
            UpdateAnchor();
            if (!string.IsNullOrEmpty(startEffect) && VfxLibrary.Get(startEffect) != null) Select(startEffect);
            else if (_names.Count > 0) Select(_names[0]);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        protected override void OnDestroy()
        {
            VfxLibrary.Reloaded -= RefreshNames;
            VfxLibrary.SuppressPolling = false;
        }

        private void RefreshNames()
        {
            _names.Clear();
            _names.AddRange(VfxLibrary.Effects.Keys);
            _names.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public void Select(string name)
        {
            var fx = VfxLibrary.Get(name);
            if (fx == null) return;
            _effect = fx;
            _selEmitter = fx.emitters.Count > 0 ? 0 : -1;
            _undo.Clear(); _redo.Clear();
            _frameStartJson = null; _changedThisFrame = false; _changedLastFrame = false;
            _gridScroll = 0f;
            Respawn();
        }

        public void SetSurfaceMode(int mode)
        {
            _surfaceMode = ((mode % 3) + 3) % 3;
            _anchorCustom = false;
            UpdateAnchor();
            Respawn();
        }

        private void UpdateAnchor()
        {
            if (_anchorCustom) return;
            switch (_surfaceMode)
            {
                case 1: _anchorPos = new Vector3(0f, 0f, 2f); _anchorNormal = Vector3.up; break;
                case 2: _anchorPos = new Vector3(0f, 1.5f, 2f); _anchorNormal = Vector3.up; break;
                default: _anchorPos = new Vector3(0f, 1.3f, 4f); _anchorNormal = Vector3.back; break;
            }
            _orbitTarget = _anchorPos + _anchorNormal * 0.3f;
        }

        private bool IsTracer(ParticleEffect fx) => fx != null && fx.emitters.Any(e => e.enabled && e.lifetimeFromDistance);
        private bool IsTrail(ParticleEffect fx) => fx != null && fx.emitters.Count > 0 && fx.Duration <= 0f;

        /// <summary>Kills the preview instance and starts the effect again at the anchor (tracers fly in from the camera side, trails follow the circling dummy).</summary>
        public void Respawn()
        {
            if (_instance != null) { ParticleWorld.Remove(_instance); _instance = null; }
            if (rig?.trailDummy != null) rig.trailDummy.gameObject.SetActive(false);
            if (_effect == null) return;
            if (IsTrail(_effect))
            {
                var dummy = rig.trailDummy;
                dummy.gameObject.SetActive(true);
                dummy.center = new Vector3(0f, 1.6f, 1.8f);
                _instance = ParticleWorld.Spawn(_effect, dummy.transform.position, -dummy.transform.forward);
                _instance.Follow(dummy.transform, default, backwards: true);
            }
            else if (IsTracer(_effect))
            {
                Vector3 from = _anchorPos + _anchorNormal * 6f + Vector3.up * 0.2f + Vector3.Cross(_anchorNormal, Vector3.up) * 0.8f;
                Vector3 d = _anchorPos - from;
                _instance = ParticleWorld.Spawn(_effect, from, d.normalized, 1f, d.magnitude);
            }
            else _instance = ParticleWorld.Spawn(_effect, _anchorPos, _anchorNormal);
            if (_instance != null) _instance.loop = _loop;
            _replayTimer = 0f;
        }

        // ------------------------------------------------------------------ editing helpers

        private void MarkChanged()
        {
            _changedThisFrame = true;
            if (_effect != null) _dirty.Add(_effect.name);
        }

        /// <summary>Structural change (emitter added/removed, sprite / count / lifetime changed): restart the preview.</summary>
        private void MarkStructural()
        {
            MarkChanged();
            _respawn = true;
        }

        private void Flash(string s) { _status = s; _statusUntil = Time.unscaledTime + 2.5f; }

        private void PushUndo(string json)
        {
            if (json == null) return;
            _undo.Push(json);
            if (_undo.Count > 100) { var arr = _undo.ToArray(); _undo.Clear(); for (int i = Math.Min(99, arr.Length - 1); i >= 0; i--) _undo.Push(arr[i]); }
            _redo.Clear();
        }

        private void Undo()
        {
            if (_effect == null || _undo.Count == 0) return;
            _redo.Push(VfxLibrary.ToJson(_effect));
            ApplyJson(_undo.Pop());
            Flash("UNDO");
        }

        private void Redo()
        {
            if (_effect == null || _redo.Count == 0) return;
            _undo.Push(VfxLibrary.ToJson(_effect));
            ApplyJson(_redo.Pop());
            Flash("REDO");
        }

        /// <summary>Replaces the current effect's content in place (so the registered object stays the same).</summary>
        private void ApplyJson(string json)
        {
            var fx = VfxLibrary.FromJson(json);
            if (fx == null) return;
            string oldName = _effect.name;
            CopyInto(fx, _effect);
            if (!string.Equals(oldName, _effect.name, StringComparison.Ordinal)) { VfxLibrary.Remove(oldName); VfxLibrary.Register(_effect); RefreshNames(); }
            _dirty.Add(_effect.name);
            _selEmitter = Math.Min(_selEmitter, _effect.emitters.Count - 1);
            _respawn = true;
        }

        private static void CopyInto(ParticleEffect src, ParticleEffect dst)
        {
            dst.name = src.name; dst.description = src.description;
            dst.emitters = src.emitters;
            dst.hasLight = src.hasLight; dst.lightColor = src.lightColor; dst.lightIntensity = src.lightIntensity; dst.lightRange = src.lightRange;
            dst.lightDuration = src.lightDuration; dst.lightDelay = src.lightDelay; dst.cullRadius = src.cullRadius; dst.scale = src.scale;
        }

        private string UniqueName(string baseName)
        {
            string n = baseName; int i = 2;
            while (VfxLibrary.Get(n) != null) n = baseName + "_" + i++;
            return n;
        }

        public void NewEffect()
        {
            var fx = new ParticleEffect { name = UniqueName("new_effect"), description = "" };
            fx.emitters.Add(new ParticleEmitter { name = "emitter" });
            VfxLibrary.Register(fx);
            RefreshNames();
            _dirty.Add(fx.name);
            Select(fx.name);
            Flash("NEW EFFECT " + fx.name.ToUpperInvariant());
        }

        public void DuplicateEffect()
        {
            if (_effect == null) return;
            var fx = _effect.Clone();
            fx.name = UniqueName(_effect.name + "_copy");
            VfxLibrary.Register(fx);
            RefreshNames();
            _dirty.Add(fx.name);
            Select(fx.name);
            Flash("DUPLICATED AS " + fx.name.ToUpperInvariant());
        }

        /// <summary>Asks for confirmation, then removes the effect and its file.</summary>
        public void DeleteEffect()
        {
            if (_effect == null) return;
            _prompt.Open($"DELETE {_effect.name.ToUpperInvariant()}? TYPE YES", "", answer => { if (string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) DeleteEffectNow(); });
        }

        private void DeleteEffectNow()
        {
            if (_effect == null) return;
            string name = _effect.name;
            VfxLibrary.DeleteFile(name);
            _dirty.Remove(name);
            RefreshNames();
            if (_instance != null) { ParticleWorld.Remove(_instance); _instance = null; }
            _effect = null;
            if (_names.Count > 0) Select(_names[0]);
            Flash("DELETED " + name.ToUpperInvariant());
        }

        public void RenameEffect(string newName)
        {
            if (_effect == null || string.IsNullOrWhiteSpace(newName) || newName == _effect.name) return;
            if (VfxLibrary.Get(newName) != null) { Flash("NAME TAKEN"); return; }
            PushUndo(VfxLibrary.ToJson(_effect));
            string old = _effect.name;
            VfxLibrary.DeleteFile(old);
            _dirty.Remove(old);
            _effect.name = newName;
            VfxLibrary.Register(_effect);
            _dirty.Add(newName);
            RefreshNames();
            Flash("RENAMED TO " + newName.ToUpperInvariant());
        }

        public void SaveAll()
        {
            int n = 0;
            foreach (var name in new List<string>(_dirty))
            {
                var fx = VfxLibrary.Get(name);
                if (fx == null) { _dirty.Remove(name); continue; }
                try { VfxLibrary.Save(fx); _dirty.Remove(name); n++; }
                catch (Exception e) { Flash("SAVE FAILED: " + e.Message); return; }
            }
            Flash(n == 0 ? "NOTHING TO SAVE" : $"SAVED {n} EFFECT{(n == 1 ? "" : "S")} TO {VfxLibrary.Directory}");
        }

        public void AddEmitter()
        {
            if (_effect == null) return;
            PushUndo(VfxLibrary.ToJson(_effect));
            var e = SelectedEmitter != null ? SelectedEmitter.Clone() : new ParticleEmitter();
            e.name = SelectedEmitter != null ? e.name + "_copy" : "emitter";
            _effect.emitters.Insert(Math.Min(_effect.emitters.Count, _selEmitter + 1), e);
            _selEmitter = Math.Min(_effect.emitters.Count - 1, _selEmitter + 1);
            MarkStructural();
        }

        public void RemoveEmitter()
        {
            if (SelectedEmitter == null) return;
            PushUndo(VfxLibrary.ToJson(_effect));
            _effect.emitters.RemoveAt(_selEmitter);
            _selEmitter = Math.Min(_selEmitter, _effect.emitters.Count - 1);
            MarkStructural();
        }

        public void MoveEmitter(int dir)
        {
            if (SelectedEmitter == null) return;
            int to = _selEmitter + dir;
            if (to < 0 || to >= _effect.emitters.Count) return;
            PushUndo(VfxLibrary.ToJson(_effect));
            var e = _effect.emitters[_selEmitter];
            _effect.emitters.RemoveAt(_selEmitter);
            _effect.emitters.Insert(to, e);
            _selEmitter = to;
            MarkStructural();
        }

        // ------------------------------------------------------------------ per-frame

        protected override void Update()
        {
            // undo snapshot at the start of an edit streak: edits happen during the UI pass (after Update), so the
            // state captured at the previous Update is the one before the first change
            if (_changedThisFrame && !_changedLastFrame) PushUndo(_frameStartJson);
            _changedLastFrame = _changedThisFrame;
            _changedThisFrame = false;
            _frameStartJson = _effect != null ? VfxLibrary.ToJson(_effect) : null;

            if (_prompt.Active) { _prompt.Update(); return; }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (_paletteOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { _paletteOpen = false; _paletteAccept = null; }
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1)) _showHelp = !_showHelp;
            if (Input.GetKeyDown(KeyCode.Escape) && _showHelp) _showHelp = false;
            if (ctrl && Input.GetKeyDown(KeyCode.S)) SaveAll();
            if (ctrl && Input.GetKeyDown(KeyCode.N)) NewEffect();
            if (ctrl && Input.GetKeyDown(KeyCode.D)) DuplicateEffect();
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (Input.GetKeyDown(KeyCode.F2) && _effect != null) _prompt.Open("RENAME EFFECT", _effect.name, RenameEffect);
            if (Input.GetKeyDown(KeyCode.Delete) && !ctrl) { if (shift) DeleteEffect(); else RemoveEmitter(); }
            if (Input.GetKeyDown(KeyCode.Insert) || (ctrl && Input.GetKeyDown(KeyCode.E))) AddEmitter();
            if (Input.GetKeyDown(KeyCode.Space)) Respawn();
            if (Input.GetKeyDown(KeyCode.L)) { _loop = !_loop; if (_instance != null) _instance.loop = _loop; Flash(_loop ? "LOOP ON" : "LOOP OFF"); }
            if (Input.GetKeyDown(KeyCode.T)) { SetSurfaceMode(_surfaceMode + 1); Flash(SurfaceName()); }
            if (Input.GetKeyDown(KeyCode.P)) { _paused = !_paused; Flash(_paused ? "PAUSED" : "PLAYING"); }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { _timeScale = Mathf.Max(0.1f, _timeScale * 0.5f); Flash($"SPEED {_timeScale:0.##}X"); }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { _timeScale = Mathf.Min(2f, _timeScale * 2f); Flash($"SPEED {_timeScale:0.##}X"); }
            if (Input.GetKeyDown(KeyCode.DownArrow) && _effect != null) _selEmitter = Math.Min(_effect.emitters.Count - 1, _selEmitter + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow) && _effect != null) _selEmitter = Math.Max(-1, _selEmitter - 1);
            if (Input.GetKeyDown(KeyCode.PageDown) && _effect != null) SelectRelative(1);
            if (Input.GetKeyDown(KeyCode.PageUp) && _effect != null) SelectRelative(-1);
            if (ctrl && Input.GetKeyDown(KeyCode.UpArrow)) MoveEmitter(-1);
            if (ctrl && Input.GetKeyDown(KeyCode.DownArrow)) MoveEmitter(1);

            Time.timeScale = _paused ? 0f : _timeScale;
            UpdateCamera();

            // automatic replay for one-shot effects when looping is off
            if (_instance != null && !_instance.IsAlive) _instance = null;
            if (_instance == null && _effect != null && _loop)
            {
                _replayTimer += Time.unscaledDeltaTime;
                if (_replayTimer > 0.6f) Respawn();
            }
            if (_respawn && !_ui.Dragging) { _respawn = false; Respawn(); }
            if (_instance != null && _instance.follow == null && !IsTrail(_effect) && !IsTracer(_effect))
            {
                // keep a running instance glued to the anchor (it may have been moved by a click while dragging)
                _instance.position = _anchorPos;
            }
        }

        /// <summary>Viewport mouse handling; runs at the end of the UI pass so panel widgets get first pick of the click.</summary>
        private void HandleViewportInput()
        {
            if (_ui.Modal) { _orbiting = false; return; }
            Vector2 mouse = Input.mousePosition;
            bool over = _viewport.Contains(mouse);

            // camera orbit (RMB drag) and zoom (wheel over the viewport)
            if (Input.GetMouseButtonDown(1) && over && !_ui.ClickConsumed) { _orbiting = true; _lastMouse = mouse; }
            if (!Input.GetMouseButton(1)) _orbiting = false;
            if (_orbiting)
            {
                Vector2 d = mouse - _lastMouse; _lastMouse = mouse;
                _yaw += d.x * 0.3f; _pitch = Mathf.Clamp(_pitch - d.y * 0.3f, -80f, 85f);
            }
            if (over && Input.mouseScrollDelta.y != 0f)
                _distance = Mathf.Clamp(_distance * (Input.mouseScrollDelta.y > 0f ? 0.88f : 1.14f), 0.5f, 30f);

            // LMB in the viewport: place the effect on whatever surface is under the cursor
            if (Input.GetMouseButtonDown(0) && over && !_ui.ClickConsumed)
            {
                var win = Application.Current.Window;
                var ray = rig.camera.ScreenPointToRay(mouse, win.Width, win.Height);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    _anchorCustom = true;
                    _anchorPos = hit.point; _anchorNormal = hit.normal;
                    _orbitTarget = hit.point + hit.normal * 0.3f;
                    Respawn();
                }
            }
            UpdateCamera();
        }

        private void SelectRelative(int dir)
        {
            if (_names.Count == 0) return;
            int i = _effect != null ? _names.FindIndex(n => string.Equals(n, _effect.name, StringComparison.OrdinalIgnoreCase)) : -1;
            i = ((i + dir) % _names.Count + _names.Count) % _names.Count;
            Select(_names[i]);
        }

        private void UpdateCamera()
        {
            var cam = rig.camera.transform;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            cam.position = _orbitTarget - rot * Vector3.forward * _distance;
            cam.rotation = rot;
        }

        private string SurfaceName() => _surfaceMode == 1 ? "FLOOR" : _surfaceMode == 2 ? "AIR" : "WALL";
    }
}
