using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content.Level;
using RipNRage.Level;

namespace RipNRage.Editor
{
    public enum EditMode { Brush, Entity, Paint }

    /// <summary>
    /// The Quake-style brush editor: selection/picking, drag-to-create boxes, move, push/pull faces,
    /// rotate, duplicate, texturing, entity placement, undo/redo, save and "play".
    /// Visuals come from the game's own LevelLoader in preview mode (WYSIWYG).
    /// </summary>
    public class EditorController : Behaviour, IUiDrawer
    {
        public MapDocument doc = new MapDocument();
        public EditorCamera cam;
        public EditMode mode = EditMode.Brush;
        public float grid = 1f;
        public float extrudeHeight = 2f;
        public float workHeight = 0f;
        public bool showHelp = true;
        public bool showAllWireframes = true;

        // selection
        private readonly List<Brush> _selBrushes = new List<Brush>();
        private Face _selFace;
        private readonly List<MapEntity> _selEntities = new List<MapEntity>();
        private Brush _hoverBrush; private Face _hoverFace; private MapEntity _hoverEntity; private Vector3 _hoverPoint; private bool _hoverGround;

        // palette
        private int _texIndex; private List<string> _texNames;
        private int _entIndex;
        private readonly Dictionary<string, Sprite> _texThumbs = new Dictionary<string, Sprite>();

        // drag state
        private enum Drag { None, Pending, Create, Move, FaceDrag, MoveEntity }
        private Drag _drag; private Vector2 _dragStartPx; private Vector3 _dragStartPoint;
        private Plane _dragPlane; private Vector3 _dragU, _dragV; private Face _dragFace; private Vector3 _dragLastDelta;
        private readonly List<(Brush brush, List<(Vector3, Vector3, Vector3)> pts)> _dragOriginals = new List<(Brush, List<(Vector3, Vector3, Vector3)>)>();
        private readonly List<(MapEntity e, Vector3 origin)> _dragEntityOriginals = new List<(MapEntity, Vector3)>();
        private float _faceDragAccum;
        private Brush _createPreview;

        // vertex paint
        public float paintRadius = 1.5f;
        public float paintStrength = 0.5f;
        public float paintCell = 0.5f;
        public Color paintColor = new Color(0.8f, 0.15f, 0.1f, 1f);
        private static readonly Color[] PaintPresets =
        {
            new Color(0.8f, 0.15f, 0.1f, 1f), new Color(0.95f, 0.55f, 0.1f, 1f), new Color(0.95f, 0.9f, 0.2f, 1f), new Color(0.2f, 0.75f, 0.25f, 1f),
            new Color(0.15f, 0.45f, 0.9f, 1f), new Color(0.55f, 0.2f, 0.8f, 1f), new Color(0.35f, 0.22f, 0.12f, 1f), new Color(0.25f, 0.25f, 0.28f, 1f),
            new Color(0.08f, 0.08f, 0.1f, 1f), Color.white,
        };
        private bool _painting;
        private bool _paintDirty;
        private float _paintHue = 0.02f, _paintSat = 0.85f, _paintVal = 0.8f;

        // snapshots
        public readonly LevelSnapshots snapshots = new LevelSnapshots();
        public bool autoSnapshotOnSave = true;
        private bool _snapshotPanel;
        private List<SnapshotInfo> _snapshotList = new List<SnapshotInfo>();
        private int _snapshotIndex;

        // undo
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private bool _dirty;

        // visuals
        private LoadedLevel _preview;
        private string _status = ""; private float _statusTimer;

        protected override void Start()
        {
            _texNames = new List<string>(LevelTextures.Names);
            _texIndex = Math.Max(0, _texNames.IndexOf("planks"));
            Rebuild();
            Status("Editor ready — F1 toggles help");
        }

        // ------------------------------------------------------------------ document helpers

        private void PushUndo()
        {
            _undo.Push(MapFile.Write(doc));
            if (_undo.Count > 64) { var arr = _undo.ToArray(); _undo.Clear(); for (int i = Math.Min(63, arr.Length - 1); i >= 0; i--) _undo.Push(arr[i]); }
            _redo.Clear();
            _dirty = true;
        }

        private void Undo()
        {
            if (_undo.Count == 0) { Status("Nothing to undo"); return; }
            _redo.Push(MapFile.Write(doc));
            Restore(_undo.Pop());
            Status("Undo");
        }

        private void Redo()
        {
            if (_redo.Count == 0) { Status("Nothing to redo"); return; }
            _undo.Push(MapFile.Write(doc));
            Restore(_redo.Pop());
            Status("Redo");
        }

        private void Restore(string text)
        {
            string path = doc.path;
            doc = MapFile.Parse(text);
            doc.path = path;
            ClearSelection();
            Rebuild();
        }

        private void Rebuild()
        {
            if (_preview != null) GameObject.DestroyImmediate(_preview.root);
            _preview = LevelLoader.Load(doc, preview: true);
        }

        private void ClearSelection() { _selBrushes.Clear(); _selFace = null; _selEntities.Clear(); }

        private void Status(string s) { _status = s; _statusTimer = 4f; }

        public void Save(string path = null)
        {
            path ??= doc.path ?? "levels/untitled.map";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            MapFile.Save(doc, path);
            _dirty = false;
            if (autoSnapshotOnSave) snapshots.Take(doc, null, automatic: true);
            Status("Saved " + path + (autoSnapshotOnSave ? " (+ auto snapshot)" : ""));
        }

        // ------------------------------------------------------------------ snapshots

        public void TakeSnapshot(string label = null)
        {
            if (doc.path == null) { Status("Save the level first"); return; }
            var s = snapshots.Take(doc, label ?? "manual", automatic: false);
            Status(s != null ? "Snapshot " + s.name : "Snapshot failed");
        }

        private void OpenSnapshotPanel()
        {
            _snapshotList = snapshots.List(doc.path);
            _snapshotIndex = 0;
            _snapshotPanel = true;
        }

