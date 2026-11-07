using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine;
using RipNRage.Engine.Destruction;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Platform;
using RipNRage.ImGuiIntegration;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

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
    public sealed class SkeletonEditor : Behaviour
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
        private ImGuiController _imgui;
        private ImGuiViewportPanel _panel;       // the canvas is rendered into this dockable panel
        private Rect _center;                    // canvas rectangle, in the panel's own pixels (origin bottom-left)
        private bool _viewportHovered;
        private UiBatch _batch;                  // the batch rendering into the panel this frame

        // modal text prompt (new / rename), driven by an ImGui popup
        private string _promptTitle, _promptText;
        private Action<string> _promptAccept;
        private bool _promptOpen, _promptFocus;
        private bool _layoutBuilt;
        private bool _userMovedView;   // the view was zoomed or panned by hand: stop auto-fitting on resize

        // ---- preview ----
        private GameObject _previewGo;
        private SpriteRenderer _previewSr;
        private SpriteRagdoll _ragdoll;
        private float _yaw = 180f, _pitch = 12f, _distance = 5.5f;
        private float _previewScale = 0.44f;

        /// <summary>Canvas scale: handles and line widths are in pixels, so they track the framebuffer size.</summary>
        private float S => MathF.Max(1f, _imgui != null ? _imgui.PixelRatio : 1f);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public SpriteSkeleton Skeleton => _skel;
        public bool Dirty => _dirty;

        // ------------------------------------------------------------------ lifecycle

        protected override void Start()
        {
            SkeletonLibrary.SuppressPolling = true;
            _imgui = new ImGuiController(layoutName: "skeleton-editor");
            _panel = new ImGuiViewportPanel("Viewport");
            Application.Current.Renderer.AfterUi += OnAfterUi;
            RefreshNames();
            ScanSheets();
            BuildPreviewObject();
            if (startSkeleton != null && SkeletonLibrary.Get(startSkeleton) != null) Select(startSkeleton);
            else if (_names.Count > 0) Select(_names[0]);
        }

        protected override void OnDestroy()
        {
            SkeletonLibrary.SuppressPolling = false;
            if (Application.Current?.Renderer != null) Application.Current.Renderer.AfterUi -= OnAfterUi;
            _panel?.Dispose();
            _panel = null;
            _imgui?.Dispose();
            _imgui = null;
        }

        /// <summary>
        /// After the engine's UI pass: the canvas is rendered into the viewport panel's own framebuffer, then Dear
        /// ImGui draws the panels on top (see RenderPipeline.AfterUi).
        /// </summary>
        private void OnAfterUi(int width, int height)
        {
            var app = Application.Current;
            if (_mode == Mode.Edit)
                _panel.Render(app.Renderer, DrawCanvas, new Color(0.16f, 0.17f, 0.2f, 1f), app.Window);
            _imgui?.Render(app.Window);
        }

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
            Prompt("NEW SKELETON FROM " + src.label + " — NAME", src.label.ToLowerInvariant().Replace("_idle", "").Replace("_", ""), name =>
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
            Prompt("RENAME SKELETON", _skel.name, name =>
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
            Prompt("NEW PART — NAME", "part" + (_view.parts.Count + 1), name =>
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
            Prompt("RENAME PART", _part.name, name =>
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
            // one ImGui frame per rendered frame: the panels are declared here, the canvas is drawn later in DrawUi
            // and ImGui itself is rendered after the UI pass.
            _imgui.NewFrame(Application.Current.Window);
            BuildUi();

            if (_promptOpen) return;                 // the modal owns the keyboard
            if (!_imgui.WantsKeyboard) HandleKeys(); // a focused text field keeps the shortcuts quiet
            if (_mode == Mode.Preview && !_imgui.WantsMouse) UpdatePreview();
        }

        // ------------------------------------------------------------------ modal prompt

        private void Prompt(string title, string initial, Action<string> accept)
        {
            _promptTitle = title; _promptText = initial ?? ""; _promptAccept = accept;
            _promptOpen = true; _promptFocus = true;
        }

        private void DrawPrompt()
        {
            if (_promptOpen && !ImGui.IsPopupOpen("##prompt")) ImGui.OpenPopup("##prompt");
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vec2(0.5f, 0.5f));
            if (!ImGui.BeginPopupModal("##prompt", ref _promptOpen, ImGuiWindowFlags.AlwaysAutoResize)) return;

            ImGui.TextColored(Accent, _promptTitle ?? "");
            ImGui.Spacing();
            if (_promptFocus) { ImGui.SetKeyboardFocusHere(); _promptFocus = false; }
            ImGui.SetNextItemWidth(320f);
            bool entered = ImGui.InputText("##text", ref _promptText, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.Spacing();
            bool ok = ImGui.Button("OK", new Vec2(90f, 0f)) || entered;
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel", new Vec2(90f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape);
            if (ok)
            {
                var accept = _promptAccept; _promptAccept = null; _promptOpen = false;
                ImGui.CloseCurrentPopup();
                accept?.Invoke(_promptText.Trim());
            }
            else if (cancel)
            {
                _promptAccept = null; _promptOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
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
            bool over = _viewportHovered && _center.Contains(mouse);
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

        // ------------------------------------------------------------------ ui (Dear ImGui)

        private static readonly System.Numerics.Vector4 Accent = new System.Numerics.Vector4(1f, 0.78f, 0.25f, 1f);
        private static readonly System.Numerics.Vector4 Dim = new System.Numerics.Vector4(0.55f, 0.58f, 0.63f, 1f);
        private static readonly System.Numerics.Vector4 DangerCol = new System.Numerics.Vector4(0.8f, 0.25f, 0.2f, 1f);

        /// <summary>
        /// Declares the whole editor UI for this frame: a menu bar, the dockable side panels and the viewport panel
        /// whose content rectangle the canvas is drawn into. Called from Update, between NewFrame and Render.
        /// </summary>
        private void BuildUi()
        {
            var window = Application.Current.Window;
            DrawMenuBar();
            float statusH = ImGui.GetFrameHeight();
            uint dockspace = _imgui.BeginDockHost("##editor", statusH);
            if (!_layoutBuilt)
            {
                _layoutBuilt = true;
                // only the first time this editor runs: the saved layout wins afterwards
                if (!_imgui.HasSavedLayout)
                    ImGuiDock.BuildDefaultLayout(dockspace, ImGui.GetWindowSize(), "Skeletons", "Skeleton");
            }
            _imgui.EndDockHost();
            DrawLeftPanel();
            DrawRightPanel();
            if (_mode == Mode.Edit) DrawViewportPanel(window);
            else
            {
                // the physics preview is the engine's own 3D render in the main framebuffer: leave the central dock
                // node empty so it shows through (the dock space is set up with a pass-through central node)
                var w = Application.Current.Window;
                _center = new Rect(0f, 0f, w.Width, w.Height);
                _viewportHovered = !_imgui.WantsMouse;
            }
            DrawStatusBar(statusH);
            if (_showHelp) DrawHelp();
            DrawPrompt();
        }

        private void DrawMenuBar()
        {
            if (!ImGui.BeginMainMenuBar()) return;
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New skeleton...", "Ctrl+N")) PromptNew();
                if (ImGui.MenuItem("Save", "Ctrl+S", false, _skel != null)) Save();
                if (ImGui.MenuItem("Rename skeleton...", "Shift+F2", false, _skel != null)) PromptRename();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _undo.Count > 0)) Undo();
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _redo.Count > 0)) Redo();
                ImGui.Separator();
                if (ImGui.MenuItem("New part...", "N", false, _view != null)) PromptNewPart();
                if (ImGui.MenuItem("Delete part", "Del", false, _part != null)) DeletePart();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                bool preview = _mode == Mode.Preview;
                if (ImGui.MenuItem("Physics preview", "Tab", preview)) SetMode(preview ? Mode.Edit : Mode.Preview);
                if (ImGui.MenuItem("Fit to window", "F", false, _mode == Mode.Edit)) _fit = true;
                if (ImGui.MenuItem("Help", "F1", _showHelp)) _showHelp = !_showHelp;
                ImGui.EndMenu();
            }

            // document summary, right of the menus
            ImGui.Spacing();
            string doc = _skel != null
                ? $"{_skel.name}{(_dirty ? "*" : "")}   view {_view?.name ?? "-"} (side {_view?.side ?? 0}, frame {_view?.frame ?? 0})   {(_mode == Mode.Edit ? "edit" : "physics preview")}   tool {_tool.ToString().ToLowerInvariant()}"
                : "no skeleton - Ctrl+N";
            ImGui.TextColored(_dirty ? Accent : Dim, doc);
            ImGui.EndMainMenuBar();
        }

        private void DrawStatusBar(float height)
        {
            if (_imgui.BeginStatusBar("##status", height))
            {
                bool recent = Time.unscaledTime < _statusUntil;
                string text = recent ? _status :
                    _mode == Mode.Preview ? "Space ragdoll   Ctrl+Space gib   click a piece = tear off   K break all   Up blast   R reset   RMB orbit   wheel zoom" :
                    _tool == Tool.Draw ? "Drawing: click = vertex   Enter / first vertex = close   Backspace = undo vertex   Esc = stop" :
                    "N new part   click = select / drag vertex or pivot   J pivot   P parent   I insert vertex   Del delete   L loose   [ ] frame   , . side   wheel zoom   MMB pan   F fit";
                ImGui.TextColored(recent ? Accent : Dim, text);
            }
            ImGui.End();
        }

        /// <summary>
        /// The canvas panel. Its contents are rendered by the engine into an offscreen target (see
        /// <see cref="ImGuiViewportPanel"/>) and shown here, so it docks and clips like any other panel.
        /// </summary>
        private void DrawViewportPanel(Window window)
        {
            _panel.Declare(_imgui, window);
            // the panel's size is only final once the dock layout has settled, and it changes when the window is
            // resized: refit until the user zooms or pans by hand
            if (!_userMovedView && (MathF.Abs(_panel.Width - _center.width) > 1f || MathF.Abs(_panel.Height - _center.height) > 1f)) _fit = true;
            _center = _panel.Rect;
            _viewportHovered = _panel.Hovered && !_promptOpen;
        }

        // ---- left: skeletons + sheets ----

        private void DrawLeftPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(280f, 600f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Skeletons")) { ImGui.End(); return; }

            if (ImGui.Button("+ New", new Vec2(-1f, 0f))) PromptNew();
            if (ImGui.Button(_dirty ? "Save *" : "Save", new Vec2(-1f, 0f))) Save();
            ImGui.Separator();

            if (ImGui.BeginListBox("##skeletons", new Vec2(-1f, 8f * ImGui.GetTextLineHeightWithSpacing())))
            {
                foreach (var n in _names)
                    if (ImGui.Selectable(n, _skel != null && _skel.name == n)) Select(n);
                ImGui.EndListBox();
            }

            ImGui.Spacing();
            ImGui.TextColored(Accent, "Sheets (new skeleton source)");
            ImGui.TextColored(Dim, "Pick a sheet, then set its grid");
            if (ImGui.BeginTable("##sheets", 3, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp, new Vec2(-1f, -1f)))
            {
                ImGui.TableSetupColumn("sheet", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("frames", ImGuiTableColumnFlags.WidthFixed, 64f);
                ImGui.TableSetupColumn("sides", ImGuiTableColumnFlags.WidthFixed, 64f);
                for (int i = 0; i < _sources.Count; i++)
                {
                    var src = _sources[i];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable(src.label + "##src" + i, i == _sourceIndex)) _sourceIndex = i;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(src.rel);
                    ImGui.TableSetColumnIndex(1);
                    ImGui.SetNextItemWidth(-1f);
                    int frames = src.frames;
                    if (ImGui.DragInt("##f" + i, ref frames, 0.1f, 1, 32, "F%d")) src.frames = Mathf.Clamp(frames, 1, 32);
                    ImGui.TableSetColumnIndex(2);
                    ImGui.SetNextItemWidth(-1f);
                    int sides = src.sides;
                    if (ImGui.DragInt("##s" + i, ref sides, 0.1f, 1, 16, "S%d")) src.sides = Mathf.Clamp(sides, 1, 16);
                }
                ImGui.EndTable();
            }
            ImGui.End();
        }

        // ---- right: skeleton / views / parts ----

        private void DrawRightPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(340f, 600f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Skeleton")) { ImGui.End(); return; }
            if (_skel == null)
            {
                ImGui.TextColored(Dim, "No skeleton. Pick a sheet and press Ctrl+N.");
                ImGui.End();
                return;
            }

            ImGui.TextWrapped(_skel.texture ?? "");
            int frames = _skel.frames, sides = _skel.sides;
            // labels sit to the right of the field, so the fields stop short of the panel edge
            ImGui.PushItemWidth(-110f);
            if (ImGui.DragInt("Frames", ref frames, 0.1f, 1, 32)) { Snapshot(); _skel.frames = Mathf.Clamp(frames, 1, 32); LoadCell(); }
            if (ImGui.DragInt("Sides", ref sides, 0.1f, 1, 16)) { Snapshot(); _skel.sides = Mathf.Clamp(sides, 1, 16); LoadCell(); }

            float ppu = _skel.pixelsPerUnit, depth = _skel.depth, life = _skel.lifetime;
            if (ImGui.DragFloat("Pixels / unit", ref ppu, 1f, 1f, 1000f, "%.0f")) { _skel.pixelsPerUnit = ppu; _dirty = true; LoadCell(); }
            if (ImGui.DragFloat("Depth", ref depth, 0.01f, 0.02f, 2f, "%.2f")) { _skel.depth = depth; _dirty = true; }
            if (ImGui.DragFloat("Lifetime", ref life, 1f, 0f, 600f, "%.0f s")) { _skel.lifetime = life; _dirty = true; }
            ImGui.PopItemWidth();

            // ---- views
            ImGui.SeparatorText("Views (side / reference frame)");
            if (ImGui.Button("+ View")) AddView();
            ImGui.SameLine();
            ImGui.BeginDisabled(_skel.views.Count <= 1);
            if (ImGui.Button("- View")) DeleteView();
            ImGui.EndDisabled();

            if (ImGui.BeginTable("##views", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("view", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("parts", ImGuiTableColumnFlags.WidthFixed, 56f);
                ImGui.TableSetupColumn("side", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableSetupColumn("frame", ImGuiTableColumnFlags.WidthFixed, 70f);
                for (int i = 0; i < _skel.views.Count; i++)
                {
                    var view = _skel.views[i];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable(view.name + "##view" + i, view == _view)) { _view = view; _part = null; _tool = Tool.Select; LoadCell(); }
                    if (ImGui.BeginPopupContextItem("##viewctx" + i))
                    {
                        if (ImGui.MenuItem("Rename...")) Prompt("Rename view", view.name, n => { if (!string.IsNullOrEmpty(n)) { Snapshot(); view.name = n; } });
                        ImGui.EndPopup();
                    }
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextColored(Dim, view.parts.Count.ToString());
                    ImGui.TableSetColumnIndex(2);
                    ImGui.SetNextItemWidth(-1f);
                    int side = view.side;
                    if (ImGui.DragInt("##vs" + i, ref side, 0.1f, 0, Math.Max(0, _skel.sides - 1), "side %d"))
                    { Snapshot(); view.side = Mathf.Clamp(side, 0, Math.Max(0, _skel.sides - 1)); if (view == _view) LoadCell(); }
                    ImGui.TableSetColumnIndex(3);
                    ImGui.SetNextItemWidth(-1f);
                    int frame = view.frame;
                    if (ImGui.DragInt("##vf" + i, ref frame, 0.1f, 0, Math.Max(0, _skel.frames - 1), "frame %d"))
                    { Snapshot(); view.frame = Mathf.Clamp(frame, 0, Math.Max(0, _skel.frames - 1)); if (view == _view) LoadCell(); }
                }
                ImGui.EndTable();
            }
            if (_view != null && _skel.views.Count > 1 && ImGui.Button("Copy parts from the next view", new Vec2(-1f, 0f)))
            {
                int i = _skel.views.IndexOf(_view);
                var from = _skel.views[(i + 1) % _skel.views.Count];
                Snapshot(); CopyParts(from, _view); _part = null;
            }

            // ---- parts
            ImGui.SeparatorText("Parts");
            ImGui.BeginDisabled(_view == null);
            if (ImGui.Button("+ Part")) PromptNewPart();
            ImGui.EndDisabled();
            if (_view != null && ImGui.BeginListBox("##parts", new Vec2(-1f, 8f * ImGui.GetTextLineHeightWithSpacing())))
            {
                for (int i = 0; i < _view.parts.Count; i++)
                {
                    var p = _view.parts[i];
                    string label = (string.IsNullOrEmpty(p.parent) ? "" : "   ") + p.name + (p.loose ? "  (loose)" : "")
                                 + (string.IsNullOrEmpty(p.parent) ? "   root" : "   < " + p.parent);
                    bool incomplete = p.polygon.Count < 3;
                    if (incomplete) ImGui.PushStyleColor(ImGuiCol.Text, DangerCol);
                    if (ImGui.Selectable(label + "##part" + i, p == _part)) { _part = p; if (_tool == Tool.Draw) _tool = Tool.Select; }
                    if (incomplete) ImGui.PopStyleColor();
                    if (ImGui.BeginPopupContextItem("##partctx" + i))
                    {
                        _part = p;
                        if (ImGui.MenuItem("Rename...")) PromptRenamePart();
                        if (ImGui.MenuItem("Delete")) DeletePart();
                        ImGui.EndPopup();
                    }
                }
                ImGui.EndListBox();
            }

            // ---- selected part
            if (_part != null)
            {
                ImGui.SeparatorText($"{_part.name}  ({_part.polygon.Count} vertices)");
                string parentLabel = string.IsNullOrEmpty(_part.parent) ? "none (root)" : _part.parent;
                ImGui.SetNextItemWidth(-110f);
                if (ImGui.BeginCombo("Parent", parentLabel))
                {
                    if (ImGui.Selectable("none (root)", string.IsNullOrEmpty(_part.parent))) { Snapshot(); _part.parent = ""; }
                    foreach (var other in _view.parts)
                    {
                        if (other == _part || IsDescendant(other, _part)) continue;
                        if (ImGui.Selectable(other.name, _part.parent == other.name)) { Snapshot(); _part.parent = other.name; }
                    }
                    ImGui.EndCombo();
                }

                float mass = _part.mass, brk = _part.breakImpulse;
                ImGui.PushItemWidth(-110f);
                if (ImGui.DragFloat("Mass", ref mass, 0.1f, 0.05f, 500f, "%.1f")) { _part.mass = mass; _dirty = true; }
                if (ImGui.DragFloat("Break", ref brk, 1f, 0f, 1000f, brk <= 0f ? "never" : "%.0f")) { _part.breakImpulse = brk; _dirty = true; }

                int shape = _part.shape == PartShape.Box ? 0 : 1;
                if (ImGui.Combo("Shape", ref shape, "Box\0Capsule\0")) { Snapshot(); _part.shape = shape == 0 ? PartShape.Box : PartShape.Capsule; }
                ImGui.PopItemWidth();
                bool loose = _part.loose;
                if (ImGui.Checkbox("Loose (L)", ref loose)) { Snapshot(); _part.loose = loose; }

                ImGui.TextColored(Dim, $"Pivot {_part.pivot.x:0.000}, {_part.pivot.y:0.000}   (J places it)");
                ImGui.PushStyleColor(ImGuiCol.Button, DangerCol);
                if (ImGui.Button("Delete part (Del)", new Vec2(-1f, 0f))) DeletePart();
                ImGui.PopStyleColor();
            }
            ImGui.End();
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

        /// <summary>
        /// The reference cell with the part outlines over it, drawn into the viewport panel's framebuffer. The
        /// rectangle starts at the origin, so panning / zooming are independent of where the panel is docked.
        /// </summary>
        private void DrawCanvas(UiBatch batch, Rect rect)
        {
            _batch = batch;
            _center = rect;
            DrawCellEditor(batch);
            if (!Input.GetMouseButton(0)) { _dragVertex = -1; _dragPivot = false; }
            if (!Input.GetMouseButton(1) && !Input.GetMouseButton(2)) _panning = false;
        }

        private void DrawCellEditor(UiBatch batch)
        {
            var r = _center;
            // checkerboard backdrop
            batch.FillRect(r, new Color(0.16f, 0.17f, 0.2f, 1f));
            if (_cell == null || _tex == null)
            {
                string msg = _skel == null ? "CTRL+N: NEW SKELETON FROM A SHEET" : "NO TEXTURE: " + (_skel.texture ?? "");
                batch.DrawText(msg, new Vector2(r.x + 20f, r.center.y), 2f * S, new Color(0.55f, 0.58f, 0.63f, 1f));
                return;
            }
            Vector2 mouse = _panel.Mouse;
            bool over = _viewportHovered && r.Contains(mouse);

            // zoom / pan
            float cellW = _cell.rect.width, cellH = _cell.rect.height;
            if (_fit)
            {
                float fitZoom = MathF.Min((r.width - 40f) / cellW, (r.height - 40f) / cellH);
                _zoom = MathF.Max(0.05f, fitZoom);
                _pan = Vector2.zero;
                _fit = false;
                _userMovedView = false;
            }
            if (over && Input.mouseScrollDelta.y != 0f)
            {
                float factor = Input.mouseScrollDelta.y > 0f ? 1.15f : 1f / 1.15f;
                Vector2 before = ScreenToCell(mouse);
                _zoom = Mathf.Clamp(_zoom * factor, 0.05f, 40f);
                _userMovedView = true;
                ComputeCellRect(r, cellW, cellH);
                Vector2 after = ScreenToCell(mouse);
                _pan += new Vector2((after.x - before.x) * _cellRect.width, (after.y - before.y) * _cellRect.height);
            }
            if (over && (Input.GetMouseButtonDown(2) || (Input.GetMouseButtonDown(1) && _tool == Tool.Select))) { _panning = true; _lastMouse = mouse; }
            if (_panning) { _pan += mouse - _lastMouse; _lastMouse = mouse; _userMovedView = true; }
            ComputeCellRect(r, cellW, cellH);

            // the cell
            batch.PushClip(r);
            _cell.GetUVs(out float u0, out float v0, out float u1, out float v1);
            DrawChecker(_cellRect);
            batch.DrawRaw(_tex, _cellRect, u0, v0, u1, v1, Color.white);
            Border(batch, _cellRect, new Color(0.25f, 0.27f, 0.32f, 1f));

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
                            batch.DrawLine(a, b, t, col);
                        }
                        float vs = (sel ? 5f : 3f) * S;
                        batch.FillRect(new Rect(a.x - vs, a.y - vs, vs * 2f, vs * 2f), col);
                    }
                    // pivot + link to the parent's pivot
                    Vector2 pv = CellToScreen(p.pivot);
                    float ps = (sel ? 7f : 5f) * S;
                    batch.FillRect(new Rect(pv.x - ps, pv.y - 1f, ps * 2f, 2f), Color.white);
                    batch.FillRect(new Rect(pv.x - 1f, pv.y - ps, 2f, ps * 2f), Color.white);
                    var parent = string.IsNullOrEmpty(p.parent) ? null : _view.Find(p.parent);
                    if (parent != null)
                    {
                        Vector2 pc = parent.polygon.Count >= 3 ? CellToScreen(Sprite.Centroid(parent.PolygonArray())) : CellToScreen(parent.pivot);
                        batch.DrawLine(pv, pc, 1f, new Color(1f, 1f, 1f, 0.45f));
                    }
                    Vector2 labelAt = p.polygon.Count >= 3 ? CellToScreen(Sprite.Centroid(p.PolygonArray())) : pv + new Vector2(8f, 8f);
                    batch.DrawText(p.name, labelAt, 1.6f * S, sel ? new Color(1f, 0.78f, 0.25f, 1f) : col);
                }
                // the edge being drawn
                if (_tool == Tool.Draw && _part != null && _part.polygon.Count > 0 && over)
                    batch.DrawLine(CellToScreen(_part.polygon[_part.polygon.Count - 1]), mouse, 1.5f, new Color(1f, 1f, 1f, 0.6f));
            }
            batch.PopClip();

            if (over) HandleCellMouse(mouse);
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
                    _batch.FillRect(new Rect(x, y, w, h), (i + j) % 2 == 0 ? new Color(0.3f, 0.3f, 0.33f, 1f) : new Color(0.38f, 0.38f, 0.42f, 1f));
                }
        }

        private int HoveredVertex(SkeletonPart p)
        {
            if (p == null) return -1;
            Vector2 mouse = _panel.Mouse;
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

        /// <summary>What a skeleton is and every shortcut, in a dockable window (F1).</summary>
        private void DrawHelp()
        {
            ImGui.SetNextWindowSize(new Vec2(760f, 420f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Help", ref _showHelp))
            {
                ImGui.End();
                return;
            }
            ImGui.TextWrapped("A skeleton says how a sprite is sliced when it dies: parts (polygons), their pivots (joints) and parents. "
                            + "It is not used for animation. Coordinates are normalised inside a sheet cell, one view per side.");
            ImGui.SeparatorText("Editing");
            Help("Ctrl+N", "New skeleton from the selected sheet");
            Help("Ctrl+S", "Save");
            Help("Ctrl+Z / Ctrl+Y", "Undo / redo");
            Help("N", "New part: type a name, then click the outline; Enter or the first vertex closes it (Backspace undoes a vertex)");
            Help("Click / drag", "Select a part, drag its vertices or its pivot");
            Help("I / Del", "Insert a vertex on the nearest edge / delete the vertex or the part");
            Help("J", "Place the pivot (where the part hangs off its parent)");
            Help("P", "Pick the parent by clicking it");
            Help("L", "Loose (the part starts detached)");
            Help(", .  and  [ ]", "Previous / next side and reference frame");
            Help("Wheel / MMB / F", "Zoom / pan / fit");
            ImGui.SeparatorText("Physics preview (Tab)");
            Help("Space", "Slice the sprite into a ragdoll with the real solver");
            Help("Ctrl+Space", "Gib it");
            Help("Click", "Tear a piece off");
            Help("K / Up / R", "Break every joint / blast / reset");
            Help("RMB / wheel", "Orbit / zoom");
            ImGui.Spacing();
            ImGui.TextColored(Dim, "The game reloads saved skeletons live; EnemyData.skeleton names the one an enemy uses.");
            ImGui.End();
        }

        private static void Help(string keys, string what)
        {
            ImGui.TextColored(Accent, keys);
            ImGui.SameLine(190f);
            ImGui.TextWrapped(what);
        }

        private static void Border(UiBatch batch, Rect r, Color c, float t = 1f)
        {
            batch.FillRect(new Rect(r.x, r.y, r.width, t), c);
            batch.FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            batch.FillRect(new Rect(r.x, r.y, t, r.height), c);
            batch.FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
