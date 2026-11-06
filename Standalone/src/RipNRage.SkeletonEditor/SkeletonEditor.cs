using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine;
using RipNRage.Engine.Destruction;
using RipNRage.Engine.Graphics;

namespace RipNRage.SkeletonEditor
{
    /// <summary>
    /// Sprite destruction skeleton editor. Left: the skeleton files and the sprite sheets a new one can be made
    /// from. Centre: the reference cell of the current view with the parts drawn over it — polygons are traced
    /// with the mouse, pivots (joints) and vertices dragged, parents picked by clicking. Right: the skeleton, its
    /// views and the selected part's settings. Tab switches to the physics preview, where the sprite is sliced
    /// with the real <see cref="SpriteRagdoll"/> (Space = ragdoll, Ctrl+Space = gib, click a piece to tear it off).
    /// Files are <c>skeletons/*.json</c> (<see cref="SkeletonLibrary"/>); the game reloads saved changes live.
    /// </summary>
    public sealed class SkeletonEditor : Behaviour, IUiDrawer
    {
        public Camera cam;
        public Transform anchor;
        public string startSkeleton;

        // ---- document ----
        private SpriteSkeleton _skel;
        private SkeletonView _view;
        private SkeletonPart _part;
        private readonly List<string> _names = new List<string>();
        private bool _dirty;
        private Texture2D _tex;
        private Sprite _cell;
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();

        // ---- sheets that can become skeletons ----
        private sealed class SheetSource { public string rel; public string label; public int frames = 1, sides = 1; }
        private readonly List<SheetSource> _sources = new List<SheetSource>();
        private int _sourceIndex;

        // ---- view state ----
        private enum Mode { Edit, Preview }
        private Mode _mode = Mode.Edit;
        private enum Tool { Select, Draw, Pivot, Parent }
        private Tool _tool = Tool.Select;
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _fit = true;
        private int _dragVertex = -1;
        private bool _dragPivot, _panning;
        private Vector2 _lastMouse;
        private float _leftScroll, _rightScroll;
        private bool _showHelp;
        private string _status = ""; private float _statusUntil;
        private readonly ImUi _ui = new ImUi();
        private readonly TextPrompt _prompt = new TextPrompt();
        private Rect _topBar, _left, _right, _center, _bottom;

        // ---- preview ----
        private GameObject _previewGo;
        private SpriteRenderer _previewSr;
        private SpriteRagdoll _ragdoll;
        private float _yaw = 180f, _pitch = 12f, _distance = 5.5f;
        private float _previewScale = 0.44f;

        private float S => MathF.Max(1f, _ui.Batch != null ? _ui.Batch.Height / 720f : 1f);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public SpriteSkeleton Skeleton => _skel;
        public bool Dirty => _dirty;

        // ------------------------------------------------------------------ lifecycle

        protected override void Start()
        {
            SkeletonLibrary.SuppressPolling = true;
            RefreshNames();
            ScanSheets();
            BuildPreviewObject();
            if (startSkeleton != null && SkeletonLibrary.Get(startSkeleton) != null) Select(startSkeleton);
            else if (_names.Count > 0) Select(_names[0]);
        }

        protected override void OnDestroy() { SkeletonLibrary.SuppressPolling = false; }