        private void SnapshotPanelInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8)) { _snapshotPanel = false; return; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) _snapshotIndex = Math.Max(0, _snapshotIndex - 1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) _snapshotIndex = Math.Min(Math.Max(0, _snapshotList.Count - 1), _snapshotIndex + 1);
            if (Input.GetKeyDown(KeyCode.F7)) { TakeSnapshot(); _snapshotList = snapshots.List(doc.path); }
            if (_snapshotList.Count == 0) return;
            var sel = _snapshotList[_snapshotIndex];
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                try
                {
                    PushUndo();
                    var restored = snapshots.Restore(sel);
                    string path = doc.path;
                    doc = restored; doc.path = path;
                    ClearSelection(); Rebuild();
                    _snapshotPanel = false;
                    Status("Restored snapshot " + sel.name + " (Ctrl+Z undoes)");
                }
                catch (Exception e) { Status("Restore failed: " + e.Message); }
            }
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X))
            {
                snapshots.Delete(sel);
                _snapshotList = snapshots.List(doc.path);
                _snapshotIndex = Math.Min(_snapshotIndex, Math.Max(0, _snapshotList.Count - 1));
                Status("Snapshot deleted");
            }
        }

        private void Play()
        {
            Save();
            string dir = AppContext.BaseDirectory;
            string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "RipNRage.exe" : "RipNRage");
            string dll = Path.Combine(dir, "RipNRage.dll");
            try
            {
                var psi = File.Exists(exe)
                    ? new ProcessStartInfo(exe) { UseShellExecute = false }
                    : new ProcessStartInfo("dotnet") { UseShellExecute = false };
                if (!File.Exists(exe)) psi.ArgumentList.Add(dll);
                psi.ArgumentList.Add("--level"); psi.ArgumentList.Add(Path.GetFullPath(doc.path));
                psi.ArgumentList.Add("--assets"); psi.ArgumentList.Add(Application.dataPath);
                psi.WorkingDirectory = Directory.GetCurrentDirectory();
                Process.Start(psi);
                Status("Launched the game with " + doc.path);
            }
            catch (Exception e) { Status("Could not start the game: " + e.Message); }
        }

        // ------------------------------------------------------------------ per frame

        protected override void Update()
        {
            if (_statusTimer > 0f) _statusTimer -= Time.unscaledDeltaTime;
            var app = Application.Current;
            int w = app.Window.Width, h = app.Window.Height;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // ---- snapshot browser captures the keyboard while open
            if (_snapshotPanel) { SnapshotPanelInput(); DrawGizmos(); return; }
            if (Input.GetKeyDown(KeyCode.F7)) TakeSnapshot();
            if (Input.GetKeyDown(KeyCode.F8)) OpenSnapshotPanel();

            // ---- global keys
            if (Input.GetKeyDown(KeyCode.F1)) showHelp = !showHelp;
            if (Input.GetKeyDown(KeyCode.F5)) Play();
            if (ctrl && Input.GetKeyDown(KeyCode.S)) Save();
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (Input.GetKeyDown(KeyCode.Tab)) { mode = (EditMode)(((int)mode + 1) % 3); _drag = Drag.None; _painting = false; Status("Mode: " + mode); }
            if (Input.GetKeyDown(KeyCode.V)) { mode = mode == EditMode.Paint ? EditMode.Brush : EditMode.Paint; _drag = Drag.None; _painting = false; Status("Mode: " + mode); }
            // spawn quick keys (any mode): P player start, M monster, N spawner — placed at the hovered surface point
            if (!cam.Flying && (_hoverBrush != null || _hoverGround))
            {
                if (Input.GetKeyDown(KeyCode.P)) PlaceOrMoveUnique("info_player_start", BrushGeometry.Snap(_hoverPoint, grid));
                if (Input.GetKeyDown(KeyCode.M)) PlaceEntity(EntityRegistry.Get("monster_rifleman"), BrushGeometry.Snap(_hoverPoint, grid));
                if (Input.GetKeyDown(KeyCode.N)) PlaceEntity(EntityRegistry.Get("monster_spawner"), BrushGeometry.Snap(_hoverPoint, grid));
            }
            if (mode != EditMode.Paint)
            {
                if (Input.GetKeyDown(KeyCode.LeftBracket)) { grid = Mathf.Max(0.125f, grid * 0.5f); Status("Grid " + grid); }
                if (Input.GetKeyDown(KeyCode.RightBracket)) { grid = Mathf.Min(16f, grid * 2f); Status("Grid " + grid); }
            }
            if (Input.GetKeyDown(KeyCode.G)) showAllWireframes = !showAllWireframes;
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_drag != Drag.None) { _drag = Drag.None; _createPreview = null; Rebuild(); } else ClearSelection(); }
            if (Input.GetKeyDown(KeyCode.H)) { workHeight = _hoverBrush != null ? BrushGeometry.Snap(_hoverPoint, grid).y : 0f; Status($"Work plane height {workHeight}"); }
            if (Input.GetKeyDown(KeyCode.F)) FocusSelection();

            // ---- hover picking (only when the cursor is free)
            if (!cam.Flying) UpdateHover(w, h);

            if (mode == EditMode.Brush) BrushModeInput(w, h, ctrl, shift, alt);
            else if (mode == EditMode.Entity) EntityModeInput(w, h, ctrl, shift, alt);
            else PaintModeInput(w, h, ctrl, shift, alt);

            DrawGizmos();
        }

        private Ray MouseRay(int w, int h) => Camera.main.ScreenPointToRay(Input.mousePosition, w, h);

        private void UpdateHover(int w, int h)
        {
            var ray = MouseRay(w, h);
            _hoverBrush = null; _hoverFace = null; _hoverEntity = null; _hoverGround = false;
            float best = float.MaxValue;
            foreach (var b in doc.Brushes)
                if (BrushGeometry.Raycast(b, ray, out float t, out Face f) && t < best) { best = t; _hoverBrush = b; _hoverFace = f; }
            foreach (var e in doc.entities)
            {
                var def = EntityRegistry.Get(e.classname);
                Vector3 ext = def?.pickExtents ?? new Vector3(0.5f, 0.5f, 0.5f);
                Vector3 c = e.origin + new Vector3(0, ext.y, 0);
                if (Physics.RayAabb(ray, new Bounds(c - ext, c + ext), best, null, out var hit) && hit.distance < best)
                {
                    best = hit.distance; _hoverEntity = e; _hoverBrush = null; _hoverFace = null;
                }
            }
            if (best < float.MaxValue) _hoverPoint = ray.GetPoint(best);
            else if (BrushGeometry.RaycastGround(ray, workHeight, out var gp)) { _hoverPoint = gp; _hoverGround = true; }
        }

        // ------------------------------------------------------------------ brush mode

        private void BrushModeInput(int w, int h, bool ctrl, bool shift, bool alt)
        {
            // keys acting on the selection
            if (_selBrushes.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X)) { PushUndo(); foreach (var b in _selBrushes) doc.Brushes.Remove(b); ClearSelection(); Rebuild(); Status("Deleted"); }
                if (ctrl && Input.GetKeyDown(KeyCode.D)) Duplicate();
                if (Input.GetKeyDown(KeyCode.R)) RotateSelection(90f);
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) PushPull(grid);
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) PushPull(-grid);
                if (Input.GetKeyDown(KeyCode.T)) ApplyTexture(_texNames[_texIndex], true);
                if (Input.GetKeyDown(KeyCode.Comma) || Input.GetKeyDown(KeyCode.Period))
                {
                    int dir = Input.GetKeyDown(KeyCode.Period) ? 1 : -1;
                    if (shift) ScaleTexture(dir > 0 ? 2f : 0.5f);
                    else { _texIndex = (_texIndex + dir + _texNames.Count) % _texNames.Count; ApplyTexture(_texNames[_texIndex], false); }
                }
                Vector3 nudge = NudgeFromArrows();
                if (nudge != Vector3.zero) { PushUndo(); foreach (var b in _selBrushes) b.Translate(nudge); Rebuild(); }
            }
            else if (Input.GetKeyDown(KeyCode.Comma) || Input.GetKeyDown(KeyCode.Period))
            {
                _texIndex = (_texIndex + (Input.GetKeyDown(KeyCode.Period) ? 1 : -1) + _texNames.Count) % _texNames.Count;
            }

            if (cam.Flying) return;

            // ---- mouse
            if (Input.GetMouseButtonDown(0))
            {
                _dragStartPx = Input.mousePosition;
                _drag = Drag.Pending;
                _dragStartPoint = _hoverPoint;
                _dragFace = _hoverFace;
                if (_hoverBrush != null && alt && _selBrushes.Contains(_hoverBrush))
                {
                    _selFace = _hoverFace; _drag = Drag.FaceDrag; _faceDragAccum = 0f; PushUndo();
                }
                else if (_hoverBrush != null && _selBrushes.Contains(_hoverBrush) && !ctrl && !shift)
                {
                    BeginMove(); // may become a click (select face) if the mouse does not move
                }
            }

            if (_drag == Drag.Pending && Input.GetMouseButton(0))
            {
                if ((Input.mousePosition - _dragStartPx).magnitude > 4f)
                {
                    // start creating a brush on the hovered face or the work plane
                    if (_hoverBrush != null && !ctrl) BeginCreate(_dragFace.plane, _dragStartPoint);
                    else BeginCreate(new Plane(Vector3.up, workHeight), new Vector3(_dragStartPoint.x, workHeight, _dragStartPoint.z));
                }
            }

            if (_drag == Drag.Create && Input.GetMouseButton(0)) UpdateCreate(w, h);
            if (_drag == Drag.Move && Input.GetMouseButton(0)) UpdateMove(w, h, shift);
            if (_drag == Drag.FaceDrag && Input.GetMouseButton(0)) UpdateFaceDrag(w, h);

            if (Input.GetMouseButtonUp(0))
            {
                switch (_drag)
                {
                    case Drag.Pending: ClickSelect(shift); break;
                    case Drag.Create: FinishCreate(); break;
                    case Drag.Move: if (_dragLastDelta == Vector3.zero) { _undo.Pop(); ClickSelect(shift); } Status("Moved"); break;
                    case Drag.FaceDrag: Status("Face moved"); break;
                }
                _drag = Drag.None;
            }
        }

        private Vector3 NudgeFromArrows()
        {
            Vector3 right = SnapAxis(cam.transform.right), fwd = SnapAxis(cam.transform.forward);
            Vector3 n = Vector3.zero;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) n -= right;
            if (Input.GetKeyDown(KeyCode.RightArrow)) n += right;
            if (Input.GetKeyDown(KeyCode.UpArrow)) n += fwd;
            if (Input.GetKeyDown(KeyCode.DownArrow)) n -= fwd;
            if (Input.GetKeyDown(KeyCode.PageUp)) n += Vector3.up;
            if (Input.GetKeyDown(KeyCode.PageDown)) n -= Vector3.up;
            return n * grid;
        }

        private static Vector3 SnapAxis(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1E-6f) return Vector3.forward;
            return MathF.Abs(v.x) > MathF.Abs(v.z) ? new Vector3(MathF.Sign(v.x), 0, 0) : new Vector3(0, 0, MathF.Sign(v.z));
        }

        private void ClickSelect(bool additive)
        {
            if (_hoverBrush != null)
            {
                if (!additive) _selBrushes.Clear();
                if (_selBrushes.Contains(_hoverBrush) && additive) _selBrushes.Remove(_hoverBrush); else if (!_selBrushes.Contains(_hoverBrush)) _selBrushes.Add(_hoverBrush);
                _selFace = _hoverFace;
                _selEntities.Clear();
                if (_selFace != null) _texIndex = Math.Max(0, _texNames.IndexOf(_selFace.texture));
            }
            else if (!additive) ClearSelection();
        }

        private void BeginCreate(Plane plane, Vector3 start)
        {
            _drag = Drag.Create;
            _dragPlane = plane;
            BrushGeometry.TextureAxes(plane.normal, out _dragU, out _dragV);
            _dragStartPoint = SnapOnPlane(start);
            _createPreview = null;
        }

        private Vector3 SnapOnPlane(Vector3 p)
        {
            // snap the in-plane coordinates, keep the point on the plane
            Vector3 onPlane = _dragPlane.Project(p);
            float u = MathF.Round(Vector3.Dot(onPlane, _dragU) / grid) * grid;
            float v = MathF.Round(Vector3.Dot(onPlane, _dragV) / grid) * grid;
            Vector3 origin = _dragPlane.normal * _dragPlane.dist;
            Vector3 basePoint = origin + _dragU * (u - Vector3.Dot(origin, _dragU)) + _dragV * (v - Vector3.Dot(origin, _dragV));
            return _dragPlane.Project(basePoint);
        }

        private void UpdateCreate(int w, int h)
        {
            var ray = MouseRay(w, h);
            if (!_dragPlane.Raycast(ray, out float t) || t < 0f) return;
            Vector3 cur = SnapOnPlane(ray.GetPoint(t));
            _createPreview = BoxFromRect(_dragStartPoint, cur, _dragPlane.normal, extrudeHeight);
        }

        private Brush BoxFromRect(Vector3 a, Vector3 b, Vector3 n, float height)
        {
            float ua = Vector3.Dot(a, _dragU), ub = Vector3.Dot(b, _dragU), va = Vector3.Dot(a, _dragV), vb = Vector3.Dot(b, _dragV);
            float uMin = MathF.Min(ua, ub), uMax = MathF.Max(ua, ub), vMin = MathF.Min(va, vb), vMax = MathF.Max(va, vb);
            if (uMax - uMin < 1E-4f || vMax - vMin < 1E-4f) return null;
            float d = Vector3.Dot(a, n);
            var brush = new Brush();
            string tex = _texNames[_texIndex];
            void AddPlane(Vector3 normal, float dist)
            {
                var f = new Face { texture = tex };
                var pl = new Plane(normal, dist);
                f.plane = pl; f.RebuildPointsFromPlane();
                brush.faces.Add(f);
            }
            AddPlane(n, d + height);          // top (extruded)
            AddPlane(-n, -d);                 // base on the drawing plane
            AddPlane(_dragU, uMax); AddPlane(-_dragU, -uMin);
            AddPlane(_dragV, vMax); AddPlane(-_dragV, -vMin);
            brush.Rebuild();
            return brush.IsValid ? brush : null;
        }

        private void FinishCreate()
        {
            if (_createPreview != null)
            {
                PushUndo();
                doc.Brushes.Add(_createPreview);
                _selBrushes.Clear(); _selBrushes.Add(_createPreview); _selFace = null;
                _createPreview = null;
                Rebuild();
                Status("Brush created (+/- push-pull face, R rotate, Ctrl+D duplicate)");
            }
            else ClickSelect(false);
        }

        private void BeginMove()
        {
            _drag = Drag.Move;
            _dragPlane = new Plane(Vector3.up, _dragStartPoint.y);
            _dragOriginals.Clear();
            foreach (var b in _selBrushes)
            {
                var pts = new List<(Vector3, Vector3, Vector3)>();
                foreach (var f in b.faces) pts.Add((f.p0, f.p1, f.p2));
                _dragOriginals.Add((b, pts));
            }
            _dragLastDelta = Vector3.zero;
            PushUndo();
        }

        private void UpdateMove(int w, int h, bool vertical)
        {
            var ray = MouseRay(w, h);
            Vector3 delta;
            if (vertical)
            {
                // vertical drag: plane facing the camera through the start point
                Vector3 n = -new Vector3(cam.transform.forward.x, 0, cam.transform.forward.z).normalized;
                var pl = new Plane(n, Vector3.Dot(n, _dragStartPoint));
                if (!pl.Raycast(ray, out float t)) return;
                delta = new Vector3(0, ray.GetPoint(t).y - _dragStartPoint.y, 0);
            }
            else
            {
                if (!_dragPlane.Raycast(ray, out float t) || t < 0f) return;
                delta = ray.GetPoint(t) - _dragStartPoint; delta.y = 0f;
            }
            delta = BrushGeometry.Snap(delta, grid);
            if (delta == _dragLastDelta) return;
            _dragLastDelta = delta;
            foreach (var (b, pts) in _dragOriginals)
            {
                for (int i = 0; i < b.faces.Count; i++) b.faces[i].SetPlane(pts[i].Item1 + delta, pts[i].Item2 + delta, pts[i].Item3 + delta);
                b.Rebuild();
            }
            Rebuild();
        }

        private void UpdateFaceDrag(int w, int h)
        {
            if (_selFace == null || _selBrushes.Count == 0) return;
            // project the mouse movement onto the face normal's screen direction
            var camT = Camera.main;
            Vector3 p0 = _dragStartPoint, p1 = _dragStartPoint + _selFace.plane.normal;
            Vector3 s0 = camT.WorldToScreenPoint(p0, w, h), s1 = camT.WorldToScreenPoint(p1, w, h);
            Vector2 dir = new Vector2(s1.x - s0.x, s1.y - s0.y);
            if (dir.sqrMagnitude < 1f) return;
            Vector2 mouse = Input.mouseDelta;
            float pixelsPerUnit = dir.magnitude;
            _faceDragAccum += Vector2.Dot(mouse, dir.normalized) / pixelsPerUnit;
            float steps = MathF.Round(_faceDragAccum / grid);
            if (steps != 0f)
            {
                float amount = steps * grid;
                _faceDragAccum -= amount;
                foreach (var b in _selBrushes) if (b.faces.Contains(_selFace)) b.MoveFace(_selFace, amount);
                Rebuild();
            }
        }

        private void PushPull(float amount)
        {
            if (_selFace == null) { Status("Select a face first (click a brush face)"); return; }
            PushUndo();
            foreach (var b in _selBrushes) if (b.faces.Contains(_selFace)) { if (!b.MoveFace(_selFace, amount)) Status("Cannot shrink further"); }
            Rebuild();
        }

        private void RotateSelection(float degrees)
        {
            PushUndo();
            Vector3 pivot = Vector3.zero;
            foreach (var b in _selBrushes) pivot += b.Center;
            pivot = BrushGeometry.Snap(pivot / _selBrushes.Count, grid);
            foreach (var b in _selBrushes) b.Rotate(Quaternion.Euler(0, degrees, 0), pivot);
            Rebuild();
            Status($"Rotated {degrees}°");
        }

        private void Duplicate()
        {
            PushUndo();
            var copies = new List<Brush>();
            foreach (var b in _selBrushes) { var c = b.Clone(); c.Translate(new Vector3(grid, 0, 0)); doc.Brushes.Add(c); copies.Add(c); }
            _selBrushes.Clear(); _selBrushes.AddRange(copies); _selFace = null;
            Rebuild();
            Status("Duplicated (drag to move)");
        }

        private void ApplyTexture(string tex, bool wholeBrush)
        {
            PushUndo();
            if (wholeBrush || _selFace == null) foreach (var b in _selBrushes) foreach (var f in b.faces) f.texture = tex;
            else _selFace.texture = tex;
            Rebuild();
            Status("Texture: " + tex);
        }

        private void ScaleTexture(float factor)
        {
            PushUndo();
            if (_selFace != null) _selFace.scale *= factor;
            else foreach (var b in _selBrushes) foreach (var f in b.faces) f.scale *= factor;
            Rebuild();
            Status("Texture scale ×" + factor);
        }

        private void FocusSelection()
        {
            Vector3 target;
            if (_selBrushes.Count > 0) target = _selBrushes[0].Center;
            else if (_selEntities.Count > 0) target = _selEntities[0].origin;
            else { var (min, max) = doc.Bounds; target = (min + max) * 0.5f; }
            cam.transform.position = target + new Vector3(0, 8f, 14f);
            cam.LookAt(target);
        }

        // ------------------------------------------------------------------ entity mode

        private void EntityModeInput(int w, int h, bool ctrl, bool shift, bool alt)
        {
            var defs = EntityRegistry.All;
            for (int i = 0; i < 10 && i < defs.Count; i++)
            {
                KeyCode k = i == 9 ? KeyCode.Alpha0 : (KeyCode)((int)KeyCode.Alpha1 + i);
                if (Input.GetKeyDown(k)) { _entIndex = i; Status("Entity: " + defs[i].classname); }
            }
            if (Input.GetKeyDown(KeyCode.Comma)) _entIndex = (_entIndex - 1 + defs.Count) % defs.Count;
            if (Input.GetKeyDown(KeyCode.Period)) _entIndex = (_entIndex + 1) % defs.Count;

            if (_selEntities.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X)) { PushUndo(); foreach (var e in _selEntities) doc.entities.Remove(e); ClearSelection(); Rebuild(); Status("Deleted"); }
                if (Input.GetKeyDown(KeyCode.R)) { PushUndo(); foreach (var e in _selEntities) e.angle = (e.angle + 45f) % 360f; Rebuild(); Status("Angle " + _selEntities[0].angle); }
                if (ctrl && Input.GetKeyDown(KeyCode.D))
                {
                    PushUndo();
                    var copies = new List<MapEntity>();
                    foreach (var e in _selEntities) { var c = e.Clone(); c.origin += new Vector3(grid, 0, 0); doc.entities.Add(c); copies.Add(c); }
                    _selEntities.Clear(); _selEntities.AddRange(copies); Rebuild();
                }
                bool plus = Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus);
                bool minus = Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus);
                if (plus || minus)
                {
                    var def = EntityRegistry.Get(_selEntities[0].classname);
                    int propIdx = shift ? 1 : 0;
                    if (def != null && def.numericProps.Count > propIdx)
                    {
                        var (key, step) = def.numericProps[propIdx];
                        PushUndo();
                        foreach (var e in _selEntities) e.SetFloat(key, MathF.Max(0f, e.GetFloat(key, float.Parse(def.defaults.TryGetValue(key, out var dv) ? dv : "0", System.Globalization.CultureInfo.InvariantCulture)) + (plus ? step : -step)));
                        Rebuild();
                        Status($"{key} = {_selEntities[0].Get(key)}");
                    }
                }
                Vector3 nudge = NudgeFromArrows();
                if (nudge != Vector3.zero) { PushUndo(); foreach (var e in _selEntities) e.origin += nudge; Rebuild(); }
            }

            if (cam.Flying) return;

            if (Input.GetMouseButtonDown(0))
            {
                _dragStartPx = Input.mousePosition;
                if (_hoverEntity != null)
                {
                    if (!shift) _selEntities.Clear();
                    if (!_selEntities.Contains(_hoverEntity)) _selEntities.Add(_hoverEntity);
                    _selBrushes.Clear(); _selFace = null;
                    _drag = Drag.MoveEntity;
                    _dragStartPoint = _hoverEntity.origin;
                    _dragPlane = new Plane(Vector3.up, _hoverEntity.origin.y);
                    _dragEntityOriginals.Clear();
                    foreach (var e in _selEntities) _dragEntityOriginals.Add((e, e.origin));
                    _dragLastDelta = Vector3.zero;
                    PushUndo();
                }
                else if (_hoverBrush != null || _hoverGround)
                {
                    PlaceEntity(defs[_entIndex], BrushGeometry.Snap(_hoverPoint, grid));
                }
                else ClearSelection();
            }

            if (_drag == Drag.MoveEntity && Input.GetMouseButton(0))
            {
                var ray = MouseRay(w, h);
                Vector3 delta = Vector3.zero;
                if (shift)
                {
                    Vector3 n = -new Vector3(cam.transform.forward.x, 0, cam.transform.forward.z).normalized;
                    var pl = new Plane(n, Vector3.Dot(n, _dragStartPoint));
                    if (pl.Raycast(ray, out float t)) delta = new Vector3(0, ray.GetPoint(t).y - _dragStartPoint.y, 0);
                }
                else if (_dragPlane.Raycast(ray, out float t) && t > 0f) { delta = ray.GetPoint(t) - _dragStartPoint; delta.y = 0f; }
                delta = BrushGeometry.Snap(delta, grid);
                if (delta != _dragLastDelta)
                {
                    _dragLastDelta = delta;
                    foreach (var (e, o) in _dragEntityOriginals) e.origin = o + delta;
                    Rebuild();
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (_drag == Drag.MoveEntity && _dragLastDelta == Vector3.zero) _undo.Pop();
                _drag = Drag.None;
            }
        }

        /// <summary>Moves the existing entity of that class (e.g. the single player start) or creates it.</summary>
        private void PlaceOrMoveUnique(string classname, Vector3 origin)
        {
            var def = EntityRegistry.Get(classname);
            var existing = doc.entities.Find(e => e.classname.Equals(classname, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { PlaceEntity(def, origin); return; }
            PushUndo();
            existing.origin = origin;
            _selEntities.Clear(); _selEntities.Add(existing); _selBrushes.Clear(); _selFace = null;
            Rebuild();
            Status($"Moved {classname} to {origin}");
        }

        // ------------------------------------------------------------------ paint mode

        private void PaintModeInput(int w, int h, bool ctrl, bool shift, bool alt)
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { paintRadius = Mathf.Max(0.25f, paintRadius * 0.75f); Status($"Brush radius {paintRadius:0.00}"); }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { paintRadius = Mathf.Min(20f, paintRadius / 0.75f); Status($"Brush radius {paintRadius:0.00}"); }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) { paintStrength = Mathf.Max(0.05f, paintStrength - 0.1f); Status($"Strength {paintStrength:0.00}"); }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) { paintStrength = Mathf.Min(1f, paintStrength + 0.1f); Status($"Strength {paintStrength:0.00}"); }
            if (Input.GetKeyDown(KeyCode.Comma)) { paintCell = Mathf.Max(0.125f, paintCell * 0.5f); Status($"Paint cell {paintCell} (new faces)"); }
            if (Input.GetKeyDown(KeyCode.Period)) { paintCell = Mathf.Min(8f, paintCell * 2f); Status($"Paint cell {paintCell} (new faces)"); }
            for (int i = 0; i < 10; i++)
            {
                KeyCode k = i == 9 ? KeyCode.Alpha0 : (KeyCode)((int)KeyCode.Alpha1 + i);
                if (Input.GetKeyDown(k)) { paintColor = PaintPresets[i]; ToHsv(paintColor, out _paintHue, out _paintSat, out _paintVal); Status("Colour preset " + (i + 1)); }
            }
            bool hsvChanged = false;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { _paintHue = Mathf.Repeat(_paintHue - 0.04f, 1f); hsvChanged = true; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { _paintHue = Mathf.Repeat(_paintHue + 0.04f, 1f); hsvChanged = true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) { _paintVal = Mathf.Clamp01(_paintVal + 0.1f); hsvChanged = true; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { _paintVal = Mathf.Clamp01(_paintVal - 0.1f); hsvChanged = true; }
            if (Input.GetKeyDown(KeyCode.PageUp)) { _paintSat = Mathf.Clamp01(_paintSat + 0.1f); hsvChanged = true; }
            if (Input.GetKeyDown(KeyCode.PageDown)) { _paintSat = Mathf.Clamp01(_paintSat - 0.1f); hsvChanged = true; }
            if (hsvChanged) paintColor = FromHsv(_paintHue, _paintSat, _paintVal);
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X))
            {
                if (_hoverFace != null && _hoverFace.paint != null) { PushUndo(); _hoverFace.paint = null; Rebuild(); Status("Face paint cleared"); }
            }
            if (cam.Flying) { _painting = false; return; }

            if (Input.GetMouseButtonDown(0) && _hoverFace != null)
            {
                if (ctrl)
                {
                    // eyedropper
                    if (_hoverFace.paint != null)
                    {
                        BrushGeometry.TextureAxes(_hoverFace.plane.normal, out var u, out var v);
                        paintColor = _hoverFace.paint.Sample(Vector3.Dot(_hoverPoint, u), Vector3.Dot(_hoverPoint, v));
                        ToHsv(paintColor, out _paintHue, out _paintSat, out _paintVal);
                        Status("Colour picked");
                    }
                    return;
                }
                PushUndo();
                _painting = true;
            }
            if (_painting && Input.GetMouseButton(0))
            {
                if (_hoverFace != null)
                {
                    Color c = shift ? Color.white : paintColor;
                    // paint every face of the hovered brush the stroke touches (lets strokes wrap around edges)
                    foreach (var f in _hoverBrush.faces)
                    {
                        if (f.polygon.Count < 3) continue;
                        if (MathF.Abs(f.plane.Distance(_hoverPoint)) > paintRadius) continue;
                        if (f != _hoverFace && Vector3.Dot(f.plane.normal, _hoverFace.plane.normal) < -0.5f) continue;
                        f.paint ??= PaintGrid.ForFace(f, paintCell);
                        BrushGeometry.TextureAxes(f.plane.normal, out var u, out var v);
                        Vector3 onFace = f.plane.Project(_hoverPoint);
                        f.paint.Splat(Vector3.Dot(onFace, u), Vector3.Dot(onFace, v), paintRadius, c, paintStrength * Time.unscaledDeltaTime * 12f);
                    }
                    _paintDirty = true;
                }
            }
            if (Input.GetMouseButtonUp(0)) _painting = false;
            if (_paintDirty) { Rebuild(); _paintDirty = false; }
        }

        private static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float max = MathF.Max(c.r, MathF.Max(c.g, c.b)), min = MathF.Min(c.r, MathF.Min(c.g, c.b));
            v = max; float d = max - min; s = max > 0f ? d / max : 0f;
            if (d <= 1E-5f) { h = 0f; return; }
            if (max == c.r) h = (c.g - c.b) / d % 6f; else if (max == c.g) h = (c.b - c.r) / d + 2f; else h = (c.r - c.g) / d + 4f;
            h = Mathf.Repeat(h / 6f, 1f);
        }

        private static Color FromHsv(float h, float s, float v)
        {
            float i = MathF.Floor(h * 6f), f = h * 6f - i;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            switch ((int)i % 6)
            {
                case 0: return new Color(v, t, p, 1f);
                case 1: return new Color(q, v, p, 1f);
                case 2: return new Color(p, v, t, 1f);
                case 3: return new Color(p, q, v, 1f);
                case 4: return new Color(t, p, v, 1f);
                default: return new Color(v, p, q, 1f);
            }
        }

        private void PlaceEntity(EntityDefinition def, Vector3 origin)
        {
            PushUndo();
            var e = new MapEntity { classname = def.classname };
            foreach (var kv in def.defaults) e.Set(kv.Key, kv.Value);
            e.origin = origin;
            doc.entities.Add(e);
            _selEntities.Clear(); _selEntities.Add(e);
            Rebuild();
            Status($"Placed {def.classname} at {origin}");
        }

        // ------------------------------------------------------------------ gizmos

        private void DrawGizmos()
        {
            // grid on the work plane around the camera
            float extent = 64f;
            Vector3 c = BrushGeometry.Snap(new Vector3(cam.transform.position.x, workHeight, cam.transform.position.z), Mathf.Max(grid, 1f));
            var gridColor = new Color(1f, 1f, 1f, 0.10f);
            var majorColor = new Color(1f, 1f, 1f, 0.25f);
            float step = Mathf.Max(grid, 0.5f);
            for (float x = -extent; x <= extent; x += step)
            {
                bool major = MathF.Abs((c.x + x) % 8f) < 1E-3f;
                DebugDraw.Line(c + new Vector3(x, 0, -extent), c + new Vector3(x, 0, extent), major ? majorColor : gridColor);
            }
            for (float z = -extent; z <= extent; z += step)
            {
                bool major = MathF.Abs((c.z + z) % 8f) < 1E-3f;
                DebugDraw.Line(c + new Vector3(-extent, 0, z), c + new Vector3(extent, 0, z), major ? majorColor : gridColor);
            }
            // axes
            DebugDraw.Line(Vector3.zero, Vector3.right * 4f, Color.red, false);
            DebugDraw.Line(Vector3.zero, Vector3.up * 4f, Color.green, false);
            DebugDraw.Line(Vector3.zero, Vector3.forward * 4f, Color.blue, false);

            if (showAllWireframes)
                foreach (var b in doc.Brushes)
                    foreach (var f in b.faces) DebugDraw.Polygon(f.polygon, new Color(0f, 0f, 0f, 0.35f));

            foreach (var b in _selBrushes)
                foreach (var f in b.faces) DebugDraw.Polygon(f.polygon, f == _selFace ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(1f, 0.85f, 0.1f, 1f), false);
            if (_hoverBrush != null && !_selBrushes.Contains(_hoverBrush) && _hoverFace != null) DebugDraw.Polygon(_hoverFace.polygon, new Color(1f, 1f, 1f, 0.9f), false);
            if (_createPreview != null) foreach (var f in _createPreview.faces) DebugDraw.Polygon(f.polygon, new Color(0.3f, 1f, 0.4f, 1f), false);

            foreach (var e in doc.entities)
            {
                var def = EntityRegistry.Get(e.classname);
                Vector3 ext = def?.pickExtents ?? new Vector3(0.5f, 0.5f, 0.5f);
                Color col = def?.gizmoColor ?? Color.white;
                bool sel = _selEntities.Contains(e);
                if (!sel) col.a = e == _hoverEntity ? 1f : 0.5f;
                Vector3 cc = e.origin + new Vector3(0, ext.y, 0);
                DebugDraw.Box(cc - ext, cc + ext, col, !sel);
                if (sel) DebugDraw.Box(cc - ext * 1.05f, cc + ext * 1.05f, new Color(1f, 0.85f, 0.1f, 1f), false);
                // facing arrow
                Vector3 fwd = Quaternion.Euler(0, e.angle, 0) * Vector3.forward;
                DebugDraw.Line(cc, cc + fwd * (ext.z + 0.8f), col, !sel);
            }

            if (mode == EditMode.Paint)
            {
                if (!cam.Flying && _hoverFace != null)
                {
                    // brush ring on the face plane
                    Vector3 n = _hoverFace.plane.normal;
                    Vector3 t = MathF.Abs(n.y) < 0.9f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.Cross(n, Vector3.right).normalized;
                    Vector3 b = Vector3.Cross(n, t).normalized;
                    Vector3 c0 = _hoverPoint + n * 0.02f;
                    const int seg = 32;
                    Vector3 prev = c0 + t * paintRadius;
                    for (int i = 1; i <= seg; i++)
                    {
                        float a = i / (float)seg * MathF.PI * 2f;
                        Vector3 p = c0 + (t * MathF.Cos(a) + b * MathF.Sin(a)) * paintRadius;
                        DebugDraw.Line(prev, p, paintColor, false);
                        prev = p;
                    }
                    DebugDraw.Line(c0, c0 + n * 0.5f, paintColor, false);
                }
            }
            else if (!cam.Flying && (_hoverGround || _hoverBrush != null))
                DebugDraw.Cross(BrushGeometry.Snap(_hoverPoint, grid), 0.25f, new Color(1f, 1f, 1f, 0.8f), false);
        }

        // ------------------------------------------------------------------ UI

        public void DrawUi(UiBatch ui)
        {
            float s = Mathf.Max(1f, ui.Scale * 2f);
            var white = new Color(1, 1, 1, 0.9f);
            string file = (doc.path ?? "untitled") + (_dirty ? "*" : "");
            string sel = mode == EditMode.Brush
                ? (_selBrushes.Count > 0 ? $"{_selBrushes.Count} brush(es){(_selFace != null ? " face " + _selFace.texture + " scale " + _selFace.scale.x : "")}" : "none")
                : (_selEntities.Count > 0 ? $"{_selEntities[0].classname} {_selEntities[0].Get("origin")} angle {_selEntities[0].angle}" + (_selEntities.Count > 1 ? $" (+{_selEntities.Count - 1})" : "") : "none");
            string line1 = $"RIP-N-RAGE LEVEL EDITOR   {file}   brushes {doc.Brushes.Count}  entities {doc.entities.Count}   FPS {Application.Current.Fps:0}";
            string modeInfo = mode == EditMode.Brush ? $"TEXTURE {_texNames[_texIndex]} (, .)"
                : mode == EditMode.Entity ? $"ENTITY {EntityRegistry.All[_entIndex].classname} (1-0 , .)"
                : $"PAINT RADIUS {paintRadius:0.00} ([ ])  STRENGTH {paintStrength:0.0} (+ -)  CELL {paintCell} (, .)";
            string line2 = $"MODE {mode.ToString().ToUpperInvariant()} (TAB)   GRID {grid}   WORK PLANE Y {workHeight} (H)   {modeInfo}   SELECTED: {sel}";
            ui.FillRect(new Rect(0, ui.Height - 30 * s, ui.Width, 30 * s), new Color(0, 0, 0, 0.55f));
            ui.DrawText(line1, new Vector2(8, ui.Height - 4), s, white);
            ui.DrawText(line2, new Vector2(8, ui.Height - 16 * s), s, new Color(0.8f, 0.9f, 1f, 0.9f));

            if (_statusTimer > 0f) ui.DrawText(_status, new Vector2(8, ui.Height - 34 * s), s, new Color(1f, 0.9f, 0.4f, 1f));

            // palette strip
            if (mode == EditMode.Paint)
            {
                float sw = 40 * s, x = 8, y = 8;
                ui.FillRect(new Rect(0, 0, ui.Width, sw + 16 * s), new Color(0, 0, 0, 0.55f));
                for (int i = 0; i < PaintPresets.Length; i++)
                {
                    ui.FillRect(new Rect(x, y, sw, sw), PaintPresets[i]);
                    ui.DrawText(i == 9 ? "0" : (i + 1).ToString(), new Vector2(x + 2, y + sw + 8 * s), s * 0.8f, white);
                    x += sw + 6 * s;
                }
                x += 10 * s;
                ui.FillRect(new Rect(x - 2, y - 2, sw * 2 + 4, sw + 4), Color.white);
                ui.FillRect(new Rect(x, y, sw * 2, sw), paintColor);
                ui.DrawText("CURRENT  ARROWS HUE/VALUE  PGUP/PGDN SAT", new Vector2(x, y + sw + 8 * s), s * 0.8f, white);
            }
            else if (mode == EditMode.Brush)
            {
                float thumb = 48 * s, x = 8, y = 8;
                ui.FillRect(new Rect(0, 0, ui.Width, thumb + 16 * s), new Color(0, 0, 0, 0.55f));
                for (int i = 0; i < _texNames.Count; i++)
                {
                    var lt = LevelTextures.Get(_texNames[i]);
                    if (!_texThumbs.TryGetValue(lt.name, out var sp)) { sp = Sprite.FromTexture(lt.Texture ?? Texture2D.White); _texThumbs[lt.name] = sp; }
                    var r = new Rect(x, y, thumb, thumb);
                    ui.Draw(sp, r, lt.visible ? lt.tint : new Color(1f, 0.5f, 1f, 1f));
                    if (i == _texIndex) { ui.FillRect(new Rect(x - 2, y - 2, thumb + 4, 2), Color.yellow); ui.FillRect(new Rect(x - 2, y + thumb, thumb + 4, 2), Color.yellow); ui.FillRect(new Rect(x - 2, y, 2, thumb), Color.yellow); ui.FillRect(new Rect(x + thumb, y, 2, thumb), Color.yellow); }
                    ui.DrawText(lt.name, new Vector2(x, y + thumb + 8 * s), s * 0.8f, white);
                    x += thumb + 10 * s;
                }
            }
            else
            {
                var defs = EntityRegistry.All;
                float lineH = 9 * s, y = 8 + lineH * defs.Count;
                ui.FillRect(new Rect(0, 0, 420 * s, y + 6), new Color(0, 0, 0, 0.55f));
                for (int i = 0; i < defs.Count; i++)
                {
                    string key = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : " ";
                    ui.DrawText($"{key} {defs[i].classname,-22} {defs[i].description}", new Vector2(8, y - i * lineH), s * 0.8f, i == _entIndex ? Color.yellow : white);
                }
            }

            if (_snapshotPanel)
            {
                float lineH = 10 * s;
                int shown = Math.Min(_snapshotList.Count, 18);
                float ph = (shown + 3) * lineH + 12, pw = 620 * s;
                float px = (ui.Width - pw) * 0.5f, py = (ui.Height - ph) * 0.5f;
                ui.FillRect(new Rect(px, py, pw, ph), new Color(0.05f, 0.05f, 0.08f, 0.92f));
                ui.DrawText($"SNAPSHOTS OF {Path.GetFileName(doc.path ?? "untitled")}   ({_snapshotList.Count})   UP/DOWN SELECT  ENTER RESTORE  DEL DELETE  F7 NEW  ESC CLOSE", new Vector2(px + 8, py + ph - 6), s * 0.85f, Color.yellow);
                if (_snapshotList.Count == 0) ui.DrawText("NO SNAPSHOTS YET - PRESS F7 (OR SAVE WITH AUTO-SNAPSHOT ON)", new Vector2(px + 8, py + ph - 6 - lineH * 1.5f), s * 0.85f, white);
                int first = Math.Max(0, Math.Min(_snapshotIndex - shown / 2, _snapshotList.Count - shown));
                for (int i = 0; i < shown; i++)
                {
                    var sn = _snapshotList[first + i];
                    bool isSel = first + i == _snapshotIndex;
                    string line = $"{(isSel ? ">" : " ")} {sn.time:yyyy-MM-dd HH:mm:ss}  {sn.name,-28} {(sn.auto ? "AUTO  " : "MANUAL")}  ~{sn.brushes} BRUSHES {sn.entities} ENTITIES  {sn.size / 1024f:0.0} KB";
                    ui.DrawText(line, new Vector2(px + 8, py + ph - 6 - lineH * (i + 2)), s * 0.85f, isSel ? Color.yellow : white);
                }
            }

            if (showHelp && !_snapshotPanel)
            {
                string[] help = mode == EditMode.Brush ? new[]
                {
                    "RMB+MOUSE LOOK  WASD/QE FLY  SHIFT FAST  WHEEL SPEED  F FOCUS",
                    "LMB DRAG ON GROUND/FACE: NEW BRUSH   LMB CLICK: SELECT (SHIFT ADD)",
                    "DRAG SELECTED: MOVE (SHIFT VERTICAL)   ALT+DRAG FACE: PUSH/PULL",
                    "+/- PUSH/PULL FACE BY GRID   ARROWS/PGUP/PGDN NUDGE   R ROTATE 90",
                    "CTRL+D DUPLICATE   DEL DELETE   , . FACE TEXTURE (SHIFT: SCALE)   T TEXTURE BRUSH",
                    "[ ] GRID   H WORK PLANE FROM HOVER   G WIREFRAMES   TAB ENTITY MODE   V PAINT MODE   P/M/N QUICK-PLACE SPAWNS",
                    "CTRL+S SAVE (+AUTO SNAPSHOT)   F7 SNAPSHOT   F8 SNAPSHOT BROWSER   CTRL+Z/Y UNDO/REDO   F5 SAVE+PLAY   F1 HIDE HELP",
                } : mode == EditMode.Paint ? new[]
                {
                    "VERTEX PAINT   LMB HOLD: PAINT FACE   SHIFT+LMB: ERASE (WHITE)   CTRL+LMB: PICK COLOUR",
                    "[ ] RADIUS   + - STRENGTH   , . CELL SIZE FOR NEWLY PAINTED FACES   DEL CLEAR HOVERED FACE",
                    "1-0 COLOUR PRESETS   LEFT/RIGHT HUE   UP/DOWN VALUE   PGUP/PGDN SATURATION",
                    "P / M / N: PLACE PLAYER START / MONSTER / SPAWNER AT CURSOR (ANY MODE)",
                    "V OR TAB BACK TO BRUSH MODE   CTRL+S SAVE   CTRL+Z/Y UNDO/REDO   F5 SAVE+PLAY",
                } : new[]
                {
                    "RMB+MOUSE LOOK  WASD/QE FLY   1-0 , . PICK ENTITY CLASS   P/M/N QUICK-PLACE PLAYER START/MONSTER/SPAWNER",
                    "LMB ON SURFACE: PLACE   LMB ON ENTITY: SELECT/DRAG (SHIFT VERTICAL)",
                    "ARROWS/PGUP/PGDN NUDGE   R ROTATE 45   +/- PROPERTY (SHIFT: 2ND)   CTRL+D DUPLICATE   DEL DELETE",
                    "TAB BRUSH MODE   CTRL+S SAVE   CTRL+Z/Y UNDO/REDO   F5 SAVE+PLAY   F1 HIDE HELP",
                };
                float y = ui.Height - 44 * s;
                ui.FillRect(new Rect(0, y - help.Length * 9 * s - 6, 640 * s, help.Length * 9 * s + 8), new Color(0, 0, 0, 0.45f));
                foreach (var l in help) { ui.DrawText(l, new Vector2(8, y), s * 0.85f, white); y -= 9 * s; }
            }
        }
    }
}
