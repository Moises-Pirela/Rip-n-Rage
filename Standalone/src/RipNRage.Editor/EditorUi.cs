using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.ImGuiIntegration;
using RipNRage.Game.Content.Level;
using RipNRage.Level;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.Editor
{
    /// <summary>
    /// The level editor's Dear ImGui panels: the tool palette (mode, grid, textures, entity classes, paint colours),
    /// the snapshot browser, the physics settings window and the help window. The level itself is the engine's 3D
    /// render, so the dock space keeps its central node empty and the viewport shows through it.
    /// </summary>
    public partial class EditorController
    {
        private ImGuiController _imgui;
        private bool _layoutBuilt;

        private static readonly Vec4 Accent = new Vec4(1f, 0.78f, 0.25f, 1f);
        private static readonly Vec4 Dim = new Vec4(0.55f, 0.58f, 0.63f, 1f);

        /// <summary>True while ImGui owns the pointer or the keyboard: the editor's own tools must stand down.</summary>
        private bool UiHasInput => _imgui != null && (_imgui.WantsMouse || _imgui.WantsKeyboard);

        private void InitUi()
        {
            _imgui = new ImGuiController(layoutName: "level-editor");
            Application.Current.Renderer.AfterUi += OnAfterUi;
        }

        private void ShutdownUi()
        {
            if (Application.Current?.Renderer != null) Application.Current.Renderer.AfterUi -= OnAfterUi;
            _imgui?.Dispose();
            _imgui = null;
        }

        /// <summary>Dear ImGui draws after the engine's UI pass, over the 3D view (see RenderPipeline.AfterUi).</summary>
        private void OnAfterUi(int width, int height) => _imgui?.Render(Application.Current.Window);

        /// <summary>Declares every panel for this frame. Called from Update, inside the ImGui frame.</summary>
        private void BuildUi()
        {
            DrawMenuBar();
            float statusH = ImGui.GetFrameHeight();
            uint dockspace = _imgui.BeginDockHost("##level", statusH);
            if (!_layoutBuilt)
            {
                _layoutBuilt = true;
                if (!_imgui.HasSavedLayout)
                    ImGuiDock.BuildDefaultLayout(dockspace, ImGui.GetWindowSize(), "Tools", null, 0.19f);
            }
            _imgui.EndDockHost();

            DrawToolsPanel();
            DrawStatusBar(statusH);
            if (_snapshotPanel) DrawSnapshotWindow();
            if (showHelp) DrawHelpWindow();
            _physicsPanel.Draw(_imgui);
        }

        private void DrawMenuBar()
        {
            if (!ImGui.BeginMainMenuBar()) return;
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Save", "Ctrl+S")) Save();
                if (ImGui.MenuItem("Save and play", "F5")) Play();
                ImGui.Separator();
                if (ImGui.MenuItem("Take snapshot", "F7")) TakeSnapshot();
                if (ImGui.MenuItem("Snapshot browser...", "F8")) OpenSnapshotPanel();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z")) Undo();
                if (ImGui.MenuItem("Redo", "Ctrl+Y")) Redo();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Mode"))
            {
                if (ImGui.MenuItem("Brush", "Tab", mode == EditMode.Brush)) SetMode(EditMode.Brush);
                if (ImGui.MenuItem("Entity", null, mode == EditMode.Entity)) SetMode(EditMode.Entity);
                if (ImGui.MenuItem("Vertex paint", "V", mode == EditMode.Paint)) SetMode(EditMode.Paint);
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Physics settings...", "F9")) { _physicsPanel.Open = !_physicsPanel.Open; if (_physicsPanel.Open) _physicsPanel.Revert(); }
                if (ImGui.MenuItem("Help", "F1", showHelp)) showHelp = !showHelp;
                ImGui.EndMenu();
            }

            ImGui.Spacing();
            ImGui.TextColored(_dirty ? Accent : Dim, (doc.path ?? "untitled") + (_dirty ? " *" : ""));
            ImGui.EndMainMenuBar();
        }

        private void SetMode(EditMode m)
        {
            mode = m; _drag = Drag.None; _painting = false;
            Status("Mode: " + m);
        }

        private void DrawStatusBar(float height)
        {
            if (_imgui.BeginStatusBar("##status", height))
            {
                if (_statusTimer > 0f)
                {
                    ImGui.TextColored(Accent, _status);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("    ");
                    ImGui.SameLine();
                }
                string sel = mode == EditMode.Brush
                    ? (_selBrushes.Count > 0 ? $"{_selBrushes.Count} brush(es){(_selFace != null ? "   face " + _selFace.texture + " scale " + _selFace.scale.x : "")}" : "nothing")
                    : (_selEntities.Count > 0 ? $"{_selEntities[0].classname} {_selEntities[0].Get("origin")} angle {_selEntities[0].angle}" + (_selEntities.Count > 1 ? $" (+{_selEntities.Count - 1})" : "") : "nothing");
                ImGui.TextColored(Dim, $"{doc.Brushes.Count} brushes   {doc.entities.Count} entities   grid {grid}   work plane y {workHeight}   selected: {sel}   {Application.Current.Fps:0} fps");
            }
            ImGui.End();
        }

        // ------------------------------------------------------------------ tools

        private void DrawToolsPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(280f, 700f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Tools")) { ImGui.End(); return; }

            int m = (int)mode;
            if (ImGui.Combo("Mode", ref m, "Brush\0Entity\0Vertex paint\0")) SetMode((EditMode)m);
            float g = grid;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat("Grid", ref g, 0.05f, 0.05f, 256f, "%.2f")) grid = MathF.Max(0.05f, g);
            float wh = workHeight;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat("Work plane y", ref wh, 0.25f, -512f, 512f, "%.2f")) workHeight = wh;

            ImGui.Separator();
            switch (mode)
            {
                case EditMode.Brush: DrawTextureList(); break;
                case EditMode.Entity: DrawEntityList(); break;
                default: DrawPaintTools(); break;
            }
            ImGui.End();
        }

        private void DrawTextureList()
        {
            ImGui.TextColored(Accent, "Textures");
            ImGui.TextColored(Dim, ", . cycles   T applies to the selected face");
            const float cell = 84f;
            if (!ImGui.BeginChild("##textures", new Vec2(-1f, -1f))) { ImGui.EndChild(); return; }
            int cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));
            for (int i = 0; i < _texNames.Count; i++)
            {
                if (i % cols != 0) ImGui.SameLine();
                var lt = LevelTextures.Get(_texNames[i]);
                ImGui.BeginGroup();
                ImGui.PushID(i);
                bool current = i == _texIndex;
                if (current) ImGui.PushStyleColor(ImGuiCol.Button, Accent);
                var tex = lt.Texture ?? Texture2D.White;
                // engine textures are bottom-up, so V is flipped; invisible textures keep their editor pink
                var tint = lt.visible ? lt.tint : new Color(1f, 0.5f, 1f, 1f);
                if (ImGui.ImageButton("##tex", (IntPtr)tex.Handle, new Vec2(cell - 16f, cell - 30f), new Vec2(0f, 1f), new Vec2(1f, 0f),
                                      new Vec4(0f, 0f, 0f, 0f), new Vec4(tint.r, tint.g, tint.b, 1f)))
                {
                    _texIndex = i;
                    ApplyTextureToSelection();
                }
                if (current) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(lt.name + (lt.visible ? "" : "  (invisible)"));
                ImGui.TextUnformatted(Truncate(lt.name, 10));
                ImGui.PopID();
                ImGui.EndGroup();
            }
            ImGui.EndChild();
        }

        private void DrawEntityList()
        {
            ImGui.TextColored(Accent, "Entity classes");
            ImGui.TextColored(Dim, "1-0 and , . pick   click a surface to place");
            var defs = EntityRegistry.All;
            if (!ImGui.BeginListBox("##entities", new Vec2(-1f, -1f))) return;
            for (int i = 0; i < defs.Count; i++)
            {
                string key = i < 9 ? (i + 1) + "  " : i == 9 ? "0  " : "   ";
                if (ImGui.Selectable(key + defs[i].classname, i == _entIndex)) _entIndex = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(defs[i].description);
            }
            ImGui.EndListBox();
        }

        private void DrawPaintTools()
        {
            ImGui.TextColored(Accent, "Vertex paint");
            float radius = paintRadius, strength = paintStrength;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat("Radius", ref radius, 0.02f, 0.05f, 20f, "%.2f")) paintRadius = radius;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat("Strength", ref strength, 0.02f, 0.05f, 1f, "%.2f")) paintStrength = strength;
            float cellSize = paintCell;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat("Cell", ref cellSize, 0.05f, 0.05f, 64f, "%.2f")) paintCell = MathF.Max(0.05f, cellSize);

            var col = new Vec4(paintColor.r, paintColor.g, paintColor.b, paintColor.a);
            if (ImGui.ColorEdit4("Colour", ref col, ImGuiColorEditFlags.NoAlpha)) paintColor = new Color(col.X, col.Y, col.Z, 1f);

            ImGui.TextColored(Dim, "Presets (1-0)");
            for (int i = 0; i < PaintPresets.Length; i++)
            {
                if (i % 5 != 0) ImGui.SameLine();
                var p = PaintPresets[i];
                ImGui.PushID(i);
                if (ImGui.ColorButton("##preset", new Vec4(p.r, p.g, p.b, 1f), ImGuiColorEditFlags.NoAlpha, new Vec2(40f, 28f))) paintColor = p;
                ImGui.PopID();
            }
            ImGui.Spacing();
            ImGui.TextWrapped("LMB paints, Shift+LMB erases, Ctrl+LMB picks the colour under the cursor. Del clears the hovered face.");
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";

        // ------------------------------------------------------------------ snapshots

        private void DrawSnapshotWindow()
        {
            var centre = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vec2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vec2(820f, 460f), ImGuiCond.Appearing);
            if (!ImGui.Begin("Snapshots", ref _snapshotPanel, ImGuiWindowFlags.NoDocking)) { ImGui.End(); return; }

            ImGui.TextColored(Accent, Path.GetFileName(doc.path ?? "untitled") + $"   ({_snapshotList.Count})");
            ImGui.SameLine();
            if (ImGui.Button("New (F7)")) { TakeSnapshot(); _snapshotList = snapshots.List(doc.path); }
            ImGui.SameLine();
            ImGui.BeginDisabled(_snapshotList.Count == 0);
            if (ImGui.Button("Restore")) RestoreSelectedSnapshot();
            ImGui.SameLine();
            if (ImGui.Button("Delete")) DeleteSelectedSnapshot();
            ImGui.EndDisabled();

            if (_snapshotList.Count == 0)
            {
                ImGui.TextColored(Dim, "No snapshots yet - press F7, or save with auto-snapshot on.");
                ImGui.End();
                return;
            }
            if (ImGui.BeginTable("##snaps", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("when", ImGuiTableColumnFlags.WidthFixed, 170f);
                ImGui.TableSetupColumn("name");
                ImGui.TableSetupColumn("kind", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableSetupColumn("contents", ImGuiTableColumnFlags.WidthFixed, 200f);
                ImGui.TableSetupColumn("size", ImGuiTableColumnFlags.WidthFixed, 80f);
                ImGui.TableHeadersRow();
                for (int i = 0; i < _snapshotList.Count; i++)
                {
                    var sn = _snapshotList[i];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable($"{sn.time:yyyy-MM-dd HH:mm:ss}##sn{i}", i == _snapshotIndex, ImGuiSelectableFlags.SpanAllColumns)) _snapshotIndex = i;
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { _snapshotIndex = i; RestoreSelectedSnapshot(); }
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(sn.name);
                    ImGui.TableSetColumnIndex(2); ImGui.TextColored(Dim, sn.auto ? "auto" : "manual");
                    ImGui.TableSetColumnIndex(3); ImGui.TextColored(Dim, $"{sn.brushes} brushes, {sn.entities} entities");
                    ImGui.TableSetColumnIndex(4); ImGui.TextColored(Dim, $"{sn.size / 1024f:0.0} KB");
                }
                ImGui.EndTable();
            }
            ImGui.End();
        }

        // ------------------------------------------------------------------ help

        private void DrawHelpWindow()
        {
            ImGui.SetNextWindowSize(new Vec2(780f, 460f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Help", ref showHelp)) { ImGui.End(); return; }
            ImGui.SeparatorText("Camera");
            Help("RMB + mouse", "Look");
            Help("WASD / Q E", "Fly (Shift faster, wheel changes the speed)");
            Help("F", "Focus the selection");
            if (mode == EditMode.Brush)
            {
                ImGui.SeparatorText("Brushes");
                Help("LMB drag", "New brush on the ground or a face");
                Help("LMB click", "Select (Shift adds)");
                Help("Drag selection", "Move (Shift moves vertically)");
                Help("Alt + drag face", "Push / pull");
                Help("+ / -", "Push or pull the face by one grid step");
                Help("Arrows, PgUp/PgDn", "Nudge");
                Help("R / Ctrl+D / Del", "Rotate 90, duplicate, delete");
                Help(", .", "Face texture (Shift: scale)");
                Help("T", "Apply the selected texture");
                Help("[ ] / H / G", "Grid, work plane from the cursor, wireframes");
            }
            else if (mode == EditMode.Paint)
            {
                ImGui.SeparatorText("Vertex paint");
                Help("LMB", "Paint the face under the cursor");
                Help("Shift + LMB", "Erase back to white");
                Help("Ctrl + LMB", "Pick the colour under the cursor");
                Help("[ ] / + -", "Radius / strength");
                Help(", .", "Cell size for newly painted faces");
                Help("1-0, arrows, PgUp/PgDn", "Presets, hue, value, saturation");
            }
            else
            {
                ImGui.SeparatorText("Entities");
                Help("1-0 and , .", "Pick the class");
                Help("LMB on a surface", "Place");
                Help("LMB on an entity", "Select or drag (Shift vertical)");
                Help("R / + - / Ctrl+D", "Rotate 45, property, duplicate");
            }
            ImGui.SeparatorText("Anywhere");
            Help("P / M / N", "Place the player start, a monster or a spawner at the cursor");
            Help("Tab / V", "Next mode / vertex paint");
            Help("Ctrl+S / F5", "Save (with an auto snapshot) / save and play");
            Help("F7 / F8 / F9", "Snapshot / browser / physics settings");
            Help("Ctrl+Z / Ctrl+Y", "Undo / redo");
            ImGui.End();
        }

        private static void Help(string keys, string what)
        {
            ImGui.TextColored(Accent, keys);
            ImGui.SameLine(220f);
            ImGui.TextWrapped(what);
        }
    }
}
