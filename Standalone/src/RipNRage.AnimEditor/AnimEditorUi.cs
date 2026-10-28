using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Graphics;

namespace RipNRage.AnimEditor
{
    /// <summary>Drawing + mouse interaction of the editor panels (immediate mode, runs inside the UI pass).</summary>
    public sealed partial class AnimEditor
    {
        private Rect _topBar, _timeline, _left, _right, _viewport;
        private float _ppf = 12f;            // pixels per frame
        private float _trackX0, _trackX1;    // timeline track span

        private float S => MathF.Max(1f, _ui.Batch.Height / 720f);

        public void DrawUi(UiBatch ui)
        {
            _ui.BeginFrame(ui);
            _ui.FontScale = MathF.Max(1.5f, ui.Scale * 2.25f);
            float s = S;
            float topH = 30f * s, bottomH = 225f * s, leftW = (_leftTab == 1 ? 470f : 260f) * s, rightW = 320f * s;
            _topBar = new Rect(0, ui.Height - topH, ui.Width, topH);
            _timeline = new Rect(0, 0, ui.Width, bottomH);
            _left = new Rect(0, bottomH, leftW, ui.Height - bottomH - topH);
            _right = new Rect(ui.Width - rightW, bottomH, rightW, ui.Height - bottomH - topH);
            _viewport = new Rect(leftW, bottomH, ui.Width - leftW - rightW, ui.Height - bottomH - topH);
            if (!_layoutKnown) { _layoutKnown = true; ApplyRig(); }

            DrawViewportOverlay();
            DrawTopBar();
            DrawLeftPanel();
            DrawPalette();
            DrawTimeline();
            if (_showHelp) DrawHelp();
            _prompt.Draw(_ui, new Rect(_viewport.x + 20f * s, _viewport.yMax - 90f * s, _viewport.width - 40f * s, 70f * s));

            // releasing the mouse ends every drag
            if (!Input.GetMouseButton(0)) { if (_dragKind != KeyKind.None) EndKeyDrag(); _dragPlayhead = false; _dragEnd = false; }
        }

        // ------------------------------------------------------------------ top bar / viewport

        private void DrawTopBar()
        {
            var r = _topBar;
            _ui.Batch.FillRect(r, ImUi.PanelBg);
            _ui.Batch.FillRect(new Rect(r.x, r.y, r.width, 1), ImUi.PanelBorder);
            float y = r.yMax - 8f * S;
            string title = "RIP-N-RAGE ANIMATION EDITOR";
            _ui.Label(title, new Vector2(8f, y), ImUi.Accent);
            float x = 8f + _ui.TextWidth(title) + 24f;
            string doc = _clip != null ? $"CLIP {_clip.name}{(_dirtyClips.Contains(_clip.name) ? "*" : "")}" : "NO CLIP";
            if (_controllerMode && _controller != null) doc = $"CONTROLLER {_controller.name}  STATE {_previewAnim.currentStateName}  ({doc})";
            _ui.Label(doc, new Vector2(x, y), ImUi.Text);
            string right = $"{(Dirty ? "UNSAVED  " : "")}{(AnimationLibrary.Directory != null ? System.IO.Path.GetFileName(AnimationLibrary.Directory) + "/" : "NO FOLDER")}  F1 HELP  CTRL+S SAVE";
            _ui.Label(right, new Vector2(r.xMax - _ui.TextWidth(right) - 8f, y), ImUi.TextDim);
            if (Time.unscaledTime < _statusUntil)
            {
                float w = _ui.TextWidth(_status) + 16f;
                var box = new Rect(_viewport.x + (_viewport.width - w) * 0.5f, _viewport.y + 12f * S, w, 24f * S);
                _ui.Batch.FillRect(box, new Color(0.1f, 0.1f, 0.12f, 0.9f));
                _ui.Label(_status, new Vector2(box.x + 8f, box.yMax - 6f * S), ImUi.Accent);
            }
        }

        private void DrawViewportOverlay()
        {
            // frame / sprite read-out in the viewport corner
            float y = _viewport.yMax - 8f * S;
            float x = _viewport.x + 8f * S;
            string sp = _previewSr.sprite != null ? (SpriteLibrary.ReferenceOf(_previewSr.sprite) ?? _previewSr.sprite.name) : "-";
            string line1 = _clip == null ? "" : $"FRAME {CurrentFrame}/{Frames}  {_time:0.000}S  {(_playing ? "PLAYING" : "PAUSED")}  X{_previewSpeed:0.##}  {(_clip.loop ? "LOOP" : "ONCE")}  {Fps:0}FPS";
            string line2 = $"SPRITE {sp}{(_previewSr.enabled ? "" : "  (DISABLED)")}{(_onion && !_controllerMode ? "  ONION" : "")}";
            _ui.Label(line1, new Vector2(x, y), ImUi.Text); y -= _ui.LineHeight;
            _ui.Label(line2, new Vector2(x, y), ImUi.TextDim); y -= _ui.LineHeight;
            foreach (var f in _flashes)
            {
                float a = Mathf.Clamp01((f.until - Time.unscaledTime) / 0.8f);
                _ui.Label(f.text, new Vector2(x, y), new Color(1f, 0.85f, 0.3f, a), _ui.FontScale * 1.3f);
                y -= _ui.LineHeight * 1.3f;
            }
            // keyboard focus hint for the viewport
            if (_ui.Hover(_viewport) && !_ui.Modal)
            {
                if (Input.mouseScrollDelta.y != 0f) { _previewSpeed = Mathf.Clamp(_previewSpeed + Input.mouseScrollDelta.y * 0.25f, 0.1f, 4f); }
                if (Input.GetMouseButtonDown(0) && !_ui.ClickConsumed) { TogglePlay(); _ui.ClickConsumed = true; }
            }
        }

