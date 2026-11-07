using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Graphics;
using RipNRage.ImGuiIntegration;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.AnimEditor
{
    /// <summary>
    /// The editor's Dear ImGui panels: clips / animator controller on the left, the sprite palette on the right and
    /// the timeline along the bottom. The timeline's tracks are engine-drawn into the panel (see
    /// <see cref="ImGuiViewportPanel"/>) because they are a custom drag surface, not a stack of widgets; the sprite
    /// preview is the engine's own 3D render showing through the empty central dock node.
    /// </summary>
    public sealed partial class AnimEditor
    {
        private float _ppf = 12f;            // pixels per frame
        private float _trackX0, _trackX1;    // timeline track span, in panel pixels

        private static readonly Vec4 Accent = new Vec4(1f, 0.78f, 0.25f, 1f);
        private static readonly Vec4 Dim = new Vec4(0.55f, 0.58f, 0.63f, 1f);
        private static readonly Vec4 GoodCol = new Vec4(0.3f, 0.75f, 0.35f, 1f);
        private static readonly Vec4 DangerCol = new Vec4(0.8f, 0.25f, 0.2f, 1f);

        private static readonly Color AccentC = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color TextC = new Color(0.88f, 0.9f, 0.92f, 1f);
        private static readonly Color DimC = new Color(0.55f, 0.58f, 0.63f, 1f);
        private static readonly Color BorderC = new Color(0.25f, 0.27f, 0.32f, 1f);

        /// <summary>Canvas scale: handle sizes track the display's pixel density.</summary>
        private float S => MathF.Max(1f, _imgui?.PixelRatio ?? 1f);

        /// <summary>Declares every panel for this frame. Called from Update, inside the ImGui frame.</summary>
        private void BuildUi()
        {
            DrawMenuBar();
            float statusH = ImGui.GetFrameHeight();
            uint dockspace = _imgui.BeginDockHost("##anim", statusH);
            if (!_layoutBuilt)
            {
                _layoutBuilt = true;
                if (!_imgui.HasSavedLayout)
                    ImGuiDock.BuildDefaultLayout(dockspace, ImGui.GetWindowSize(), "Clips", "Sprites", 0.24f, 0.2f, "Timeline", 0.42f);
            }
            _imgui.EndDockHost();

            DrawClipsPanel();
            DrawSpritePanel();
            DrawTimelinePanel();
            DrawStatusBar(statusH);
            if (_showHelp) DrawHelp();
            DrawPrompt();
            if (!_layoutKnown) { _layoutKnown = true; ApplyRig(); }

            // releasing the mouse ends every drag
            if (!Input.GetMouseButton(0)) { if (_dragKind != KeyKind.None) EndKeyDrag(); _dragPlayhead = false; _dragEnd = false; }
        }

        private void DrawMenuBar()
        {
            if (!ImGui.BeginMainMenuBar()) return;
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New clip...", "Ctrl+N")) PromptNewClip();
                if (ImGui.MenuItem("Duplicate clip...", null, false, _clip != null)) PromptDuplicateClip();
                if (ImGui.MenuItem("Rename clip...", "F2", false, _clip != null)) PromptRenameClip();
                if (ImGui.MenuItem("Delete clip...", null, false, _clip != null)) PromptDeleteClip();
                ImGui.Separator();
                if (ImGui.MenuItem("Save all", "Ctrl+S", false, Dirty)) SaveAll();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _undo.Count > 0)) Undo();
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _redo.Count > 0)) Redo();
                ImGui.Separator();
                bool clipEdit = _clip != null && !_controllerMode;
                if (ImGui.MenuItem("Add event...", "E", false, clipEdit)) PromptEvent();
                if (ImGui.MenuItem("Toggle enabled key", "N", false, clipEdit)) ToggleEnabledKey();
                if (ImGui.MenuItem("Delete key", "Del", false, clipEdit && _selKind != KeyKind.None)) DeleteSelectedKey();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Onion skin", "O", _onion, !_controllerMode)) { _onion = !_onion; SampleClip(); }
                if (ImGui.MenuItem("Preview controller", "P", _controllerMode)) SetControllerMode(!_controllerMode);
                if (ImGui.MenuItem("Help", "F1", _showHelp)) _showHelp = !_showHelp;
                ImGui.EndMenu();
            }

            ImGui.Spacing();
            string doc = _clip != null ? $"{_clip.name}{(_dirtyClips.Contains(_clip.name) ? " *" : "")}" : "no clip";
            if (_controllerMode && _controller != null) doc = $"{_controller.name}   state {_previewAnim.currentStateName}   ({doc})";
            ImGui.TextColored(Dirty ? Accent : Dim, doc);
            ImGui.EndMainMenuBar();
        }

        private void DrawStatusBar(float height)
        {
            if (_imgui.BeginStatusBar("##status", height))
            {
                if (Time.unscaledTime < _statusUntil)
                {
                    ImGui.TextColored(Accent, _status);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("    ");
                    ImGui.SameLine();
                }
                string sp = _previewSr.sprite != null ? (SpriteLibrary.ReferenceOf(_previewSr.sprite) ?? _previewSr.sprite.name) : "-";
                string info = _clip == null
                    ? "No clip. Ctrl+N creates one."
                    : $"frame {CurrentFrame}/{Frames}   {_time:0.000}s   {(_playing ? "playing" : "paused")}   x{_previewSpeed:0.##}   {(_clip.loop ? "loop" : "once")}   {Fps:0} fps   sprite {sp}";
                ImGui.TextColored(Dim, info);
            }
            ImGui.End();
        }

        // ------------------------------------------------------------------ left: clips / controller

        private void DrawClipsPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(300f, 600f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Clips")) { ImGui.End(); return; }
            if (ImGui.BeginTabBar("##lefttabs"))
            {
                if (ImGui.BeginTabItem("Clips")) { _leftTab = 0; DrawClipList(); ImGui.EndTabItem(); }
                var ctrlFlags = _openControllerTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                _openControllerTab = false;
                bool ctrlOpen = true;
                if (ImGui.BeginTabItem("Controller", ref ctrlOpen, ctrlFlags)) { _leftTab = 1; DrawControllerPanel(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }

        private void DrawClipList()
        {
            float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 3f) / 4f;
            if (ImGui.Button("New", new Vec2(w, 0f))) PromptNewClip();
            ImGui.SameLine();
            ImGui.BeginDisabled(_clip == null);
            if (ImGui.Button("Dup", new Vec2(w, 0f))) PromptDuplicateClip();
            ImGui.SameLine();
            if (ImGui.Button("Rename", new Vec2(w, 0f))) PromptRenameClip();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, DangerCol);
            if (ImGui.Button("Delete", new Vec2(w, 0f))) PromptDeleteClip();
            ImGui.PopStyleColor();
            ImGui.EndDisabled();

            if (!ImGui.BeginListBox("##clips", new Vec2(-1f, -1f))) return;
            foreach (var name in _clipNames)
            {
                var c = AnimationLibrary.GetClip(name);
                bool unsaved = _dirtyClips.Contains(name);
                string label = (unsaved ? "* " : "") + name + $"      {c.spriteKeys.Count}k" + (c.events.Count > 0 ? $" {c.events.Count}e" : "");
                if (unsaved) ImGui.PushStyleColor(ImGuiCol.Text, Accent);
                if (ImGui.Selectable(label, _clip == c)) { if (_controllerMode) SetControllerMode(false); SelectClip(name); }
                if (unsaved) ImGui.PopStyleColor();
            }
            ImGui.EndListBox();
        }

        private void DrawControllerPanel()
        {
            // controller picker + preview toggle
            int ci = _controller != null ? _controllerNames.IndexOf(_controller.name) : -1;
            ImGui.SetNextItemWidth(-1f);
            if (_controllerNames.Count > 0 && ImGui.Combo("##controller", ref ci, _controllerNames.ToArray(), _controllerNames.Count))
                SelectController(_controllerNames[ci]);
            if (ImGui.Button(_controllerMode ? "Previewing (P stops)" : "Preview controller (P)", new Vec2(-1f, 0f)))
                SetControllerMode(!_controllerMode);
            if (_controller == null) { ImGui.TextColored(Dim, "No controller."); return; }

            if (!ImGui.BeginChild("##graph", new Vec2(-1f, -1f))) { ImGui.EndChild(); return; }
            var g = Graph;

            if (_controller.baseController != null)
            {
                ImGui.SeparatorText("Overrides on " + _controller.baseController.name);
                foreach (var slot in g.clips.Keys.ToList())
                {
                    _controller.overrides.TryGetValue(slot, out var ov);
                    ImGui.PushID("ov" + slot);
                    ImGui.SetNextItemWidth(-140f);
                    if (ClipCombo(ov ?? g.clips[slot], out var picked))
                    {
                        PushUndoController();
                        if (picked == null || picked == g.clips[slot]) _controller.overrides.Remove(slot); else _controller.overrides[slot] = picked;
                        _controller.ApplyOverride(_controller.baseController, new Dictionary<string, SpriteAnimationClip>(_controller.overrides));
                        MarkDirty(_controller);
                        if (_controllerMode) StartControllerPreview();
                    }
                    ImGui.SameLine();
                    ImGui.TextColored(ov != null ? Accent : Dim, slot);
                    ImGui.PopID();
                }
            }

            // ---- parameters
            ImGui.SeparatorText("Parameters");
            foreach (var kv in g.parameters.ToList())
            {
                ImGui.PushID("p" + kv.Key);
                bool live = _controllerMode && kv.Value == AnimatorParameterType.Bool && _previewAnim.GetBool(kv.Key);
                if (ImGui.SmallButton(ParamValue(kv.Key, kv.Value))) PokeParameter(kv.Key, kv.Value, 1);
                ImGui.SameLine();
                ImGui.TextColored(live ? GoodCol : Dim, kv.Value.ToString().ToLowerInvariant());
                ImGui.SameLine();
                ImGui.TextUnformatted(kv.Key);
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 4f);
                if (ImGui.SmallButton("x"))
                {
                    PushUndoController();
                    g.parameters.Remove(kv.Key);
                    foreach (var st in g.states) foreach (var t in st.transitions) t.conditions.RemoveAll(c => c.parameter == kv.Key);
                    foreach (var t in g.anyStateTransitions) t.conditions.RemoveAll(c => c.parameter == kv.Key);
                    AfterGraphEdit();
                }
                ImGui.PopID();
            }
            if (ImGui.SmallButton("+ parameter")) PromptNewParameter();

            // ---- states
            ImGui.SeparatorText("States");
            for (int si = 0; si < g.states.Count; si++)
            {
                var st = g.states[si];
                bool isCur = _controllerMode && _previewAnim.currentState == st;
                ImGui.PushID("s" + si);
                bool isDefault = g.defaultState == st;
                if (ImGui.RadioButton("##def", isDefault) && !isDefault) { PushUndoController(); g.defaultState = st; AfterGraphEdit(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Default state");
                ImGui.SameLine();
                bool open = ImGui.TreeNodeEx(st.name, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth
                    | (_selState == si && _selTransition < 0 ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None));
                if (ImGui.IsItemClicked()) { _selState = si; _selTransition = -1; }
                if (isCur) { ImGui.SameLine(); ImGui.TextColored(GoodCol, "<"); }
                if (open)
                {
                    g.clips.TryGetValue(st.clipName, out var stClip);
                    var shown = _controller.clips.TryGetValue(st.clipName, out var eff) ? eff : stClip;
                    ImGui.SetNextItemWidth(-90f);
                    if (ClipCombo(shown, out var picked))
                    {
                        PushUndoController();
                        if (picked == null) g.clips.Remove(st.clipName); else g.clips[st.clipName] = picked;
                        AfterGraphEdit();
                    }
                    ImGui.SameLine();
                    ImGui.TextColored(Dim, "clip");

                    float speed = st.speed;
                    ImGui.SetNextItemWidth(-90f);
                    if (ImGui.DragFloat("speed", ref speed, 0.05f, 0.05f, 8f, "x%.2f")) { PushUndoController(); st.speed = MathF.Max(0.05f, speed); AfterGraphEdit(); }

                    DrawTransitions(si, st.transitions);
                    ImGui.PushStyleColor(ImGuiCol.Button, DangerCol);
                    if (ImGui.SmallButton("remove state")) { RemoveState(si); ImGui.PopStyleColor(); ImGui.TreePop(); ImGui.PopID(); break; }
                    ImGui.PopStyleColor();
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
            if (ImGui.SmallButton("+ state")) PromptNewState();

            ImGui.SeparatorText("Any state");
            DrawTransitions(-1, g.anyStateTransitions);
            ImGui.EndChild();
        }

        /// <summary>Clip picker shared by the state rows and the override rows. True when the choice changed.</summary>
        private bool ClipCombo(SpriteAnimationClip current, out SpriteAnimationClip picked)
        {
            picked = null;
            var names = new List<string> { "(none)" };
            names.AddRange(_clipNames);
            int i = current != null ? Math.Max(0, names.IndexOf(current.name)) : 0;
            if (!ImGui.Combo("##clip", ref i, names.ToArray(), names.Count)) return false;
            picked = i == 0 ? null : AnimationLibrary.GetClip(names[i]);
            return true;
        }

        /// <summary>Transition rows: destination, one condition and the exit time.</summary>
        private void DrawTransitions(int stateIndex, List<AnimatorTransition> list)
        {
            var g = Graph;
            for (int ti = 0; ti < list.Count; ti++)
            {
                var t = list[ti];
                ImGui.PushID("t" + ti);
                bool sel = _selState == stateIndex && _selTransition == ti;
                if (sel) ImGui.PushStyleColor(ImGuiCol.Text, Accent);

                // destination
                var stateNames = g.states.Select(s => s.name).ToArray();
                int di = Math.Max(0, Array.IndexOf(stateNames, t.destination?.name));
                ImGui.SetNextItemWidth(110f);
                if (ImGui.Combo("##dest", ref di, stateNames, stateNames.Length)) { PushUndoController(); t.destination = g.states[di]; AfterGraphEdit(); }
                if (ImGui.IsItemClicked()) { _selState = stateIndex; _selTransition = ti; }

                // condition parameter
                ImGui.SameLine();
                var c = t.conditions.Count > 0 ? t.conditions[0] : null;
                var pnames = new List<string> { "(no cond)" };
                pnames.AddRange(g.parameters.Keys);
                int pi = c != null ? Math.Max(0, pnames.IndexOf(c.parameter)) : 0;
                ImGui.SetNextItemWidth(100f);
                if (ImGui.Combo("##par", ref pi, pnames.ToArray(), pnames.Count))
                {
                    PushUndoController();
                    if (pi == 0) t.conditions.Clear();
                    else
                    {
                        if (c == null) { c = new AnimatorCondition { mode = AnimatorConditionMode.If }; t.conditions.Add(c); }
                        c.parameter = pnames[pi];
                        var pt = g.parameters[c.parameter];
                        if (pt == AnimatorParameterType.Float || pt == AnimatorParameterType.Int)
                        { if (c.mode == AnimatorConditionMode.If || c.mode == AnimatorConditionMode.IfNot) c.mode = AnimatorConditionMode.Greater; }
                        else if (c.mode != AnimatorConditionMode.If && c.mode != AnimatorConditionMode.IfNot) c.mode = AnimatorConditionMode.If;
                    }
                    AfterGraphEdit();
                }

                if (c != null)
                {
                    ImGui.SameLine();
                    bool numeric = g.parameters.TryGetValue(c.parameter, out var pt2) && (pt2 == AnimatorParameterType.Float || pt2 == AnimatorParameterType.Int);
                    var pool = numeric ? Modes.Skip(2).ToArray() : Modes.Take(2).ToArray();
                    var labels = pool.Select(ModeLabel).ToArray();
                    int mi = Math.Max(0, Array.IndexOf(pool, c.mode));
                    ImGui.SetNextItemWidth(80f);
                    if (ImGui.Combo("##mode", ref mi, labels, labels.Length)) { PushUndoController(); c.mode = pool[mi]; AfterGraphEdit(); }
                    if (c.mode >= AnimatorConditionMode.Greater)
                    {
                        ImGui.SameLine();
                        float th = c.threshold;
                        ImGui.SetNextItemWidth(70f);
                        if (ImGui.DragFloat("##th", ref th, 0.1f)) { PushUndoController(); c.threshold = th; AfterGraphEdit(); }
                    }
                }

                ImGui.SameLine();
                bool hasExit = t.hasExitTime;
                if (ImGui.Checkbox("exit", ref hasExit)) { PushUndoController(); t.hasExitTime = hasExit; AfterGraphEdit(); }
                if (hasExit)
                {
                    ImGui.SameLine();
                    float et = t.exitTime;
                    ImGui.SetNextItemWidth(70f);
                    if (ImGui.DragFloat("##exit", ref et, 0.01f, 0f, 1f)) { PushUndoController(); t.exitTime = Mathf.Clamp(et, 0f, 1f); AfterGraphEdit(); }
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("x")) { RemoveTransition(stateIndex, ti); if (sel) ImGui.PopStyleColor(); ImGui.PopID(); break; }

                if (sel) ImGui.PopStyleColor();
                ImGui.PopID();
            }
            if (ImGui.SmallButton("+ transition")) AddTransition(stateIndex);
        }

        private static string ModeLabel(AnimatorConditionMode m) => m switch
        {
            AnimatorConditionMode.If => "is true",
            AnimatorConditionMode.IfNot => "is false",
            AnimatorConditionMode.Greater => ">",
            AnimatorConditionMode.Less => "<",
            AnimatorConditionMode.Equals => "=",
            AnimatorConditionMode.NotEqual => "!=",
            _ => m.ToString(),
        };

        // ------------------------------------------------------------------ right: sprite palette

        private void DrawSpritePanel()
        {
            ImGui.SetNextWindowSize(new Vec2(320f, 600f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Sprites")) { ImGui.End(); return; }
            ImGui.TextColored(Dim, "Click a sprite to key it at the playhead");

            var atlases = SpriteLibrary.Atlases.ToList();
            int looseCount = SpriteLibrary.LooseSprites.Count();
            if (ImGui.BeginTabBar("##atlases"))
            {
                for (int i = 0; i < atlases.Count; i++)
                {
                    if (!ImGui.BeginTabItem($"{atlases[i].key} ({atlases[i].atlas.Sprites.Count})")) continue;
                    _atlasIndex = i;
                    DrawSpriteGrid(atlases[i].atlas.Sprites.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                                                           .Select(k => (k.Key, k.Value)).ToList());
                    ImGui.EndTabItem();
                }
                if (looseCount > 0 && ImGui.BeginTabItem($"Loose ({looseCount})"))
                {
                    _atlasIndex = atlases.Count;
                    DrawSpriteGrid(SpriteLibrary.LooseSprites.Select(k => (k.Key, k.Value)).ToList());
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }

        private void DrawSpriteGrid(List<(string name, Sprite sprite)> sprites)
        {
            const float cell = 84f;
            if (!ImGui.BeginChild("##grid", new Vec2(-1f, -1f))) { ImGui.EndChild(); return; }
            int cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));
            var current = _previewSr.sprite;
            for (int i = 0; i < sprites.Count; i++)
            {
                if (i % cols != 0) ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.PushID(i);
                bool isCurrent = sprites[i].sprite == current;
                if (isCurrent) ImGui.PushStyleColor(ImGuiCol.Button, Accent);
                if (SpriteButton(sprites[i].sprite, new Vec2(cell - 16f, cell - 30f))) PlaceSprite(sprites[i].sprite);
                if (ImGui.IsItemHovered()) _paletteSprite = sprites[i].sprite;
                if (isCurrent) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(sprites[i].name);
                ImGui.TextUnformatted(Truncate(sprites[i].name, 10));
                ImGui.PopID();
                ImGui.EndGroup();
            }
            ImGui.EndChild();
        }

        private static bool SpriteButton(Sprite sprite, Vec2 size)
        {
            if (sprite?.texture == null) return ImGui.Button("?", size);
            var t = sprite.texture;
            var r = sprite.rect;
            // engine textures are bottom-up, so V is flipped
            var uv0 = new Vec2(r.x / t.width, (r.y + r.height) / t.height);
            var uv1 = new Vec2((r.x + r.width) / t.width, r.y / t.height);
            return ImGui.ImageButton("##sprite", (IntPtr)t.Handle, size, uv0, uv1);
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";

        // ------------------------------------------------------------------ bottom: timeline

        private void DrawTimelinePanel()
        {
            ImGui.SetNextWindowSize(new Vec2(1000f, 260f), ImGuiCond.FirstUseEver);
            bool open = ImGui.Begin("Timeline");
            if (open)
            {
                DrawTransport();
                _timeline.DeclareInline(_imgui);
            }
            else _timeline.DeclareInline(_imgui, false);
            ImGui.End();
        }

        /// <summary>Transport and clip settings above the tracks.</summary>
        private void DrawTransport()
        {
            bool hasClip = _clip != null;
            bool clipEdit = hasClip && !_controllerMode;
            ImGui.BeginDisabled(!hasClip);
            if (ImGui.Button("|<")) { _playing = false; SetFrame(0); }
            ImGui.SameLine();
            if (ImGui.Button("<")) { _playing = false; SetFrame(CurrentFrame - 1); }
            ImGui.SameLine();
            if (ImGui.Button(_playing ? "Pause" : "Play", new Vec2(70f, 0f))) TogglePlay();
            ImGui.SameLine();
            if (ImGui.Button(">")) { _playing = false; SetFrame(CurrentFrame + 1); }
            ImGui.SameLine();
            if (ImGui.Button(">|")) { _playing = false; SetFrame(Frames); }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.BeginDisabled(!clipEdit);
            bool loop = hasClip && _clip.loop;
            if (ImGui.Checkbox("Loop", ref loop)) { PushUndoClip(); _clip.loop = loop; MarkDirty(_clip); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(_controllerMode);
            bool onion = _onion;
            if (ImGui.Checkbox("Onion", ref onion)) { _onion = onion; SampleClip(); }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.BeginDisabled(!clipEdit);
            float fps = hasClip ? Fps : 0f;
            ImGui.SetNextItemWidth(90f);
            if (ImGui.DragFloat("fps", ref fps, 1f, 1f, 240f, "%.0f")) SetFrameRate(fps);
            ImGui.SameLine();
            int len = hasClip ? Frames : 0;
            ImGui.SetNextItemWidth(90f);
            if (ImGui.DragInt("frames", ref len, 0.2f, 0, 4096)) SetLength(len);
            ImGui.EndDisabled();

            ImGui.SameLine();
            float speed = _previewSpeed;
            ImGui.SetNextItemWidth(90f);
            if (ImGui.DragFloat("speed", ref speed, 0.05f, 0.1f, 4f, "x%.2f")) _previewSpeed = Mathf.Clamp(speed, 0.1f, 4f);

            ImGui.SameLine();
            ImGui.BeginDisabled(!clipEdit);
            if (ImGui.Button("+ Event (E)")) PromptEvent();
            ImGui.SameLine();
            if (ImGui.Button("+ Enabled (N)")) ToggleEnabledKey();
            ImGui.SameLine();
            ImGui.BeginDisabled(_selKind == KeyKind.None);
            ImGui.PushStyleColor(ImGuiCol.Button, DangerCol);
            if (ImGui.Button("Del key")) DeleteSelectedKey();
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.EndDisabled();

            if (_controllerMode)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, "controller preview: the timeline follows the animator (P to edit clips)");
            }
        }

        private float FrameToX(float frame) => _trackX0 + (frame - _tlScroll) * _ppf;
        private int XToFrame(float x) => Mathf.RoundToInt((x - _trackX0) / _ppf) + _tlScroll;

        /// <summary>
        /// The tracks: ruler, sprite blocks, enabled segments, event markers, the end marker and the playhead.
        /// Engine-drawn into the timeline panel, in panel-local pixels with the origin bottom-left.
        /// </summary>
        private void DrawTimelineTracks(UiBatch batch, Rect r)
        {
            float s = S;
            float rowH = (BitmapFont.GlyphH + 1) * 2f * s + 6f;
            float scale = 2f * s, small = 1.6f * s;
            bool hasClip = _clip != null;
            if (!hasClip)
            {
                batch.DrawText("NO CLIP SELECTED - CTRL+N CREATES ONE", new Vector2(r.x + 10f, r.yMax - 10f), scale, DimC);
                return;
            }
            bool clipEdit = !_controllerMode;
            Vector2 mouse = _timeline.Mouse;
            bool panelHovered = _timeline.Hovered && !_promptOpen;

            // ---- geometry
            float labelW = 70f * s;
            _trackX0 = r.x + labelW + 6f;
            _trackX1 = r.xMax - 12f;
            int frames = Frames;
            float fit = (_trackX1 - _trackX0) / MathF.Max(1, frames + 2);
            _ppf = MathF.Max(7f * s, MathF.Min(fit, 48f * s));
            int visible = (int)((_trackX1 - _trackX0) / _ppf);
            _tlScroll = Mathf.Clamp(_tlScroll, 0, Math.Max(0, frames + 2 - visible));

            float y = r.yMax - 6f;
            float rulerH = rowH + 2f;
            float spriteH = MathF.Max(40f * s, r.height - rulerH - rowH * 2.4f - 18f);
            float enH = 16f * s;
            float evH = rowH + 4f;
            var ruler = new Rect(_trackX0, y - rulerH, _trackX1 - _trackX0, rulerH);
            var spriteTrack = new Rect(_trackX0, ruler.y - spriteH - 2f, ruler.width, spriteH);
            var enTrack = new Rect(_trackX0, spriteTrack.y - enH - 2f, ruler.width, enH);
            var evTrack = new Rect(_trackX0, enTrack.y - evH - 2f, ruler.width, evH);
            var all = new Rect(_trackX0, evTrack.y, ruler.width, ruler.yMax - evTrack.y);

            batch.DrawText("FRAME", new Vector2(r.x + 6f, ruler.yMax - 3f), small, DimC);
            batch.DrawText("SPRITE", new Vector2(r.x + 6f, spriteTrack.yMax - 3f), small, DimC);
            batch.DrawText("ENABLED", new Vector2(r.x + 6f, enTrack.yMax - 1f), small, DimC);
            batch.DrawText("EVENTS", new Vector2(r.x + 6f, evTrack.yMax - 3f), small, DimC);

            // wheel over the tracks scrolls horizontally
            if (panelHovered && all.Contains(mouse) && Input.mouseScrollDelta.y != 0f)
                _tlScroll = Mathf.Clamp(_tlScroll - (int)(Input.mouseScrollDelta.y * 3), 0, Math.Max(0, frames + 2 - visible));

            batch.PushClip(new Rect(all.x - 2f, all.y - 2f, all.width + 4f, all.height + 4f));
            batch.FillRect(ruler, new Color(0.12f, 0.13f, 0.16f, 1f));
            batch.FillRect(spriteTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            batch.FillRect(enTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            batch.FillRect(evTrack, new Color(0.1f, 0.11f, 0.13f, 1f));
            float xEnd = FrameToX(frames);
            batch.FillRect(new Rect(MathF.Max(xEnd, all.x), all.y, MathF.Max(0f, all.xMax - MathF.Max(xEnd, all.x)), all.height), new Color(0f, 0f, 0f, 0.35f));

            // ruler ticks
            int labelEvery = Math.Max(1, Mathf.CeilToInt(28f * s / _ppf));
            for (int f = _tlScroll; f <= frames + 2 && FrameToX(f) <= _trackX1; f++)
            {
                float x = FrameToX(f);
                bool major = f % labelEvery == 0;
                batch.FillRect(new Rect(x, ruler.y, 1f, major ? ruler.height * 0.6f : ruler.height * 0.3f), major ? DimC : new Color(0.3f, 0.32f, 0.36f, 1f));
                if (major) batch.DrawText(f.ToString(), new Vector2(x + 2f, ruler.yMax - 2f), small, f <= frames ? TextC : DimC);
                if (f <= frames) batch.FillRect(new Rect(x, spriteTrack.y, 1f, spriteTrack.height), new Color(1f, 1f, 1f, 0.04f));
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
                batch.FillRect(block, sel ? new Color(0.5f, 0.35f, 0.12f, 1f) : active ? new Color(0.2f, 0.24f, 0.3f, 1f) : new Color(0.16f, 0.18f, 0.22f, 1f));
                float thumbH = block.height - rowH - 2f;
                DrawSpriteFit(batch, keys[i].sprite, new Rect(block.x + 2f, block.y + rowH, MathF.Max(4f, block.width - 4f), thumbH), Color.white);
                if (block.width > 30f) batch.DrawText(Fit(batch, keys[i].sprite?.name ?? "?", block.width - 4f, small), new Vector2(block.x + 2f, block.y + rowH - 3f), small, sel ? AccentC : DimC);
                Border(batch, block, sel ? AccentC : BorderC);
                batch.FillRect(new Rect(x0, spriteTrack.y, 2f, spriteTrack.height), sel ? AccentC : new Color(0.8f, 0.8f, 0.85f, 0.8f));
                if (clipEdit && panelHovered && block.Contains(mouse) && Input.GetMouseButtonDown(0))
                {
                    _selKind = KeyKind.Sprite; _selIndex = i; _playing = false;
                    SetFrame(f0);
                    PushUndoClip(); _dragKind = KeyKind.Sprite; _dragIndex = i; _dragMoved = false;
                }
            }

            // ---- enabled segments + keys
            var en = _clip.enabledKeys;
            if (en.Count == 0) batch.DrawText("(ALWAYS ON)", new Vector2(enTrack.x + 4f, enTrack.yMax - 1f), small, DimC);
            for (int i = 0; i < en.Count; i++)
            {
                int f0 = Mathf.RoundToInt(en[i].time * Fps);
                int f1 = i + 1 < en.Count ? Mathf.RoundToInt(en[i + 1].time * Fps) : Math.Max(frames, f0);
                float x0 = FrameToX(f0), x1 = MathF.Max(FrameToX(f1), x0 + 2f);
                batch.FillRect(new Rect(x0, enTrack.y + 4f, x1 - x0, enTrack.height - 8f), en[i].enabled ? new Color(0.25f, 0.6f, 0.3f, 0.8f) : new Color(0.6f, 0.2f, 0.2f, 0.6f));
            }
            for (int i = 0; i < en.Count; i++)
            {
                float x0 = FrameToX(Mathf.RoundToInt(en[i].time * Fps));
                bool sel = _selKind == KeyKind.Enabled && _selIndex == i;
                var handle = new Rect(x0 - 4f, enTrack.y + 1f, 8f, enTrack.height - 2f);
                batch.FillRect(handle, sel ? AccentC : en[i].enabled ? new Color(0.3f, 0.75f, 0.35f, 1f) : new Color(0.8f, 0.25f, 0.2f, 1f));
                Border(batch, handle, sel ? Color.white : BorderC);
                if (clipEdit && panelHovered && handle.Contains(mouse) && Input.GetMouseButtonDown(0))
                {
                    _selKind = KeyKind.Enabled; _selIndex = i; _playing = false;
                    SetFrame(Mathf.RoundToInt(en[i].time * Fps));
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
                    float tw = batch.MeasureText(evs[i].function, small).x + 10f;
                    var marker = new Rect(x0 - 4f, evTrack.y + 2f, MathF.Max(tw, 10f), evTrack.height - 4f);
                    if (pass == 0)
                    {
                        batch.FillRect(marker, sel ? new Color(0.5f, 0.35f, 0.12f, 1f) : new Color(0.25f, 0.3f, 0.5f, 0.9f));
                        continue;
                    }
                    batch.FillRect(new Rect(x0 - 1f, evTrack.y, 2f, evTrack.height), sel ? AccentC : new Color(0.6f, 0.7f, 1f, 1f));
                    batch.DrawText(evs[i].function, new Vector2(x0 + 4f, marker.yMax - 2f), small, sel ? AccentC : TextC);
                    if (clipEdit && panelHovered && marker.Contains(mouse) && Input.GetMouseButtonDown(0))
                    {
                        _selKind = KeyKind.Event; _selIndex = i; _playing = false;
                        SetFrame(f0);
                        PushUndoClip(); _dragKind = KeyKind.Event; _dragIndex = i;
                    }
                }

            // ---- end marker (drag to change the length)
            var endHandle = new Rect(xEnd - 3f, ruler.y, 7f, ruler.height);
            batch.FillRect(new Rect(xEnd, all.y, 2f, all.height), new Color(1f, 0.4f, 0.3f, 0.9f));
            batch.FillRect(endHandle, _dragEnd ? AccentC : new Color(1f, 0.4f, 0.3f, 1f));
            if (clipEdit && panelHovered && endHandle.Contains(mouse) && Input.GetMouseButtonDown(0)) { _dragEnd = true; PushUndoClip(); }

            // ---- playhead
            float px = FrameToX(CurrentFrame);
            batch.FillRect(new Rect(px - 1f, all.y, 2f, all.height), AccentC);
            batch.FillRect(new Rect(px - 5f, ruler.yMax - 6f, 10f, 6f), AccentC);
            batch.PopClip();

            // ---- scrubbing (ruler or empty track area)
            if (panelHovered && all.Contains(mouse) && Input.GetMouseButtonDown(0) && _dragKind == KeyKind.None && !_dragEnd)
            {
                _dragPlayhead = true; _playing = false;
                if (!_controllerMode) { _selKind = KeyKind.None; _selIndex = -1; }
            }
            if (Input.GetMouseButton(0))
            {
                int mf = XToFrame(mouse.x);
                if (_dragPlayhead) SetFrame(mf);
                else if (_dragEnd) { _clip.length = Math.Max(0, mf) / Fps; MarkDirty(_clip); if (CurrentFrame > Frames) SetFrame(Frames); }
                else if (_dragKind != KeyKind.None)
                {
                    if (mf != CurrentFrame || _dragMoved) { _dragMoved = true; MoveKey(_dragKind, _dragIndex, mf); SetFrame(mf); }
                }
            }
        }

        private static void DrawSpriteFit(UiBatch batch, Sprite sprite, Rect box, Color color)
        {
            if (sprite == null) return;
            float k = MathF.Min(box.width / sprite.rect.width, box.height / sprite.rect.height);
            float w = sprite.rect.width * k, h = sprite.rect.height * k;
            batch.Draw(sprite, new Rect(box.x + (box.width - w) * 0.5f, box.y + (box.height - h) * 0.5f, w, h), color);
        }

        private static string Fit(UiBatch batch, string text, float width, float scale)
        {
            if (batch.MeasureText(text, scale).x <= width) return text;
            for (int n = text.Length - 1; n > 1; n--)
            {
                string t = text.Substring(0, n) + "~";
                if (batch.MeasureText(t, scale).x <= width) return t;
            }
            return "";
        }

        private static void Border(UiBatch batch, Rect r, Color c, float t = 1f)
        {
            batch.FillRect(new Rect(r.x, r.y, r.width, t), c);
            batch.FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            batch.FillRect(new Rect(r.x, r.y, t, r.height), c);
            batch.FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ------------------------------------------------------------------ viewport overlay

        /// <summary>Flash messages over the 3D preview (under the panels).</summary>
        public void DrawUi(UiBatch ui)
        {
            float s = MathF.Max(1f, _imgui?.PixelRatio ?? 1f);
            float y = ui.Height * 0.5f;
            foreach (var f in _flashes)
            {
                float a = Mathf.Clamp01(f.until - Time.unscaledTime);
                if (a <= 0f) continue;
                ui.DrawText(f.text, new Vector2(ui.Width * 0.5f - ui.MeasureText(f.text, 2.4f * s).x * 0.5f, y), 2.4f * s, new Color(1f, 0.9f, 0.4f, a));
                y -= (BitmapFont.GlyphH + 2) * 2.4f * s;
            }
        }

        // ------------------------------------------------------------------ prompt + help

        private void Prompt(string title, string initial, Action<string> accept)
        {
            _promptTitle = title; _promptText = initial ?? ""; _promptAccept = accept;
            _promptOpen = true; _promptFocus = true;
        }

        private void DrawPrompt()
        {
            if (_promptOpen && !ImGui.IsPopupOpen("##prompt")) ImGui.OpenPopup("##prompt");
            var centre = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vec2(0.5f, 0.5f));
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
            else if (cancel) { _promptAccept = null; _promptOpen = false; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }

        private void DrawHelp()
        {
            ImGui.SetNextWindowSize(new Vec2(780f, 520f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Help", ref _showHelp)) { ImGui.End(); return; }
            ImGui.TextWrapped("A clip is a list of sprite keys with a frame rate and a length, plus optional enabled keys and events. "
                            + "A controller is a state machine over those clips.");
            ImGui.SeparatorText("Clips");
            Help("Ctrl+N / F2", "New / rename");
            Help("Ctrl+S", "Save all");
            Help("Ctrl+Z / Ctrl+Y", "Undo / redo");
            ImGui.SeparatorText("Timeline");
            Help("Click a sprite", "Key it at the playhead");
            Help("Drag a key", "Move it; drag the red marker to change the length");
            Help("Drag the ruler", "Scrub");
            Help("Wheel", "Scroll the tracks");
            Help("E / N / Del", "Add event / toggle enabled key / delete the selected key");
            Help("Space", "Play or pause");
            Help("O", "Onion skin");
            ImGui.SeparatorText("Controller");
            Help("P", "Preview the controller");
            Help("Parameters", "Click a value to toggle or fire it while previewing");
            Help("States", "The radio button marks the default state");
            ImGui.Spacing();
            ImGui.TextColored(Dim, "The game reloads saved clips and controllers live.");
            ImGui.End();
        }

        private static void Help(string keys, string what)
        {
            ImGui.TextColored(Accent, keys);
            ImGui.SameLine(200f);
            ImGui.TextWrapped(what);
        }
    }
}