        private void RefreshNames()
        {
            _names.Clear(); _names.AddRange(SkeletonLibrary.Skeletons.Keys); _names.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Every PNG under assets/Sprites (except the HUD) is a candidate sheet.</summary>
        private void ScanSheets()
        {
            _sources.Clear();
            string root = Application.AssetPath("Sprites");
            if (!Directory.Exists(root)) return;
            foreach (var f in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Application.dataPath, f).Replace('\\', '/');
                if (rel.Contains("/HUD/")) continue;
                var src = new SheetSource { rel = rel, label = Path.GetFileNameWithoutExtension(f) };
                // sensible defaults for the known enemy sheets (4 frames x 4 sides grids)
                if (rel.Contains("/Enemies/")) { src.frames = src.label.EndsWith("Idle") || src.label.EndsWith("Dead") ? 1 : 4; src.sides = src.label.EndsWith("Attack") ? 1 : 4; }
                _sources.Add(src);
            }
            _sources.Sort((a, b) => string.Compare(a.rel, b.rel, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildPreviewObject()
        {
            _previewGo = new GameObject("PreviewSprite");
            _previewGo.transform.SetParent(anchor, false);
            _previewSr = _previewGo.AddComponent<SpriteRenderer>();
            _previewSr.billboard = true; _previewSr.lit = true;
            _previewSr.enabled = false;
        }

        // ------------------------------------------------------------------ document

        public void Select(string name)
        {
            var s = SkeletonLibrary.Get(name);
            if (s == null) return;
            _skel = s;
            _view = s.views.Count > 0 ? s.views[0] : null;
            _part = null;
            _undo.Clear(); _redo.Clear();
            _tool = Tool.Select; _fit = true;
            LoadCell();
            ResetPreview();
        }

        private void LoadCell()
        {
            _tex = SkeletonLibrary.TextureOf(_skel);
            _cell = _skel != null && _view != null && _tex != null ? SkeletonLibrary.CellSprite(_skel, _view.side, _view.frame) : null;
            if (_cell != null)
            {
                float cellH = _cell.rect.height / MathF.Max(_cell.pixelsPerUnit, 1f);
                _previewScale = cellH > 0f ? 2.25f / cellH : 1f;
                _previewSr.sprite = _cell;
                _previewGo.transform.localScale = Vector3.one * _previewScale;
                _previewGo.transform.localPosition = new Vector3(0f, cellH * _previewScale * 0.5f, 0f);
            }
        }

        private void Snapshot()
        {
            if (_skel == null) return;
            _undo.Push(JsonSerializer.Serialize(_skel, EngineJson.Options));
            if (_undo.Count > 100) { var arr = _undo.ToArray(); _undo.Clear(); for (int i = Math.Min(arr.Length, 80) - 1; i >= 0; i--) _undo.Push(arr[i]); }
            _redo.Clear();
            _dirty = true;
        }

        private void Restore(string json)
        {
            var s = JsonSerializer.Deserialize<SpriteSkeleton>(json, EngineJson.Options);
            if (s == null) return;
            string viewName = _view?.name, partName = _part?.name;
            SkeletonLibrary.Register(s);
            _skel = s;
            _view = viewName != null ? s.GetView(viewName) : null;
            if (_view == null && s.views.Count > 0) _view = s.views[0];
            _part = _view != null && partName != null ? _view.Find(partName) : null;
            _dirty = true;
            LoadCell();
        }

        private void Undo() { if (_undo.Count == 0) return; _redo.Push(JsonSerializer.Serialize(_skel, EngineJson.Options)); Restore(_undo.Pop()); Status("UNDO"); }
        private void Redo() { if (_redo.Count == 0) return; _undo.Push(JsonSerializer.Serialize(_skel, EngineJson.Options)); Restore(_redo.Pop()); Status("REDO"); }

        private void Save()
        {
            if (_skel == null) return;
            try { SkeletonLibrary.Save(_skel); _dirty = false; RefreshNames(); Status("SAVED " + SkeletonLibrary.FileOf(_skel.name)); }
            catch (Exception e) { Status("SAVE FAILED: " + e.Message); }
        }

        private void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 3f; }

        private void PromptNew()
        {
            if (_sources.Count == 0) { Status("NO SHEETS UNDER assets/Sprites"); return; }
            var src = _sources[Mathf.Clamp(_sourceIndex, 0, _sources.Count - 1)];
            _prompt.Open("NEW SKELETON FROM " + src.label + " — NAME", src.label.ToLowerInvariant().Replace("_idle", "").Replace("_", ""), name =>
            {
                if (string.IsNullOrEmpty(name)) return;
                var s = new SpriteSkeleton { name = name, texture = src.rel, frames = src.frames, sides = src.sides };
                s.views.Add(new SkeletonView { name = "front", side = 0, frame = 0 });
                SkeletonLibrary.Register(s);
                RefreshNames();
                Select(name);
                _dirty = true;
                Status("NEW SKELETON " + name + " — N = NEW PART, TRACE ITS OUTLINE, ENTER = CLOSE");
            });
        }

        private void PromptRename()
        {
            if (_skel == null) return;
            _prompt.Open("RENAME SKELETON", _skel.name, name =>
            {
                if (string.IsNullOrEmpty(name) || name == _skel.name) return;
                SkeletonLibrary.Delete(_skel.name);
                _skel.name = name;
                SkeletonLibrary.Register(_skel);
                RefreshNames(); _dirty = true;
            });
        }

        private void PromptNewPart()
        {
            if (_view == null) return;
            _prompt.Open("NEW PART — NAME", "part" + (_view.parts.Count + 1), name =>
            {
                if (string.IsNullOrEmpty(name)) return;
                Snapshot();
                var p = new SkeletonPart { name = name, parent = _part != null ? _part.name : "", pivot = new Vector2(0.5f, 0.5f) };
                _view.parts.Add(p);
                _part = p;
                _tool = Tool.Draw;
                Status("TRACE THE OUTLINE: CLICK VERTICES, ENTER OR CLICK THE FIRST VERTEX TO CLOSE, BACKSPACE = UNDO VERTEX");
            });
        }

        private void PromptRenamePart()
        {
            if (_part == null) return;
            _prompt.Open("RENAME PART", _part.name, name =>
            {
                if (string.IsNullOrEmpty(name) || name == _part.name) return;
                Snapshot();
                string old = _part.name;
                foreach (var p in _view.parts) if (p.parent == old) p.parent = name;
                _part.name = name;
            });
        }

        private void DeletePart()
        {
            if (_part == null || _view == null) return;
            Snapshot();
            foreach (var p in _view.parts) if (p.parent == _part.name) p.parent = _part.parent;
            _view.parts.Remove(_part);
            _part = null;
        }

        private void AddView()
        {
            if (_skel == null) return;
            Snapshot();
            int side = _view != null ? (_view.side + 1) % Math.Max(1, _skel.sides) : 0;
            var v = new SkeletonView { name = "view" + (_skel.views.Count + 1), side = side, frame = 0 };
            if (_view != null) CopyParts(_view, v);
            _skel.views.Add(v);
            _view = v; _part = null;
            LoadCell();
        }

        private static void CopyParts(SkeletonView from, SkeletonView to)
        {
            to.parts.Clear();
            foreach (var p in from.parts)
            {
                var c = new SkeletonPart { name = p.name, parent = p.parent, pivot = p.pivot, mass = p.mass, shape = p.shape, breakImpulse = p.breakImpulse, loose = p.loose };
                c.polygon.AddRange(p.polygon);
                to.parts.Add(c);
            }
        }

        private void DeleteView()
        {
            if (_skel == null || _view == null || _skel.views.Count <= 1) return;
            Snapshot();
            _skel.views.Remove(_view);
            _view = _skel.views[0]; _part = null;
            LoadCell();
        }

        // ------------------------------------------------------------------ update

        protected override void Update()
        {
            bool wasPrompt = _prompt.Active;
            _prompt.Update();
            _ui.Modal = _prompt.Active;
            if (_prompt.Active || wasPrompt) return;
            HandleKeys();
            if (_mode == Mode.Preview) UpdatePreview();
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.F1)) _showHelp = !_showHelp;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_showHelp) _showHelp = false;
                else if (_tool != Tool.Select) { FinishDraw(); _tool = Tool.Select; }
                else Application.Quit();
            }
            if (Ctrl && Input.GetKeyDown(KeyCode.S)) Save();
            if (Ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (Ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (Ctrl && Input.GetKeyDown(KeyCode.N)) PromptNew();
            if (Input.GetKeyDown(KeyCode.F2)) { if (Shift) PromptRename(); else PromptRenamePart(); }
            if (Input.GetKeyDown(KeyCode.Tab)) SetMode(_mode == Mode.Edit ? Mode.Preview : Mode.Edit);
            if (_skel == null) return;

            if (_mode == Mode.Edit)
            {
                if (Input.GetKeyDown(KeyCode.N) && !Ctrl) PromptNewPart();
                if (Input.GetKeyDown(KeyCode.J)) { _tool = _tool == Tool.Pivot ? Tool.Select : Tool.Pivot; Status(_tool == Tool.Pivot ? "CLICK WHERE THE PART HANGS OFF ITS PARENT" : ""); }
                if (Input.GetKeyDown(KeyCode.P)) { _tool = _tool == Tool.Parent ? Tool.Select : Tool.Parent; Status(_tool == Tool.Parent ? "CLICK THE PARENT PART (EMPTY SPACE = NO PARENT)" : ""); }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { if (_tool == Tool.Draw) { FinishDraw(); _tool = Tool.Select; } }
                if (Input.GetKeyDown(KeyCode.Backspace) && _tool == Tool.Draw && _part != null && _part.polygon.Count > 0) { _part.polygon.RemoveAt(_part.polygon.Count - 1); _dirty = true; }
                if (Input.GetKeyDown(KeyCode.Delete)) DeleteHoveredOrPart();
                if (Input.GetKeyDown(KeyCode.I)) InsertVertexAtMouse();
                if (Input.GetKeyDown(KeyCode.F)) { _fit = true; }
                if (Input.GetKeyDown(KeyCode.L) && _part != null) { Snapshot(); _part.loose = !_part.loose; }
                if (Input.GetKeyDown(KeyCode.LeftBracket) && _view != null) { Snapshot(); _view.frame = Math.Max(0, _view.frame - 1); LoadCell(); }
                if (Input.GetKeyDown(KeyCode.RightBracket) && _view != null) { Snapshot(); _view.frame = Math.Min(Math.Max(0, _skel.frames - 1), _view.frame + 1); LoadCell(); }
                if (Input.GetKeyDown(KeyCode.Comma) && _view != null) { Snapshot(); _view.side = (_view.side + Math.Max(1, _skel.sides) - 1) % Math.Max(1, _skel.sides); LoadCell(); }
                if (Input.GetKeyDown(KeyCode.Period) && _view != null) { Snapshot(); _view.side = (_view.side + 1) % Math.Max(1, _skel.sides); LoadCell(); }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Space)) SpawnRagdoll(Ctrl);
                if (Input.GetKeyDown(KeyCode.R)) ResetPreview();
                if (Input.GetKeyDown(KeyCode.K) && _ragdoll != null) _ragdoll.BreakAll();
                if (Input.GetKeyDown(KeyCode.UpArrow) && _ragdoll != null) _ragdoll.AddExplosion(40f, anchor.position + new Vector3(0f, -0.2f, 0.8f), 4f, 0.6f);
            }
        }

        private void SetMode(Mode m)
        {
            if (_tool == Tool.Draw) FinishDraw();
            _tool = Tool.Select;
            _mode = m;
            if (m == Mode.Preview) { _previewSr.enabled = _ragdoll == null && _cell != null; }
            else { _previewSr.enabled = false; }
        }

        private void FinishDraw()
        {
            if (_part != null && _part.polygon.Count < 3) { _part.polygon.Clear(); Status("POLYGON NEEDS 3+ VERTICES — PART LEFT EMPTY"); }
            else if (_part != null && _part.polygon.Count >= 3)
            {
                // default pivot: top centre of the part (where a limb usually hangs) when it was never moved
                if (_part.pivot == new Vector2(0.5f, 0.5f))
                {
                    Rect b = _part.Bounds();
                    _part.pivot = new Vector2(b.x + b.width * 0.5f, b.yMax);
                }
            }
        }

        private void DeleteHoveredOrPart()
        {
            if (_part == null) return;
            int v = HoveredVertex(_part);
            if (v >= 0) { Snapshot(); _part.polygon.RemoveAt(v); return; }
            DeletePart();
        }

        private void InsertVertexAtMouse()
        {
            if (_part == null || _part.polygon.Count < 2 || !_center.Contains(Input.mousePosition)) return;
            Vector2 m = ScreenToCell(Input.mousePosition);
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < _part.polygon.Count; i++)
            {
                Vector2 a = _part.polygon[i], b = _part.polygon[(i + 1) % _part.polygon.Count];
                float d = DistanceToSegment(m, a, b);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            Snapshot();
            _part.polygon.Insert(best + 1, m);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1E-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        // ------------------------------------------------------------------ preview

        private void ResetPreview()
        {
            if (_ragdoll != null) { GameObject.Destroy(_ragdoll.gameObject); _ragdoll = null; }
            if (_previewSr != null) _previewSr.enabled = _mode == Mode.Preview && _cell != null;
        }

        private void SpawnRagdoll(bool gib)
        {
            if (_skel == null || _cell == null) return;
            ResetPreview();
            var options = RagdollOptions.Default;
            options.gib = gib;
            options.scatterImpulse = gib ? 6f : 0f;
            options.lifetime = 0f;
            // a shove through the torso from the camera side, like a shot
            Vector3 from = cam.transform.position;
            Vector3 hit = _previewGo.transform.position;
            options.impulse = (hit - from).normalized * (gib ? 0f : 12f);
            options.impulsePoint = hit + Vector3.up * 0.3f;
            _ragdoll = SpriteRagdoll.Spawn(_skel, _cell, _previewGo.transform.position, _previewGo.transform.lossyScale, options, _previewSr);
            if (_ragdoll == null) { Status("NOTHING TO SLICE: THE VIEW HAS NO PARTS WITH 3+ VERTICES"); return; }
            _ragdoll.lifetime = 0f;
            _previewSr.enabled = false;
            Status(gib ? "GIBBED — R = RESET" : "RAGDOLL — CLICK A PIECE TO TEAR IT OFF, K = BREAK ALL, UP = BLAST, R = RESET");
        }

        private void UpdatePreview()
        {
            // orbit with the right mouse button, zoom with the wheel, tear pieces off with the left button
            Vector2 mouse = Input.mousePosition;
            bool over = _center.Contains(mouse) && !_ui.ClickConsumed;
            if (Input.GetMouseButton(1) && over)
            {
                Vector2 d = mouse - _lastMouse;
                _yaw += d.x * 0.3f; _pitch = Mathf.Clamp(_pitch - d.y * 0.3f, -20f, 80f);
            }
            _lastMouse = mouse;
            if (over && Input.mouseScrollDelta.y != 0f) _distance = Mathf.Clamp(_distance * (Input.mouseScrollDelta.y > 0f ? 0.9f : 1.1f), 1.5f, 20f);
            Vector3 target = anchor.position + Vector3.up * 1f;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            cam.transform.position = target - rot * Vector3.forward * _distance;
            cam.transform.rotation = rot;

            if (over && Input.GetMouseButtonDown(0) && _ragdoll != null)
            {
                var win = Application.Current.Window;
                var ray = cam.ScreenPointToRay(mouse, win.Width, win.Height);
                if (Physics.Raycast(ray, out RaycastHit h, 100f))
                {
                    foreach (var p in _ragdoll.parts)
                        if (p.collider == h.collider)
                        {
                            if (p.attached) { _ragdoll.Detach(p); Status("TORE OFF " + p.name); }
                            p.body.AddImpulseAtPosition(ray.direction * 6f, h.point);
                            break;
                        }
                }
            }
        }

        // ------------------------------------------------------------------ ui

        public void DrawUi(UiBatch ui)
        {
            _ui.BeginFrame(ui);
            _ui.BeginWidgets();
            _ui.FontScale = MathF.Max(1.5f, ui.Scale * 2.25f);
            float s = S;
            float topH = 30f * s, bottomH = 26f * s, leftW = 280f * s, rightW = 330f * s;
            _topBar = new Rect(0, ui.Height - topH, ui.Width, topH);
            _bottom = new Rect(0, 0, ui.Width, bottomH);
            _left = new Rect(0, bottomH, leftW, ui.Height - bottomH - topH);
            _right = new Rect(ui.Width - rightW, bottomH, rightW, ui.Height - bottomH - topH);
            _center = new Rect(leftW, bottomH, ui.Width - leftW - rightW, ui.Height - bottomH - topH);

            if (_mode == Mode.Edit) DrawCellEditor();
            DrawTopBar();
            DrawLeftPanel();
            DrawRightPanel();
            DrawBottomBar();
            if (_showHelp) DrawHelp();
            _prompt.Draw(_ui, new Rect(_center.x + 20f * s, _center.yMax - 90f * s, _center.width - 40f * s, 70f * s));
            if (!Input.GetMouseButton(0)) { _dragVertex = -1; _dragPivot = false; }
            if (!Input.GetMouseButton(1) && !Input.GetMouseButton(2)) _panning = false;
        }

        private void DrawTopBar()
        {
            var r = _topBar;
            _ui.Batch.FillRect(r, ImUi.PanelBg);
            _ui.Batch.FillRect(new Rect(r.x, r.y, r.width, 1), ImUi.PanelBorder);
            float y = r.yMax - 8f * S;
            string title = "RIP-N-RAGE SKELETON EDITOR";
            _ui.Label(title, new Vector2(8f, y), ImUi.Accent);
            float x = 8f + _ui.TextWidth(title) + 24f;
            string doc = _skel != null ? $"{_skel.name}{(_dirty ? "*" : "")}  VIEW {_view?.name ?? "-"} (SIDE {_view?.side ?? 0}, FRAME {_view?.frame ?? 0})  {(_mode == Mode.Edit ? "EDIT" : "PHYSICS PREVIEW")}  TOOL {_tool.ToString().ToUpperInvariant()}" : "NO SKELETON — CTRL+N";
            _ui.Label(doc, new Vector2(x, y), ImUi.Text);
            string right = $"{(SkeletonLibrary.Directory != null ? Path.GetFileName(SkeletonLibrary.Directory) + "/" : "NO FOLDER")}  TAB PREVIEW  F1 HELP  CTRL+S SAVE";
            _ui.Label(right, new Vector2(r.xMax - _ui.TextWidth(right) - 8f, y), ImUi.TextDim);
        }

        private void DrawBottomBar()
        {
            var r = _bottom;
            _ui.Batch.FillRect(r, ImUi.PanelBg);
            _ui.Batch.FillRect(new Rect(r.x, r.yMax - 1, r.width, 1), ImUi.PanelBorder);
            string text = Time.unscaledTime < _statusUntil ? _status :
                _mode == Mode.Preview ? "SPACE RAGDOLL  CTRL+SPACE GIB  CLICK A PIECE = TEAR OFF  K BREAK ALL  UP BLAST  R RESET  RMB ORBIT  WHEEL ZOOM" :
                _tool == Tool.Draw ? "DRAWING: CLICK = VERTEX  ENTER / FIRST VERTEX = CLOSE  BACKSPACE = UNDO VERTEX  ESC = STOP" :
                "N NEW PART  CLICK = SELECT / DRAG VERTEX OR PIVOT  J PIVOT  P PARENT  I INSERT VERTEX  DEL DELETE  L LOOSE  [ ] FRAME  , . SIDE  WHEEL ZOOM  MMB PAN  F FIT";
            _ui.Label(_ui.Fit(text, r.width - 16f), new Vector2(8f, r.yMax - 6f * S), Time.unscaledTime < _statusUntil ? ImUi.Accent : ImUi.TextDim);
        }

        // ---- left: skeletons + sheets ----

        private void DrawLeftPanel()
        {
            var r = _left;
            _ui.Panel(r, "SKELETONS");
            float rowH = _ui.LineHeight;
            float y = r.yMax - rowH - 8f * S;
            float x = r.x + 6f, w = r.width - 12f;
            if (_ui.Button(new Rect(x, y - rowH + 4f, w * 0.48f, rowH - 2f), "+ NEW (CTRL+N)")) PromptNew();
            if (_ui.Button(new Rect(x + w * 0.52f, y - rowH + 4f, w * 0.48f, rowH - 2f), _dirty ? "SAVE*" : "SAVE", _dirty)) Save();
            y -= rowH + 6f;
            int shown = Math.Min(_names.Count, 8);
            for (int i = 0; i < shown; i++)
            {
                string n = _names[i];
                int click = _ui.Row(new Rect(x, y - rowH, w, rowH), n, _skel != null && _skel.name == n);
                if (click == 1) Select(n);
                y -= rowH;
            }
            if (_names.Count > shown) { _ui.Label($"... {_names.Count - shown} MORE", new Vector2(x + 4f, y), ImUi.TextDim); y -= rowH; }

            y -= 8f;
            _ui.Label("SHEETS (NEW SKELETON SOURCE)", new Vector2(x, y), ImUi.Accent); y -= rowH;
            _ui.Label("CLICK = PICK, FRAMES / SIDES: CLICK +, RMB -", new Vector2(x, y), ImUi.TextDim); y -= rowH;
            var list = new Rect(r.x, r.y + 4f, r.width, y - r.y - 4f);
            float content = _sources.Count * rowH;
            _leftScroll = _ui.ScrollArea(list, _leftScroll, content);
            _ui.Batch.PushClip(list);
            float yy = list.yMax + _leftScroll;
            for (int i = 0; i < _sources.Count; i++)
            {
                var src = _sources[i];
                var row = new Rect(x, yy - rowH, w - 110f * S, rowH);
                if (row.yMax >= list.y && row.y <= list.yMax)
                {
                    int click = _ui.Row(row, src.label, i == _sourceIndex);
                    if (click == 1) _sourceIndex = i;
                    int c1 = _ui.Cell(new Rect(row.xMax + 4f, yy - rowH + 2f, 50f * S, rowH - 4f), "F" + src.frames);
                    if (c1 != 0) src.frames = Mathf.Clamp(src.frames + c1, 1, 32);
                    int c2 = _ui.Cell(new Rect(row.xMax + 58f * S, yy - rowH + 2f, 50f * S, rowH - 4f), "S" + src.sides);
                    if (c2 != 0) src.sides = Mathf.Clamp(src.sides + c2, 1, 16);
                }
                yy -= rowH;
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(list, _leftScroll, content);
        }

        // ---- right: skeleton / views / parts ----

        private void DrawRightPanel()
        {
            var r = _right;
            _ui.Panel(r, "SKELETON");
            if (_skel == null) return;
            float rowH = _ui.LineHeight;
            float x = r.x + 6f, w = r.width - 12f;
            float y = r.yMax - rowH - 8f * S;
            float half = w * 0.5f - 2f;

            _ui.Label(_ui.Fit("SHEET " + _skel.texture, w), new Vector2(x, y), ImUi.TextDim); y -= rowH;
            int c;
            c = _ui.Cell(new Rect(x, y - rowH + 2f, half, rowH - 4f), "FRAMES " + _skel.frames); if (c != 0) { Snapshot(); _skel.frames = Mathf.Clamp(_skel.frames + c, 1, 32); LoadCell(); }
            c = _ui.Cell(new Rect(x + half + 4f, y - rowH + 2f, half, rowH - 4f), "SIDES " + _skel.sides); if (c != 0) { Snapshot(); _skel.sides = Mathf.Clamp(_skel.sides + c, 1, 16); LoadCell(); }
            y -= rowH;
            float v;
            v = _ui.DragValue(new Rect(x, y - rowH + 2f, half, rowH - 4f), "PPU", _skel.pixelsPerUnit, 1f, 1f, 1000f, 100f, "0"); if (v != _skel.pixelsPerUnit) { _skel.pixelsPerUnit = v; _dirty = true; LoadCell(); }
            v = _ui.DragValue(new Rect(x + half + 4f, y - rowH + 2f, half, rowH - 4f), "DEPTH", _skel.depth, 0.01f, 0.02f, 2f, 0.15f, "0.00"); if (v != _skel.depth) { _skel.depth = v; _dirty = true; }
            y -= rowH;
            v = _ui.DragValue(new Rect(x, y - rowH + 2f, half, rowH - 4f), "LIFE", _skel.lifetime, 1f, 0f, 600f, 20f, "0"); if (v != _skel.lifetime) { _skel.lifetime = v; _dirty = true; }
            if (_ui.Button(new Rect(x + half + 4f, y - rowH + 2f, half, rowH - 4f), "RENAME (SHIFT+F2)")) PromptRename();
            y -= rowH + 6f;

            // views
            _ui.Label("VIEWS (SIDE / REF FRAME)", new Vector2(x, y), ImUi.Accent);
            if (_ui.Button(new Rect(r.xMax - 6f - 120f * S, y - rowH + 6f, 56f * S, rowH - 4f), "+ VIEW")) AddView();
            if (_ui.Button(new Rect(r.xMax - 6f - 60f * S, y - rowH + 6f, 54f * S, rowH - 4f), "- VIEW", false, null, _skel.views.Count > 1)) DeleteView();
            y -= rowH;
            foreach (var view in _skel.views)
            {
                var row = new Rect(x, y - rowH, w - 120f * S, rowH);
                int click = _ui.Row(row, view.name, view == _view, null, $"{view.parts.Count} PARTS");
                if (click == 1) { _view = view; _part = null; _tool = Tool.Select; LoadCell(); }
                if (click == 2) { _prompt.Open("RENAME VIEW", view.name, n => { if (!string.IsNullOrEmpty(n)) { Snapshot(); view.name = n; } }); }
                c = _ui.Cell(new Rect(row.xMax + 4f, y - rowH + 2f, 56f * S, rowH - 4f), "SIDE " + view.side);
                if (c != 0) { Snapshot(); view.side = ((view.side + c) % Math.Max(1, _skel.sides) + Math.Max(1, _skel.sides)) % Math.Max(1, _skel.sides); if (view == _view) LoadCell(); }
                c = _ui.Cell(new Rect(row.xMax + 64f * S, y - rowH + 2f, 52f * S, rowH - 4f), "FR " + view.frame);
                if (c != 0) { Snapshot(); view.frame = Mathf.Clamp(view.frame + c, 0, Math.Max(0, _skel.frames - 1)); if (view == _view) LoadCell(); }
                y -= rowH;
                if (y < r.y + 260f * S) break;
            }
            if (_view != null && _skel.views.Count > 1 && _ui.Button(new Rect(x, y - rowH + 4f, w, rowH - 4f), "COPY PARTS FROM THE NEXT VIEW INTO THIS ONE"))
            {
                int i = _skel.views.IndexOf(_view);
                var from = _skel.views[(i + 1) % _skel.views.Count];
                Snapshot(); CopyParts(from, _view); _part = null;
            }
            y -= rowH + 6f;

            // parts
            _ui.Label("PARTS (CLICK = SELECT, RMB = RENAME)", new Vector2(x, y), ImUi.Accent);
            if (_ui.Button(new Rect(r.xMax - 6f - 70f * S, y - rowH + 6f, 64f * S, rowH - 4f), "+ PART (N)")) PromptNewPart();
            y -= rowH;
            if (_view != null)
            {
                var list = new Rect(r.x, r.y + 150f * S, r.width, Math.Max(0f, y - r.y - 150f * S));
                float content = _view.parts.Count * rowH;
                _rightScroll = _ui.ScrollArea(list, _rightScroll, content);
                _ui.Batch.PushClip(list);
                float yy = list.yMax + _rightScroll;
                foreach (var p in _view.parts)
                {
                    string label = (string.IsNullOrEmpty(p.parent) ? "" : "  ") + p.name + (p.loose ? " (LOOSE)" : "");
                    int click = _ui.Row(new Rect(x, yy - rowH, w, rowH), label, p == _part, p.polygon.Count < 3 ? ImUi.Danger : null,
                        string.IsNullOrEmpty(p.parent) ? "ROOT" : "< " + p.parent);
                    if (click == 1) { _part = p; if (_tool == Tool.Draw) _tool = Tool.Select; }
                    if (click == 2) { _part = p; PromptRenamePart(); }
                    yy -= rowH;
                }
                _ui.Batch.PopClip();
                _ui.ScrollBar(list, _rightScroll, content);
                y = list.y - 4f;
            }

            // selected part
            if (_part != null)
            {
                _ui.Label(_ui.Fit("PART " + _part.name + $"  ({_part.polygon.Count} VERTICES)", w), new Vector2(x, y), ImUi.Accent); y -= rowH;
                c = _ui.Cell(new Rect(x, y - rowH + 2f, w, rowH - 4f), "PARENT: " + (string.IsNullOrEmpty(_part.parent) ? "NONE (ROOT)" : _part.parent) + "  (CLICK CYCLES, P = PICK)");
                if (c != 0) { Snapshot(); CycleParent(c); }
                y -= rowH;
                v = _ui.DragValue(new Rect(x, y - rowH + 2f, half, rowH - 4f), "MASS", _part.mass, 0.1f, 0.05f, 500f, 2f, "0.0"); if (v != _part.mass) { _part.mass = v; _dirty = true; }
                v = _ui.DragValue(new Rect(x + half + 4f, y - rowH + 2f, half, rowH - 4f), "BREAK", _part.breakImpulse, 1f, 0f, 1000f, 0f, "0"); if (v != _part.breakImpulse) { _part.breakImpulse = v; _dirty = true; }
                y -= rowH;
                if (_ui.Button(new Rect(x, y - rowH + 2f, half, rowH - 4f), "SHAPE: " + _part.shape.ToString().ToUpperInvariant())) { Snapshot(); _part.shape = _part.shape == PartShape.Box ? PartShape.Capsule : PartShape.Box; }
                bool loose = _ui.Toggle(new Rect(x + half + 4f, y - rowH + 2f, half, rowH - 4f), "LOOSE (L)", _part.loose); if (loose != _part.loose) { Snapshot(); _part.loose = loose; }
                y -= rowH;
                _ui.Label($"PIVOT {_part.pivot.x:0.000}, {_part.pivot.y:0.000}  (J = PLACE)", new Vector2(x, y), ImUi.TextDim); y -= rowH;
                if (_ui.Button(new Rect(x, y - rowH + 2f, w, rowH - 4f), "DELETE PART (DEL)", false, ImUi.Danger)) DeletePart();
            }
        }

        private void CycleParent(int dir)
        {
            if (_view == null || _part == null) return;
            var options = new List<string> { "" };
            foreach (var p in _view.parts) if (p != _part && !IsDescendant(p, _part)) options.Add(p.name);
            int i = options.IndexOf(_part.parent ?? "");
            if (i < 0) i = 0;
            i = ((i + dir) % options.Count + options.Count) % options.Count;
            _part.parent = options[i];
        }

        private bool IsDescendant(SkeletonPart candidate, SkeletonPart ancestor)
        {
            var p = candidate; int guard = 0;
            while (p != null && guard++ < 64)
            {
                if (p == ancestor) return true;
                p = string.IsNullOrEmpty(p.parent) ? null : _view.Find(p.parent);
            }
            return false;
        }

        // ---- centre: the cell with the parts over it ----

        private Rect _cellRect; // screen rect of the cell image

        private Vector2 CellToScreen(Vector2 n) => new Vector2(_cellRect.x + n.x * _cellRect.width, _cellRect.y + n.y * _cellRect.height);
        private Vector2 ScreenToCell(Vector2 p) => new Vector2((p.x - _cellRect.x) / MathF.Max(_cellRect.width, 1f), (p.y - _cellRect.y) / MathF.Max(_cellRect.height, 1f));

        private static readonly Color[] PartColors =
        {
            new Color(1f, 0.5f, 0.2f, 1f), new Color(0.3f, 0.8f, 1f, 1f), new Color(0.5f, 1f, 0.4f, 1f), new Color(1f, 0.4f, 0.8f, 1f),
            new Color(1f, 0.9f, 0.3f, 1f), new Color(0.6f, 0.5f, 1f, 1f), new Color(0.4f, 1f, 0.9f, 1f), new Color(1f, 0.7f, 0.6f, 1f),
        };

        private void DrawCellEditor()
        {
            var r = _center;
            // checkerboard backdrop
            _ui.Batch.FillRect(r, new Color(0.16f, 0.17f, 0.2f, 1f));
            if (_cell == null || _tex == null)
            {
                _ui.Label(_skel == null ? "CTRL+N: NEW SKELETON FROM A SHEET" : "NO TEXTURE: " + (_skel.texture ?? ""), new Vector2(r.x + 20f, r.center.y), ImUi.TextDim);
                return;
            }
            Vector2 mouse = Input.mousePosition;
            bool over = r.Contains(mouse) && !_ui.Modal;

            // zoom / pan
            float cellW = _cell.rect.width, cellH = _cell.rect.height;
            if (_fit)
            {
                float fitZoom = MathF.Min((r.width - 40f) / cellW, (r.height - 40f) / cellH);
                _zoom = MathF.Max(0.05f, fitZoom);
                _pan = Vector2.zero;
                _fit = false;
            }
            if (over && Input.mouseScrollDelta.y != 0f && !_ui.ClickConsumed)
            {
                float factor = Input.mouseScrollDelta.y > 0f ? 1.15f : 1f / 1.15f;
                Vector2 before = ScreenToCell(mouse);
                _zoom = Mathf.Clamp(_zoom * factor, 0.05f, 40f);
                ComputeCellRect(r, cellW, cellH);
                Vector2 after = ScreenToCell(mouse);
                _pan += new Vector2((after.x - before.x) * _cellRect.width, (after.y - before.y) * _cellRect.height);
            }
            if (over && (Input.GetMouseButtonDown(2) || (Input.GetMouseButtonDown(1) && _tool == Tool.Select))) { _panning = true; _lastMouse = mouse; }
            if (_panning) { _pan += mouse - _lastMouse; _lastMouse = mouse; }
            ComputeCellRect(r, cellW, cellH);

            // the cell
            _ui.Batch.PushClip(r);
            _cell.GetUVs(out float u0, out float v0, out float u1, out float v1);
            DrawChecker(_cellRect);
            _ui.Batch.DrawRaw(_tex, _cellRect, u0, v0, u1, v1, Color.white);
            _ui.Border(_cellRect, ImUi.PanelBorder);

            // parts
            if (_view != null)
            {
                for (int i = 0; i < _view.parts.Count; i++)
                {
                    var p = _view.parts[i];
                    bool sel = p == _part;
                    Color col = PartColors[i % PartColors.Length];
                    if (!sel) col = new Color(col.r, col.g, col.b, 0.75f);
                    float t = sel ? 3f : 1.5f;
                    for (int k = 0; k < p.polygon.Count; k++)
                    {
                        Vector2 a = CellToScreen(p.polygon[k]);
                        if (p.polygon.Count >= 2 && (k + 1 < p.polygon.Count || _tool != Tool.Draw || !sel))
                        {
                            Vector2 b = CellToScreen(p.polygon[(k + 1) % p.polygon.Count]);
                            _ui.Batch.DrawLine(a, b, t, col);
                        }
                        float vs = (sel ? 5f : 3f) * S;
                        _ui.Batch.FillRect(new Rect(a.x - vs, a.y - vs, vs * 2f, vs * 2f), col);
                    }
                    // pivot + link to the parent's pivot
                    Vector2 pv = CellToScreen(p.pivot);
                    float ps = (sel ? 7f : 5f) * S;
                    _ui.Batch.FillRect(new Rect(pv.x - ps, pv.y - 1f, ps * 2f, 2f), Color.white);
                    _ui.Batch.FillRect(new Rect(pv.x - 1f, pv.y - ps, 2f, ps * 2f), Color.white);
                    var parent = string.IsNullOrEmpty(p.parent) ? null : _view.Find(p.parent);
                    if (parent != null)
                    {
                        Vector2 pc = parent.polygon.Count >= 3 ? CellToScreen(Sprite.Centroid(parent.PolygonArray())) : CellToScreen(parent.pivot);
                        _ui.Batch.DrawLine(pv, pc, 1f, new Color(1f, 1f, 1f, 0.45f));
                    }
                    Vector2 labelAt = p.polygon.Count >= 3 ? CellToScreen(Sprite.Centroid(p.PolygonArray())) : pv + new Vector2(8f, 8f);
                    _ui.Label(p.name, labelAt, sel ? ImUi.Accent : col, _ui.FontScale * 0.8f);
                }
                // the edge being drawn
                if (_tool == Tool.Draw && _part != null && _part.polygon.Count > 0 && over)
                    _ui.Batch.DrawLine(CellToScreen(_part.polygon[_part.polygon.Count - 1]), mouse, 1.5f, new Color(1f, 1f, 1f, 0.6f));
            }
            _ui.Batch.PopClip();

            if (over && !_ui.ClickConsumed) HandleCellMouse(mouse);
        }

        private void ComputeCellRect(Rect r, float cellW, float cellH)
        {
            float w = cellW * _zoom, h = cellH * _zoom;
            _cellRect = new Rect(r.center.x - w * 0.5f + _pan.x, r.center.y - h * 0.5f + _pan.y, w, h);
        }

        private void DrawChecker(Rect rect)
        {
            float cell = 32f * S;
            var clip = rect;
            int nx = (int)MathF.Ceiling(rect.width / cell), ny = (int)MathF.Ceiling(rect.height / cell);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    float x = rect.x + i * cell, y = rect.y + j * cell;
                    float w = MathF.Min(cell, rect.xMax - x), h = MathF.Min(cell, rect.yMax - y);
                    _ui.Batch.FillRect(new Rect(x, y, w, h), (i + j) % 2 == 0 ? new Color(0.3f, 0.3f, 0.33f, 1f) : new Color(0.38f, 0.38f, 0.42f, 1f));
                }
        }

        private int HoveredVertex(SkeletonPart p)
        {
            if (p == null) return -1;
            Vector2 mouse = Input.mousePosition;
            float best = 9f * S; int bi = -1;
            for (int i = 0; i < p.polygon.Count; i++)
            {
                float d = (CellToScreen(p.polygon[i]) - mouse).magnitude;
                if (d < best) { best = d; bi = i; }
            }
            return bi;
        }

        private SkeletonPart PartAt(Vector2 cellPoint)
        {
            if (_view == null) return null;
            // smallest polygon containing the point wins (limbs over the torso)
            SkeletonPart best = null; float bestArea = float.MaxValue;
            foreach (var p in _view.parts)
            {
                if (p.polygon.Count < 3 || !PointInPolygon(cellPoint, p.polygon)) continue;
                Rect b = p.Bounds();
                float area = b.width * b.height;
                if (area < bestArea) { bestArea = area; best = p; }
            }
            return best;
        }

        private static bool PointInPolygon(Vector2 q, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private void HandleCellMouse(Vector2 mouse)
        {
            Vector2 cp = ScreenToCell(mouse);
            bool down = Input.GetMouseButtonDown(0);
            bool held = Input.GetMouseButton(0);

            switch (_tool)
            {
                case Tool.Draw:
                    if (down && _part != null)
                    {
                        // clicking the first vertex closes the outline
                        if (_part.polygon.Count >= 3 && (CellToScreen(_part.polygon[0]) - mouse).magnitude < 10f * S) { FinishDraw(); _tool = Tool.Select; Status("OUTLINE CLOSED — J PLACES THE PIVOT, P PICKS THE PARENT"); }
                        else { Snapshot(); _part.polygon.Add(cp); }
                    }
                    break;
                case Tool.Pivot:
                    if (down && _part != null) { Snapshot(); _part.pivot = cp; _tool = Tool.Select; Status("PIVOT PLACED"); }
                    break;
                case Tool.Parent:
                    if (down && _part != null)
                    {
                        var target = PartAt(cp);
                        Snapshot();
                        if (target == null || target == _part) _part.parent = "";
                        else if (IsDescendant(target, _part)) Status("CANNOT PARENT TO A DESCENDANT");
                        else _part.parent = target.name;
                        _tool = Tool.Select;
                    }
                    break;
                default:
                    if (down)
                    {
                        int vtx = HoveredVertex(_part);
                        if (vtx >= 0) { Snapshot(); _dragVertex = vtx; }
                        else if (_part != null && (CellToScreen(_part.pivot) - mouse).magnitude < 10f * S) { Snapshot(); _dragPivot = true; }
                        else
                        {
                            var hit = PartAt(cp);
                            if (hit != null) { _part = hit; int v2 = HoveredVertex(_part); if (v2 >= 0) { Snapshot(); _dragVertex = v2; } }
                            else _part = null;
                        }
                    }
                    if (held && _part != null)
                    {
                        if (_dragVertex >= 0 && _dragVertex < _part.polygon.Count) { _part.polygon[_dragVertex] = Clamp01(cp); _dirty = true; }
                        else if (_dragPivot) { _part.pivot = Clamp01(cp); _dirty = true; }
                    }
                    break;
            }
        }

        private static Vector2 Clamp01(Vector2 v) => new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));

        private void DrawHelp()
        {
            float s = S;
            var r = new Rect(_center.x + 30f * s, _center.y + 30f * s, _center.width - 60f * s, _center.height - 60f * s);
            _ui.Panel(r, "HELP (F1)");
            float y = r.yMax - _ui.LineHeight - 10f * s, x = r.x + 12f * s;
            string[] lines =
            {
                "A SKELETON SAYS HOW A SPRITE IS SLICED WHEN IT DIES: PARTS (POLYGONS), THEIR PIVOTS (JOINTS) AND PARENTS.",
                "IT IS NOT USED FOR ANIMATION. COORDINATES ARE NORMALISED INSIDE A SHEET CELL, ONE VIEW PER SIDE.",
                "",
                "CTRL+N NEW SKELETON FROM THE SELECTED SHEET (LEFT)     CTRL+S SAVE     CTRL+Z / CTRL+Y UNDO / REDO",
                "N NEW PART: TYPE A NAME, THEN CLICK THE OUTLINE; ENTER OR THE FIRST VERTEX CLOSES IT (BACKSPACE UNDOES A VERTEX)",
                "CLICK A PART TO SELECT, DRAG ITS VERTICES / PIVOT, I INSERTS A VERTEX ON THE NEAREST EDGE, DEL DELETES VERTEX / PART",
                "J PLACE THE PIVOT (WHERE THE PART HANGS OFF ITS PARENT)    P PICK THE PARENT BY CLICKING IT    L LOOSE (STARTS DETACHED)",
                "RIGHT PANEL: MASS, BREAK IMPULSE (0 = NEVER TEARS), SHAPE BOX / CAPSULE, PARENT CYCLE; VIEWS PER SHEET SIDE ( , . [ ] )",
                "WHEEL ZOOM, MIDDLE / RIGHT DRAG PAN, F FIT",
                "",
                "TAB: PHYSICS PREVIEW — SPACE SLICES THE SPRITE INTO A RAGDOLL WITH THE REAL SOLVER, CTRL+SPACE GIBS IT,",
                "CLICK A PIECE TO TEAR IT OFF, K BREAKS EVERY JOINT, UP ARROW SETS OFF A BLAST, R RESETS, RMB ORBITS, WHEEL ZOOMS.",
                "",
                "THE GAME RELOADS SAVED SKELETONS LIVE; ENEMYDATA.SKELETON NAMES THE ONE AN ENEMY USES.",
            };
            foreach (var l in lines) { _ui.Label(_ui.Fit(l, r.width - 24f * s), new Vector2(x, y), l.Length == 0 ? ImUi.TextDim : ImUi.Text); y -= _ui.LineHeight; }
        }
    }
}