        // ------------------------------------------------------------------ left panel: clips / controller

        private void DrawLeftPanel()
        {
            var r = _left;
            _ui.Panel(r);
            float s = S;
            float tabH = 24f * s;
            var tabRow = new Rect(r.x + 4f, r.yMax - tabH - 4f, r.width - 8f, tabH);
            if (_ui.Button(new Rect(tabRow.x, tabRow.y, tabRow.width * 0.5f - 2f, tabH), "CLIPS (TAB)", _leftTab == 0)) _leftTab = 0;
            if (_ui.Button(new Rect(tabRow.x + tabRow.width * 0.5f + 2f, tabRow.y, tabRow.width * 0.5f - 2f, tabH), "CONTROLLER", _leftTab == 1)) _leftTab = 1;
            var body = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, tabRow.y - r.y - 6f);
            if (_leftTab == 0) DrawClipList(body); else DrawControllerPanel(body);
        }

        private void DrawClipList(Rect body)
        {
            float s = S;
            float btnH = 22f * s;
            var btnRow = new Rect(body.x, body.y + 2f, body.width, btnH);
            float bw = (btnRow.width - 6f) / 4f;
            if (_ui.Button(new Rect(btnRow.x, btnRow.y, bw, btnH), "NEW")) PromptNewClip();
            if (_ui.Button(new Rect(btnRow.x + bw + 2f, btnRow.y, bw, btnH), "DUP")) PromptDuplicateClip();
            if (_ui.Button(new Rect(btnRow.x + 2 * (bw + 2f), btnRow.y, bw, btnH), "RENAME")) PromptRenameClip();
            if (_ui.Button(new Rect(btnRow.x + 3 * (bw + 2f), btnRow.y, bw, btnH), "DELETE", tint: new Color(0.35f, 0.15f, 0.15f, 1f))) PromptDeleteClip();

            var list = new Rect(body.x, btnRow.yMax + 4f, body.width, body.yMax - btnRow.yMax - 4f);
            float rowH = _ui.LineHeight;
            float content = _clipNames.Count * rowH;
            _clipScroll = _ui.ScrollArea(list, _clipScroll, content);
            _ui.Batch.PushClip(list);
            float y = list.yMax + _clipScroll;
            for (int i = 0; i < _clipNames.Count; i++)
            {
                var name = _clipNames[i];
                var row = new Rect(list.x, y - rowH, list.width - 6f, rowH);
                y -= rowH;
                if (row.yMax < list.y || row.y > list.yMax) continue;
                var c = AnimationLibrary.GetClip(name);
                string right = $"{c.spriteKeys.Count}K{(c.events.Count > 0 ? " " + c.events.Count + "E" : "")}";
                bool sel = _clip == c;
                int click = _ui.Row(row, (_dirtyClips.Contains(name) ? "*" : "") + name, sel, null, right);
                if (click == 1) { if (_controllerMode) SetControllerMode(false); SelectClip(name); }
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(list, _clipScroll, content);
        }

        private void DrawControllerPanel(Rect body)
        {
            float s = S;
            float rowH = _ui.LineHeight;
            float x = body.x + 2f;
            float y = body.yMax - 2f;
            // controller picker
            var pick = new Rect(x, y - rowH, body.width - 4f, rowH);
            string cname = _controller?.name ?? "(NONE)";
            int d = _ui.Cell(pick, "< " + cname + " >", ImUi.ButtonBg, ImUi.Accent);
            if (d != 0 && _controllerNames.Count > 0)
            {
                int i = _controller != null ? _controllerNames.IndexOf(_controller.name) : -1;
                i = ((i + d) % _controllerNames.Count + _controllerNames.Count) % _controllerNames.Count;
                SelectController(_controllerNames[i]);
            }
            y -= rowH + 4f;
            var modeBtn = new Rect(x, y - rowH, body.width - 4f, rowH);
            if (_ui.Button(modeBtn, _controllerMode ? "PREVIEWING CONTROLLER (P = STOP)" : "PREVIEW CONTROLLER (P)", _controllerMode)) SetControllerMode(!_controllerMode);
            y -= rowH + 4f;
            if (_controller == null) return;

            var area = new Rect(body.x, body.y, body.width, y - body.y);
            // measure content lazily: draw into a clip and track the height
            _ui.Batch.PushClip(area);
            float top = area.yMax + _ctrlScroll;
            float cy = top;
            var g = Graph;
            float w = area.width - 8f;

            void Line(string text, Color? col = null) { _ui.Label(_ui.Fit(text, w), new Vector2(x, cy), col ?? ImUi.Text); cy -= rowH; }

            if (_controller.baseController != null)
            {
                Line($"BASE GRAPH: {_controller.baseController.name}", ImUi.TextDim);
                Line("OVERRIDES (CLICK = NEXT CLIP, RMB = PREV)", ImUi.Accent);
                foreach (var slot in g.clips.Keys.ToList())
                {
                    _controller.overrides.TryGetValue(slot, out var ov);
                    var lab = new Rect(x, cy - rowH, w * 0.42f, rowH);
                    _ui.Label(_ui.Fit(slot, lab.width - 4f), new Vector2(lab.x, cy - 3f), ImUi.Text);
                    int dd = _ui.Cell(new Rect(lab.xMax + 2f, cy - rowH + 1f, w - lab.width - 2f, rowH - 2f), ov?.name ?? "(base)", null, ov != null ? ImUi.Accent : ImUi.TextDim);
                    if (dd != 0)
                    {
                        PushUndoController();
                        var next = CycleClip(ov ?? g.clips[slot], dd);
                        if (next == null || next == g.clips[slot]) _controller.overrides.Remove(slot); else _controller.overrides[slot] = next;
                        _controller.ApplyOverride(_controller.baseController, new Dictionary<string, SpriteAnimationClip>(_controller.overrides));
                        MarkDirty(_controller);
                        if (_controllerMode) StartControllerPreview();
                    }
                    cy -= rowH;
                }
                cy -= 6f;
            }

            // parameters
            Line("PARAMETERS (CLICK = TOGGLE/FIRE IN PREVIEW)", ImUi.Accent);
            foreach (var kv in g.parameters.ToList())
            {
                var lab = new Rect(x, cy - rowH, w * 0.45f, rowH);
                _ui.Label(_ui.Fit(kv.Key, lab.width - 4f), new Vector2(lab.x, cy - 3f), ImUi.Text);
                _ui.Label(kv.Value.ToString().ToUpperInvariant(), new Vector2(lab.xMax, cy - 3f), ImUi.TextDim);
                float vx = lab.xMax + 60f * s;
                int dd = _ui.Cell(new Rect(vx, cy - rowH + 1f, w - (vx - x) - 22f * s, rowH - 2f), ParamValue(kv.Key, kv.Value), null,
                    _controllerMode && kv.Value == AnimatorParameterType.Bool && _previewAnim.GetBool(kv.Key) ? ImUi.Good : ImUi.Text);
                if (dd != 0) PokeParameter(kv.Key, kv.Value, dd);
                if (_ui.Button(new Rect(x + w - 20f * s, cy - rowH + 1f, 20f * s, rowH - 2f), "X", tint: new Color(0.35f, 0.15f, 0.15f, 1f)))
                {
                    PushUndoController();
                    g.parameters.Remove(kv.Key);
                    foreach (var st in g.states) foreach (var t in st.transitions) t.conditions.RemoveAll(c => c.parameter == kv.Key);
                    foreach (var t in g.anyStateTransitions) t.conditions.RemoveAll(c => c.parameter == kv.Key);
                    AfterGraphEdit();
                }
                cy -= rowH;
            }
            if (_ui.Button(new Rect(x, cy - rowH + 1f, 110f * s, rowH - 2f), "+ PARAMETER")) PromptNewParameter();
            cy -= rowH + 6f;

            // states
            Line("STATES  (DEF = DEFAULT, CLIP: CLICK CYCLES, SPEED: CLICK +/-)", ImUi.Accent);
            for (int si = 0; si < g.states.Count; si++)
            {
                var st = g.states[si];
                bool isCur = _controllerMode && _previewAnim.currentState == st;
                bool sel = _selState == si && _selTransition < 0;
                var row = new Rect(x, cy - rowH, w, rowH);
                if (sel) _ui.Batch.FillRect(row, ImUi.Selected);
                if (isCur) _ui.Batch.FillRect(new Rect(row.x, row.y, 3f, row.height), ImUi.Good);
                float cx = x + 6f;
                var defRect = new Rect(cx, cy - rowH + 1f, 30f * s, rowH - 2f);
                if (_ui.Button(defRect, "DEF", g.defaultState == st)) { PushUndoController(); g.defaultState = st; AfterGraphEdit(); }
                cx += defRect.width + 3f;
                var nameRect = new Rect(cx, cy - rowH, w * 0.3f, rowH);
                if (_ui.Row(nameRect, st.name, false, isCur ? ImUi.Good : ImUi.Text) == 1) { _selState = si; _selTransition = -1; }
                cx += nameRect.width + 2f;
                var clipRect = new Rect(cx, cy - rowH + 1f, w * 0.32f, rowH - 2f);
                g.clips.TryGetValue(st.clipName, out var stClip);
                var shown = _controller.clips.TryGetValue(st.clipName, out var eff) ? eff : stClip;
                int dc = _ui.Cell(clipRect, shown?.name ?? "(none)", null, shown != null ? ImUi.Text : ImUi.TextDim);
                if (dc != 0)
                {
                    PushUndoController();
                    var next = CycleClip(stClip, dc);
                    if (next == null) g.clips.Remove(st.clipName); else g.clips[st.clipName] = next;
                    AfterGraphEdit();
                }
                cx += clipRect.width + 2f;
                var spRect = new Rect(cx, cy - rowH + 1f, 44f * s, rowH - 2f);
                int ds = _ui.Cell(spRect, "X" + st.speed.ToString("0.##"));
                if (ds != 0) { PushUndoController(); st.speed = MathF.Max(0.05f, st.speed + ds * 0.25f); AfterGraphEdit(); }
                cx += spRect.width + 2f;
                if (_ui.Button(new Rect(x + w - 20f * s, cy - rowH + 1f, 20f * s, rowH - 2f), "X", tint: new Color(0.35f, 0.15f, 0.15f, 1f))) { RemoveState(si); break; }
                cy -= rowH;
                cy = DrawTransitions(si, st.transitions, x + 18f * s, cy, w - 18f * s);
            }
            if (_ui.Button(new Rect(x, cy - rowH + 1f, 90f * s, rowH - 2f), "+ STATE")) PromptNewState();
            cy -= rowH + 6f;
            Line("ANY STATE", ImUi.Accent);
            cy = DrawTransitions(-1, g.anyStateTransitions, x + 18f * s, cy, w - 18f * s);
            cy -= 4f;

            _ui.Batch.PopClip();
            float contentH = top - cy;
            _ctrlScroll = _ui.ScrollArea(area, _ctrlScroll, contentH);
            _ui.ScrollBar(area, _ctrlScroll, contentH);
        }

        /// <summary>Transition rows: [-> dest] [param] [mode] [threshold] [EXIT] [x]. Returns the new cursor y.</summary>
        private float DrawTransitions(int stateIndex, List<AnimatorTransition> list, float x, float cy, float w)
        {
            float s = S;
            float rowH = _ui.LineHeight;
            var g = Graph;
            for (int ti = 0; ti < list.Count; ti++)
            {
                var t = list[ti];
                bool sel = _selState == stateIndex && _selTransition == ti;
                var row = new Rect(x, cy - rowH, w, rowH);
                if (sel) _ui.Batch.FillRect(row, ImUi.Selected);
                float cx = x;
                var destRect = new Rect(cx, cy - rowH + 1f, w * 0.28f, rowH - 2f);
                int dd = _ui.Cell(destRect, "> " + (t.destination?.name ?? "?"));
                if (dd != 0 && g.states.Count > 0)
                {
                    PushUndoController();
                    int i = g.states.IndexOf(t.destination);
                    i = ((i + dd) % g.states.Count + g.states.Count) % g.states.Count;
                    t.destination = g.states[i];
                    AfterGraphEdit();
                }
                cx += destRect.width + 2f;
                var c = t.conditions.Count > 0 ? t.conditions[0] : null;
                var parRect = new Rect(cx, cy - rowH + 1f, w * 0.22f, rowH - 2f);
                var pnames = g.parameters.Keys.ToList();
                int dp = _ui.Cell(parRect, c?.parameter ?? "(no cond)", null, c != null ? ImUi.Text : ImUi.TextDim);
                if (dp != 0)
                {
                    PushUndoController();
                    int i = c != null ? pnames.IndexOf(c.parameter) : -1;
                    int n = pnames.Count + 1; // +1 = none
                    i = ((i + dp) % n + n) % n;
                    if (i == pnames.Count) t.conditions.Clear();
                    else
                    {
                        if (c == null) { c = new AnimatorCondition { mode = AnimatorConditionMode.If }; t.conditions.Add(c); }
                        c.parameter = pnames[i];
                        if (g.parameters[c.parameter] == AnimatorParameterType.Float || g.parameters[c.parameter] == AnimatorParameterType.Int)
                        { if (c.mode == AnimatorConditionMode.If || c.mode == AnimatorConditionMode.IfNot) c.mode = AnimatorConditionMode.Greater; }
                        else if (c.mode != AnimatorConditionMode.If && c.mode != AnimatorConditionMode.IfNot) c.mode = AnimatorConditionMode.If;
                    }
                    AfterGraphEdit();
                }
                cx += parRect.width + 2f;
                if (c != null)
                {
                    var modeRect = new Rect(cx, cy - rowH + 1f, w * 0.16f, rowH - 2f);
                    int dm = _ui.Cell(modeRect, ModeLabel(c.mode));
                    if (dm != 0)
                    {
                        PushUndoController();
                        bool numeric = g.parameters.TryGetValue(c.parameter, out var pt) && (pt == AnimatorParameterType.Float || pt == AnimatorParameterType.Int);
                        var pool = numeric ? Modes.Skip(2).ToArray() : Modes.Take(2).ToArray();
                        int i = Array.IndexOf(pool, c.mode);
                        i = ((i + dm) % pool.Length + pool.Length) % pool.Length;
                        c.mode = pool[i];
                        AfterGraphEdit();
                    }
                    cx += modeRect.width + 2f;
                    if (c.mode >= AnimatorConditionMode.Greater)
                    {
                        var thRect = new Rect(cx, cy - rowH + 1f, w * 0.1f, rowH - 2f);
                        int dt = _ui.Cell(thRect, c.threshold.ToString("0.##"));
                        if (dt != 0) { PushUndoController(); c.threshold += dt * (Shift ? 0.1f : 1f); AfterGraphEdit(); }
                        cx += thRect.width + 2f;
                    }
                }
                var exitRect = new Rect(cx, cy - rowH + 1f, 46f * s, rowH - 2f);
                if (_ui.Button(exitRect, t.hasExitTime ? $"EXIT {t.exitTime:0.##}" : "EXIT", t.hasExitTime))
                {
                    PushUndoController();
                    t.hasExitTime = !t.hasExitTime;
                    AfterGraphEdit();
                }
                if (t.hasExitTime && _ui.Hover(exitRect) && Input.mouseScrollDelta.y != 0f)
                {
                    PushUndoController(); t.exitTime = Mathf.Clamp(t.exitTime + Input.mouseScrollDelta.y * 0.1f, 0f, 1f); AfterGraphEdit();
                }
                if (_ui.Button(new Rect(x + w - 20f * s, cy - rowH + 1f, 20f * s, rowH - 2f), "X", tint: new Color(0.35f, 0.15f, 0.15f, 1f))) { RemoveTransition(stateIndex, ti); break; }
                if (_ui.Hover(row) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed) { _selState = stateIndex; _selTransition = ti; _ui.ClickConsumed = true; }
                cy -= rowH;
            }
            if (_ui.Button(new Rect(x, cy - rowH + 1f, 100f * s, rowH - 2f), "+ TRANSITION")) AddTransition(stateIndex);
            cy -= rowH + 2f;
            return cy;
        }

        private static string ModeLabel(AnimatorConditionMode m) => m switch
        {
            AnimatorConditionMode.If => "IS TRUE",
            AnimatorConditionMode.IfNot => "IS FALSE",
            AnimatorConditionMode.Greater => ">",
            AnimatorConditionMode.Less => "<",
            AnimatorConditionMode.Equals => "=",
            AnimatorConditionMode.NotEqual => "!=",
            _ => m.ToString(),
        };

        // ------------------------------------------------------------------ palette

        private void DrawPalette()
        {
            var r = _right;
            _ui.Panel(r, _ui.Fit("SPRITES  (CLICK = KEY AT PLAYHEAD)", r.width - 12f));
            float s = S;
            float rowH = _ui.LineHeight;
            var atlases = SpriteLibrary.Atlases.ToList();
            int looseCount = SpriteLibrary.LooseSprites.Count();
            int tabs = atlases.Count + (looseCount > 0 ? 1 : 0);
            _atlasIndex = Mathf.Clamp(_atlasIndex, 0, Math.Max(0, tabs - 1));
            // atlas list (two columns)
            float listTop = r.yMax - rowH - 6f;
            int cols = 2;
            float cw = (r.width - 8f) / cols;
            int rows = (tabs + cols - 1) / cols;
            for (int i = 0; i < tabs; i++)
            {
                string name = i < atlases.Count ? atlases[i].key : "LOOSE";
                int count = i < atlases.Count ? atlases[i].atlas.Sprites.Count : looseCount;
                var tab = new Rect(r.x + 4f + (i % cols) * cw, listTop - (i / cols + 1) * rowH, cw - 2f, rowH - 2f);
                if (_ui.Button(tab, $"{name} ({count})", _atlasIndex == i)) { _atlasIndex = i; _paletteScroll = 0f; }
            }
            var grid = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, listTop - rows * rowH - 8f - r.y);
            _ui.Border(grid, ImUi.PanelBorder);

            var sprites = new List<(string name, Sprite sprite)>();
            if (_atlasIndex < atlases.Count)
                foreach (var kv in atlases[_atlasIndex].atlas.Sprites.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) sprites.Add((kv.Key, kv.Value));
            else foreach (var kv in SpriteLibrary.LooseSprites) sprites.Add((kv.Key, kv.Value));

            float cell = 74f * s;
            int perRow = Math.Max(1, (int)((grid.width - 6f) / cell));
            float cellH = cell + rowH;
            int nRows = (sprites.Count + perRow - 1) / perRow;
            float content = nRows * cellH + 4f;
            _paletteScroll = _ui.ScrollArea(grid, _paletteScroll, content, cellH);
            _ui.Batch.PushClip(grid);
            Sprite hovered = null;
            var current = _previewSr.sprite;
            for (int i = 0; i < sprites.Count; i++)
            {
                int cx = i % perRow, cyi = i / perRow;
                var box = new Rect(grid.x + 3f + cx * cell, grid.yMax - 3f - (cyi + 1) * cellH + _paletteScroll + rowH, cell - 4f, cell - 4f);
                if (box.yMax < grid.y || box.y - rowH > grid.yMax) continue;
                var sp = sprites[i].sprite;
                bool hover = !_ui.Modal && _ui.Hover(box);
                bool isCurrent = sp == current;
                _ui.Batch.FillRect(box, isCurrent ? ImUi.Selected : new Color(0.14f, 0.15f, 0.18f, 1f));
                DrawSpriteFit(sp, new Rect(box.x + 3f, box.y + 3f, box.width - 6f, box.height - 6f), Color.white);
                _ui.Border(box, hover ? ImUi.Accent : isCurrent ? ImUi.ButtonOn : ImUi.PanelBorder);
                _ui.Label(_ui.Fit(sprites[i].name, box.width), new Vector2(box.x, box.y - 2f), hover ? ImUi.Accent : ImUi.TextDim);
                if (hover)
                {
                    hovered = sp;
                    if (Input.GetMouseButtonDown(0) && !_ui.ClickConsumed) { PlaceSprite(sp); _ui.ClickConsumed = true; }
                }
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(grid, _paletteScroll, content);
            if (hovered != null) _paletteSprite = hovered;
        }

        /// <summary>Draws a sprite scaled to fit a rect (keeps aspect, centred).</summary>
        private void DrawSpriteFit(Sprite sp, Rect box, Color color)
        {
            if (sp == null) return;
            float sw = sp.rect.width, sh = sp.rect.height;
            if (sw <= 0f || sh <= 0f) return;
            float k = MathF.Min(box.width / sw, box.height / sh);
            float w = sw * k, h = sh * k;
            _ui.Batch.Draw(sp, new Rect(box.x + (box.width - w) * 0.5f, box.y + (box.height - h) * 0.5f, w, h), color);
        }

        // ------------------------------------------------------------------ timeline

        private float FrameToX(float frame) => _trackX0 + (frame - _tlScroll) * _ppf;
        private int XToFrame(float x) => Mathf.RoundToInt((x - _trackX0) / _ppf) + _tlScroll;

        private void DrawTimeline()
        {
            var r = _timeline;
            _ui.Panel(r);
            float s = S;
            float rowH = _ui.LineHeight;
            float btnH = 22f * s;
            float y = r.yMax - 4f;

            // ---- transport / clip settings row
            float bx = r.x + 6f;
            Rect Next(float w) { var rr = new Rect(bx, y - btnH, w, btnH); bx += w + 3f; return rr; }
            bool hasClip = _clip != null;
            if (_ui.Button(Next(28f * s), "|<", enabled: hasClip)) { _playing = false; SetFrame(0); }
            if (_ui.Button(Next(28f * s), "<", enabled: hasClip)) { _playing = false; SetFrame(CurrentFrame - 1); }
            if (_ui.Button(Next(64f * s), _playing ? "PAUSE" : "PLAY", _playing, enabled: hasClip)) TogglePlay();
            if (_ui.Button(Next(28f * s), ">", enabled: hasClip)) { _playing = false; SetFrame(CurrentFrame + 1); }
            if (_ui.Button(Next(28f * s), ">|", enabled: hasClip)) { _playing = false; SetFrame(Frames); }
            bx += 8f;
            bool clipEdit = hasClip && !_controllerMode;
            if (_ui.Button(Next(52f * s), "LOOP", hasClip && _clip.loop, enabled: clipEdit)) { PushUndoClip(); _clip.loop = !_clip.loop; MarkDirty(_clip); }
            if (_ui.Button(Next(56f * s), "ONION", _onion, enabled: !_controllerMode)) { _onion = !_onion; SampleClip(); }
            bx += 8f;
            _ui.Label("FPS", new Vector2(bx, y - 5f * s), ImUi.TextDim); bx += 28f * s;
            int df = _ui.Cell(Next(40f * s), hasClip ? Fps.ToString("0") : "-");
            if (df != 0 && clipEdit) SetFrameRate(Fps + df * (Shift ? 10f : 1f));
            _ui.Label("LEN", new Vector2(bx, y - 5f * s), ImUi.TextDim); bx += 28f * s;
            int dl = _ui.Cell(Next(44f * s), hasClip ? Frames.ToString() : "-");
            if (dl != 0 && clipEdit) SetLength(Frames + dl * (Shift ? 5 : 1));
            _ui.Label("SPEED", new Vector2(bx, y - 5f * s), ImUi.TextDim); bx += 44f * s;
            int dv = _ui.Cell(Next(40f * s), "X" + _previewSpeed.ToString("0.##"));
            if (dv != 0) _previewSpeed = Mathf.Clamp(_previewSpeed + dv * 0.25f, 0.1f, 4f);
            bx += 8f;
            if (_ui.Button(Next(70f * s), "+ EVENT (E)", enabled: clipEdit)) PromptEvent();
            if (_ui.Button(Next(84f * s), "+ ENABLED (N)", enabled: clipEdit)) ToggleEnabledKey();
            if (_ui.Button(Next(70f * s), "DEL KEY", enabled: clipEdit && _selKind != KeyKind.None, tint: new Color(0.35f, 0.15f, 0.15f, 1f))) DeleteSelectedKey();
            if (_ui.Button(Next(60f * s), "UNDO", enabled: _undo.Count > 0)) Undo();
            if (_ui.Button(Next(60f * s), "REDO", enabled: _redo.Count > 0)) Redo();
            if (_ui.Button(Next(70f * s), Dirty ? "SAVE *" : "SAVE", Dirty)) SaveAll();
            y -= btnH + 6f;
            if (!hasClip) { _ui.Label("NO CLIP SELECTED — CTRL+N CREATES ONE", new Vector2(r.x + 10f, y), ImUi.TextDim); return; }

            // ---- tracks geometry
            float labelW = 64f * s;
            _trackX0 = r.x + labelW + 6f;
            _trackX1 = r.xMax - 12f;
            int frames = Frames;
            float fit = (_trackX1 - _trackX0) / MathF.Max(1, frames + 2);
            _ppf = MathF.Max(7f * s, MathF.Min(fit, 48f * s));
            int visible = (int)((_trackX1 - _trackX0) / _ppf);
            _tlScroll = Mathf.Clamp(_tlScroll, 0, Math.Max(0, frames + 2 - visible));

            float rulerH = rowH + 2f;
            float spriteH = 84f * s;
            float enH = 16f * s;
            float evH = rowH + 4f;
            var ruler = new Rect(_trackX0, y - rulerH, _trackX1 - _trackX0, rulerH);
            var spriteTrack = new Rect(_trackX0, ruler.y - spriteH - 2f, ruler.width, spriteH);
            var enTrack = new Rect(_trackX0, spriteTrack.y - enH - 2f, ruler.width, enH);
            var evTrack = new Rect(_trackX0, enTrack.y - evH - 2f, ruler.width, evH);
            var all = new Rect(_trackX0, evTrack.y, ruler.width, ruler.yMax - evTrack.y);

            // labels
            _ui.Label("FRAME", new Vector2(r.x + 6f, ruler.yMax - 3f), ImUi.TextDim);
            _ui.Label("SPRITE", new Vector2(r.x + 6f, spriteTrack.yMax - 3f), ImUi.TextDim);
            _ui.Label("ENABLED", new Vector2(r.x + 6f, enTrack.yMax - 1f), ImUi.TextDim);
            _ui.Label("EVENTS", new Vector2(r.x + 6f, evTrack.yMax - 3f), ImUi.TextDim);

            // wheel over the tracks scrolls horizontally
            if (_ui.Hover(all) && !_ui.Modal && Input.mouseScrollDelta.y != 0f) _tlScroll = Mathf.Clamp(_tlScroll - (int)(Input.mouseScrollDelta.y * 3), 0, Math.Max(0, frames + 2 - visible));

            _ui.Batch.PushClip(new Rect(all.x - 2f, all.y - 2f, all.width + 4f, all.height + 4f));
            // backgrounds
            _ui.Batch.FillRect(ruler, new Color(0.12f, 0.13f, 0.16f, 1f));
            _ui.Batch.FillRect(spriteTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            _ui.Batch.FillRect(enTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            _ui.Batch.FillRect(evTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            // clip extent
            float xEnd = FrameToX(frames);
            _ui.Batch.FillRect(new Rect(MathF.Max(xEnd, all.x), all.y, MathF.Max(0f, all.xMax - MathF.Max(xEnd, all.x)), all.height), new Color(0f, 0f, 0f, 0.35f));

            // ruler ticks
            int labelEvery = Math.Max(1, Mathf.CeilToInt(28f * s / _ppf));
            for (int f = _tlScroll; f <= frames + 2 && FrameToX(f) <= _trackX1; f++)
            {
                float x = FrameToX(f);
                bool major = f % labelEvery == 0;
                _ui.Batch.FillRect(new Rect(x, ruler.y, 1f, major ? ruler.height * 0.6f : ruler.height * 0.3f), major ? ImUi.TextDim : new Color(0.3f, 0.32f, 0.36f, 1f));
                if (major) _ui.Label(f.ToString(), new Vector2(x + 2f, ruler.yMax - 2f), f <= frames ? ImUi.Text : ImUi.TextDim, _ui.FontScale * 0.8f);
                if (f <= frames) _ui.Batch.FillRect(new Rect(x, spriteTrack.y, 1f, spriteTrack.height), new Color(1f, 1f, 1f, 0.04f));
            }

            // ---- sprite keys as blocks
            var keys = _clip.spriteKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                int f0 = Mathf.RoundToInt(keys[i].time * Fps);
                int f1 = i + 1 < keys.Count ? Mathf.RoundToInt(keys[i + 1].time * Fps) : Math.Max(frames, f0 + 1);
                float x0 = FrameToX(f0), x1 = MathF.Max(FrameToX(f1), x0 + _ppf);
                var block = new Rect(x0 + 1f, spriteTrack.y + 2f, x1 - x0 - 2f, spriteTrack.height - 4f);
                bool sel = _selKind == KeyKind.Sprite && _selIndex == i;
                bool active = CurrentSpriteKey() == i;
                _ui.Batch.FillRect(block, sel ? new Color(0.5f, 0.35f, 0.12f, 1f) : active ? new Color(0.2f, 0.24f, 0.3f, 1f) : new Color(0.16f, 0.18f, 0.22f, 1f));
                float thumbH = block.height - rowH - 2f;
                DrawSpriteFit(keys[i].sprite, new Rect(block.x + 2f, block.y + rowH, MathF.Max(4f, block.width - 4f), thumbH), Color.white);
                if (block.width > 30f) _ui.Label(_ui.Fit(keys[i].sprite?.name ?? "?", block.width - 4f, _ui.FontScale * 0.8f), new Vector2(block.x + 2f, block.y + rowH - 3f), sel ? ImUi.Accent : ImUi.TextDim, _ui.FontScale * 0.8f);
                _ui.Border(block, sel ? ImUi.Accent : ImUi.PanelBorder);
                // key handle at the start
                _ui.Batch.FillRect(new Rect(x0, spriteTrack.y, 2f, spriteTrack.height), sel ? ImUi.Accent : new Color(0.8f, 0.8f, 0.85f, 0.8f));
                if (clipEdit && !_ui.Modal && _ui.Hover(block) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed)
                {
                    _ui.ClickConsumed = true;
                    _selKind = KeyKind.Sprite; _selIndex = i; _playing = false;
                    SetFrame(f0);
                    PushUndoClip(); _dragKind = KeyKind.Sprite; _dragIndex = i; _dragMoved = false;
                }
            }

            // ---- enabled segments + keys
            var en = _clip.enabledKeys;
            if (en.Count == 0) _ui.Label("(ALWAYS ON)", new Vector2(enTrack.x + 4f, enTrack.yMax - 1f), ImUi.TextDim, _ui.FontScale * 0.8f);
            for (int i = 0; i < en.Count; i++)
            {
                int f0 = Mathf.RoundToInt(en[i].time * Fps);
                int f1 = i + 1 < en.Count ? Mathf.RoundToInt(en[i + 1].time * Fps) : Math.Max(frames, f0);
                float x0 = FrameToX(f0), x1 = MathF.Max(FrameToX(f1), x0 + 2f);
                _ui.Batch.FillRect(new Rect(x0, enTrack.y + 4f, x1 - x0, enTrack.height - 8f), en[i].enabled ? new Color(0.25f, 0.6f, 0.3f, 0.8f) : new Color(0.6f, 0.2f, 0.2f, 0.6f));
            }
            for (int i = 0; i < en.Count; i++)
            {
                int f0 = Mathf.RoundToInt(en[i].time * Fps);
                float x0 = FrameToX(f0);
                bool sel = _selKind == KeyKind.Enabled && _selIndex == i;
                var handle = new Rect(x0 - 4f, enTrack.y + 1f, 8f, enTrack.height - 2f);
                _ui.Batch.FillRect(handle, sel ? ImUi.Accent : en[i].enabled ? ImUi.Good : ImUi.Danger);
                _ui.Border(handle, sel ? Color.white : ImUi.PanelBorder);
                if (clipEdit && !_ui.Modal && _ui.Hover(handle) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed)
                {
                    _ui.ClickConsumed = true;
                    _selKind = KeyKind.Enabled; _selIndex = i; _playing = false;
                    SetFrame(f0);
                    PushUndoClip(); _dragKind = KeyKind.Enabled; _dragIndex = i;
                }
            }

            // ---- events (markers first, labels on top so overlapping events stay readable)
            var evs = _clip.events;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < evs.Count; i++)
                {
                    int f0 = Mathf.RoundToInt(evs[i].time * Fps);
                    float x0 = FrameToX(f0);
                    bool sel = _selKind == KeyKind.Event && _selIndex == i;
                    float tw = _ui.TextWidth(evs[i].function, _ui.FontScale * 0.8f) + 10f;
                    var marker = new Rect(x0 - 4f, evTrack.y + 2f, MathF.Max(tw, 10f), evTrack.height - 4f);
                    if (pass == 0)
                    {
                        _ui.Batch.FillRect(marker, sel ? new Color(0.5f, 0.35f, 0.12f, 1f) : new Color(0.25f, 0.3f, 0.5f, 0.9f));
                        continue;
                    }
                    _ui.Batch.FillRect(new Rect(x0 - 1f, evTrack.y, 2f, evTrack.height), sel ? ImUi.Accent : new Color(0.6f, 0.7f, 1f, 1f));
                    _ui.Label(evs[i].function, new Vector2(x0 + 4f, marker.yMax - 2f), sel ? ImUi.Accent : ImUi.Text, _ui.FontScale * 0.8f);
                    if (clipEdit && !_ui.Modal && _ui.Hover(marker) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed)
                    {
                        _ui.ClickConsumed = true;
                        _selKind = KeyKind.Event; _selIndex = i; _playing = false;
                        SetFrame(f0);
                        PushUndoClip(); _dragKind = KeyKind.Event; _dragIndex = i;
                    }
                }

            // ---- end marker (drag to change the length)
            var endHandle = new Rect(xEnd - 3f, ruler.y, 7f, ruler.height);
            _ui.Batch.FillRect(new Rect(xEnd, all.y, 2f, all.height), new Color(1f, 0.4f, 0.3f, 0.9f));
            _ui.Batch.FillRect(endHandle, _dragEnd ? ImUi.Accent : new Color(1f, 0.4f, 0.3f, 1f));
            if (clipEdit && !_ui.Modal && _ui.Hover(endHandle) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed) { _ui.ClickConsumed = true; _dragEnd = true; PushUndoClip(); }

            // ---- playhead
            float px = FrameToX(CurrentFrame);
            _ui.Batch.FillRect(new Rect(px - 1f, all.y, 2f, all.height), ImUi.Accent);
            _ui.Batch.FillRect(new Rect(px - 5f, ruler.yMax - 6f, 10f, 6f), ImUi.Accent);
            _ui.Batch.PopClip();

            // ---- scrubbing (ruler or empty track area)
            if (!_ui.Modal && _ui.Hover(all) && Input.GetMouseButtonDown(0) && !_ui.ClickConsumed)
            {
                _ui.ClickConsumed = true;
                _dragPlayhead = true; _playing = false;
                if (!_controllerMode) { _selKind = KeyKind.None; _selIndex = -1; }
            }
            if (Input.GetMouseButton(0))
            {
                int mf = XToFrame(Input.mousePosition.x);
                if (_dragPlayhead) SetFrame(mf);
                else if (_dragEnd) { _clip.length = Math.Max(0, mf) / Fps; MarkDirty(_clip); if (CurrentFrame > Frames) SetFrame(Frames); }
                else if (_dragKind != KeyKind.None)
                {
                    int cur = _dragKind == KeyKind.Sprite ? Mathf.RoundToInt(_clip.spriteKeys[Math.Min(_dragIndex, _clip.spriteKeys.Count - 1)].time * Fps) : -1;
                    if (mf != CurrentFrame || _dragMoved) { _dragMoved = true; MoveKey(_dragKind, _dragIndex, mf); SetFrame(mf); }
                }
            }
            if (_controllerMode) _ui.Label("CONTROLLER PREVIEW: TIMELINE FOLLOWS THE ANIMATOR (P TO EDIT CLIPS)", new Vector2(_trackX0, evTrack.y - 3f), ImUi.TextDim, _ui.FontScale * 0.8f);
        }

        // ------------------------------------------------------------------ help

        private void DrawHelp()
        {
            string[] lines =
            {
                "ANIMATION EDITOR KEYS",
                "SPACE PLAY/PAUSE   LEFT/RIGHT STEP (SHIFT = 5)   HOME/END   UP/DOWN PREV/NEXT KEY   WHEEL OVER VIEWPORT = SPEED",
                "PALETTE CLICK = SET SPRITE KEY AT PLAYHEAD (REPLACES A KEY ON THE SAME FRAME)   I = INSERT LAST HOVERED SPRITE",
                "DRAG KEYS / EVENT MARKERS / ENABLED HANDLES TO MOVE THEM   DRAG THE RED END MARKER TO CHANGE THE LENGTH   [ ] = LENGTH -/+",
                "E ADD EVENT   ENTER RENAME SELECTED EVENT   N TOGGLE ENABLED KEY   DEL DELETE KEY   CTRL+C/V COPY/PASTE KEY   L LOOP   O ONION SKIN",
                "CTRL+N NEW CLIP   CTRL+D DUPLICATE   F2 RENAME   SHIFT+DEL DELETE CLIP   CTRL+Z/Y UNDO/REDO   CTRL+S SAVE ALL",
                "TAB CLIPS/CONTROLLER PANEL   P PREVIEW CONTROLLER (PARAMETERS CLICKABLE, TIMELINE FOLLOWS)   - / = PREVIEW SPEED   G GAME FRAMING",
                "CONTROLLER: CELLS CYCLE WITH LMB/RMB (CLIP, DESTINATION, PARAMETER, MODE, THRESHOLD), EXIT TOGGLES EXIT TIME (WHEEL = VALUE)",
                "THE GAME HOT-RELOADS animations/ WHEN YOU SAVE.   ESC QUIT   F1 CLOSE",
            };
            float s = S;
            float w = 0f; foreach (var l in lines) w = MathF.Max(w, _ui.TextWidth(l));
            float h = lines.Length * _ui.LineHeight + 16f * s;
            var box = new Rect(_viewport.x + (_viewport.width - w - 20f) * 0.5f, _viewport.y + (_viewport.height - h) * 0.5f, w + 20f, h);
            _ui.Batch.FillRect(box, new Color(0.04f, 0.05f, 0.07f, 0.95f));
            _ui.Border(box, ImUi.Accent);
            float y = box.yMax - 8f * s;
            for (int i = 0; i < lines.Length; i++) { _ui.Label(lines[i], new Vector2(box.x + 10f, y), i == 0 ? ImUi.Accent : ImUi.Text); y -= _ui.LineHeight; }
        }
    }
}
