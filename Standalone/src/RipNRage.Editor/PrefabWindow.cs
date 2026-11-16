using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Game.Content.Level;
using RipNRage.ImGuiIntegration;
using RipNRage.Level;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.Editor
{
    /// <summary>
    /// The Prefab window (10.6): the template behind a class, edited as a whole — its node tree (children and their
    /// flags), every component's defaults, the components themselves (add / remove), the key aliases a map may use
    /// and the editor block. Edits apply to the library at once so every instance in the level re-previews as you
    /// type; Save writes the file, Revert re-reads it. "Save as prefab" from the Entity window opens it on a copy of
    /// an instance with that instance's overrides folded in. A saved file is regenerated: its comments are not kept.
    /// </summary>
    public sealed class PrefabWindow
    {
        public bool IsOpen;
        private Prefab _doc;            // a fresh, editable tree (never the library's cached one)
        private Prefab _reference;      // the base's tree, for "what differs"
        private string _name = "", _base, _file;
        private Prefab _node;           // the selected node
        private string _newChild = "", _newAlias = "", _newAliasPath = "", _status = "";
        private float _statusUntil;
        private int _addIndex;
        private bool _dirty;
        private MapEntity _retarget;    // "save as": the instance that becomes this prefab's first user
        private readonly List<PropertyRow> _rows = new List<PropertyRow>();
        private string _rowsFor;
        private string _filter = "";

        private static readonly Vec4 Accent = PropertyGrid.Accent, Dim = PropertyGrid.Dim;

        /// <summary>Opens an existing prefab for editing.</summary>
        public void Open(string prefabName)
        {
            var lib = GamePrefabs.Library;
            if (!lib.Contains(prefabName)) { Status("no prefab " + prefabName); return; }
            _doc = lib.Fresh(prefabName);
            _name = prefabName;
            _base = lib.BaseOf(prefabName);
            _reference = _base != null && lib.Contains(_base) ? lib.Fresh(_base) : null;
            _file = lib.SourceOf(prefabName) ?? Path.Combine(GamePrefabs.LevelFolder, prefabName + ".json");
            _node = _doc; _retarget = null; _dirty = false; _rowsFor = null;
            IsOpen = true;
        }

        /// <summary>Opens a new prefab made from an instance: its class's template with the instance's overrides folded in.</summary>
        public void NewFrom(MapEntity instance, EditorController editor)
        {
            var def = EntityRegistry.Get(instance.classname);
            if (def?.prefab == null) { Status("that class is not a prefab"); return; }
            var lib = GamePrefabs.Library;
            var overrides = instance.properties.Where(kv => !IsUniversal(kv.Key)).ToList();
            try { _doc = overrides.Count == 0 ? lib.Fresh(def.prefab) : lib.Get(def.prefab, overrides); }
            catch (Exception e) { Status(e.Message); return; }
            string stem = def.prefab;
            int n = 2; _name = stem + "_2";
            while (lib.Contains(_name)) _name = stem + "_" + (++n);
            _doc.Name = _name;
            _base = null; _reference = null;
            _file = Path.Combine(GamePrefabs.LevelFolder, _name + ".json");
            _node = _doc; _retarget = instance; _dirty = true; _rowsFor = null;
            IsOpen = true;
            Status($"a new prefab from this {def.prefab}: name it, then Save");
        }

        private static bool IsUniversal(string key) =>
            key.Equals("classname", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("angle", StringComparison.OrdinalIgnoreCase) || key.Equals("targetname", StringComparison.OrdinalIgnoreCase);

        private void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 5f; }

        // ------------------------------------------------------------------ the window

        public void Draw(EditorController editor)
        {
            if (!IsOpen || _doc == null) return;
            ImGui.SetNextWindowSize(new Vec2(520f, 720f), ImGuiCond.FirstUseEver);
            bool open = IsOpen;
            if (!ImGui.Begin("Prefab", ref open)) { IsOpen = open; ImGui.End(); return; }
            IsOpen = open;

            // ---- header
            ImGui.SetNextItemWidth(200f);
            string name = _name;
            if (ImGui.InputText("name", ref name, 64) && _retarget != null) { _name = name; _doc.Name = name; _file = Path.Combine(GamePrefabs.LevelFolder, name + ".json"); }
            if (_retarget == null && ImGui.IsItemHovered()) ImGui.SetTooltip("an existing prefab keeps its name (its file and every map that names it)");
            if (_base != null) { ImGui.SameLine(); ImGui.TextColored(Dim, "base: " + _base); }
            string desc = _doc.Description ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##desc", ref desc, 256)) { _doc.Description = desc; Changed(editor); }
            ImGui.TextColored(Dim, _file);
            if (ImGui.Button(_dirty ? "Save *" : "Save")) Save(editor);
            ImGui.SameLine();
            if (ImGui.Button("Revert")) Revert(editor);
            if (Time.unscaledTime < _statusUntil) ImGui.TextColored(Accent, _status);
            ImGui.Separator();

            if (ImGui.BeginTabBar("##prefab"))
            {
                if (ImGui.BeginTabItem("Components")) { DrawTree(editor); ImGui.Separator(); DrawComponents(editor); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Keys")) { DrawKeys(editor); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Editor")) { DrawEditorBlock(editor); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }

        // ---- the node tree: root and children, flags on the selected node
        private void DrawTree(EditorController editor)
        {
            ImGui.TextColored(Dim, "Nodes - the root and its children; a detached child is posed like the root with no Parent");
            DrawNode(_doc, _doc, editor);
            if (_node != null)
            {
                bool det = _node.Detached, po = _node.PreviewOnly, go = _node.GameOnly;
                if (_node != _doc)
                {
                    if (ImGui.Checkbox("detached", ref det)) { _node.Detached = det; Changed(editor); }
                    ImGui.SameLine(); if (ImGui.Checkbox("previewOnly", ref po)) { _node.PreviewOnly = po; Changed(editor); }
                    ImGui.SameLine(); if (ImGui.Checkbox("gameOnly", ref go)) { _node.GameOnly = go; Changed(editor); }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("remove child")) { RemoveChild(_doc, _node); _node = _doc; Changed(editor); }
                }
                ImGui.SetNextItemWidth(160f);
                ImGui.InputTextWithHint("##newchild", "new child name", ref _newChild, 32);
                ImGui.SameLine();
                if (ImGui.SmallButton("add child") && _newChild.Trim().Length > 0 && FindNode(_doc, _newChild.Trim()) == null)
                {
                    var child = new Prefab { Name = _newChild.Trim() };
                    child.Components.Add(new RipNRage.Engine.Ecs.Transforms.LocalTransformConfig());
                    _node.Children.Add(child); _node = child; _newChild = ""; Changed(editor);
                }
            }
        }

        private void DrawNode(Prefab node, Prefab root, EditorController editor)
        {
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (node == _node) flags |= ImGuiTreeNodeFlags.Selected;
            if (node.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
            string label = node == root ? "root" : node.Name;
            string tags = (node.Detached ? " [detached]" : "") + (node.PreviewOnly ? " [preview]" : "") + (node.GameOnly ? " [game]" : "");
            bool openNode = ImGui.TreeNodeEx(label + tags + $"  ({node.Components.Count})##" + label, flags);
            if (ImGui.IsItemClicked()) { _node = node; _rowsFor = null; }
            if (openNode)
            {
                foreach (var c in node.Children.ToList()) DrawNode(c, root, editor);
                ImGui.TreePop();
            }
        }

        private static Prefab FindNode(Prefab root, string name)
        {
            foreach (var c in root.Children) { if (c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return c; var d = FindNode(c, name); if (d != null) return d; }
            return null;
        }

        private static void RemoveChild(Prefab root, Prefab child)
        {
            if (root.Children.Remove(child)) return;
            foreach (var c in root.Children) RemoveChild(c, child);
        }

        // ---- the selected node's components: defaults, add / remove
        private void DrawComponents(EditorController editor)
        {
            if (_node == null) return;
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##filter", "filter fields", ref _filter, 64);
            string key = (_node == _doc ? "root" : _node.Name) + ":" + _node.Components.Count;
            if (_rowsFor != key) { BuildRows(editor); _rowsFor = key; }
            if (ImGui.BeginChild("##comps", new Vec2(0f, -34f)))
                PropertyGrid.Draw(_rows, _filter, editor.Sources, 170f, group =>
                {
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 4f);
                    if (ImGui.SmallButton("x##" + group))
                    {
                        var cfg = _node.Components.Find(c => c.ComponentType.Name == group);
                        if (cfg != null) { _node.Components.Remove(cfg); _rowsFor = null; Changed(editor); }
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("remove this component from the prefab");
                });
            ImGui.EndChild();
            var present = new HashSet<string>(_node.Components.Select(c => c.ComponentType.Name));
            var names = PrefabLibrary.ConfigNames().Where(n => !present.Contains(n)).OrderBy(n => n).ToArray();
            if (names.Length == 0) return;
            _addIndex = Math.Clamp(_addIndex, 0, names.Length - 1);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
            ImGui.Combo("##add", ref _addIndex, names, names.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                var type = ComponentRegistry.FindConfig(names[_addIndex]);
                if (type != null) { _node.Components.Add((IComponentConfig)Activator.CreateInstance(type)); _rowsFor = null; Changed(editor); }
            }
        }

        private void BuildRows(EditorController editor)
        {
            _rows.Clear();
            var refNode = _node == _doc ? _reference : _reference != null ? FindNode(_reference, _node.Name) : null;
            foreach (var cfg in _node.Components)
            {
                string comp = cfg.ComponentType.Name;
                var refCfg = refNode?.Components.Find(c => c.GetType() == cfg.GetType()) ?? (IComponentConfig)Activator.CreateInstance(cfg.GetType());
                foreach (var f in cfg.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.GetCustomAttribute<InspectHiddenAttribute>() != null) continue;
                    var row = PropertyRow.ForField(f, cfg, refCfg, comp);
                    row.Id = comp + "." + f.Name;
                    if (f.FieldType == typeof(float[][])) row.ReadOnly = true;
                    var field = f; var target = cfg; var reference = refCfg;
                    row.Set = v => { field.SetValue(target, v); Changed(editor); };
                    row.Revert = () => { field.SetValue(target, field.GetValue(reference)); row.Value = field.GetValue(target); row.Overridden = false; Changed(editor); };
                    _rows.Add(row);
                }
            }
        }

        // ---- keys: alias → path(s)
        private void DrawKeys(EditorController editor)
        {
            ImGui.TextWrapped("The short keys a map may write for this class, and the field path(s) each one drives (Config.field, or child/Config.field; several paths separated by commas).");
            if (ImGui.BeginTable("##keys", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("key", ImGuiTableColumnFlags.WidthFixed, 120f);
                ImGui.TableSetupColumn("drives");
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 24f);
                ImGui.TableHeadersRow();
                foreach (var kv in _doc.Keys.ToList())
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(kv.Key);
                    ImGui.TableSetColumnIndex(1);
                    string paths = string.Join(", ", kv.Value);
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputText("##p" + kv.Key, ref paths, 256) && ImGui.IsItemDeactivatedAfterEdit() == false) { }
                    if (ImGui.IsItemDeactivatedAfterEdit()) { _doc.Keys[kv.Key] = paths.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray(); Changed(editor); }
                    ImGui.TableSetColumnIndex(2);
                    if (ImGui.SmallButton("x##k" + kv.Key)) { _doc.Keys.Remove(kv.Key); Changed(editor); }
                }
                ImGui.EndTable();
            }
            ImGui.SetNextItemWidth(120f); ImGui.InputTextWithHint("##na", "key", ref _newAlias, 32);
            ImGui.SameLine(); ImGui.SetNextItemWidth(260f); ImGui.InputTextWithHint("##np", "Config.field", ref _newAliasPath, 128);
            ImGui.SameLine();
            if (ImGui.SmallButton("add") && _newAlias.Trim().Length > 0 && _newAliasPath.Trim().Length > 0)
            {
                _doc.Keys[_newAlias.Trim()] = _newAliasPath.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                _newAlias = ""; _newAliasPath = ""; Changed(editor);
            }
        }

        // ---- the editor block: gizmo colour, pick extents, placement defaults, the + / - keys
        private void DrawEditorBlock(EditorController editor)
        {
            _doc.Extra ??= new JsonObject();
            if (_doc.Extra["editor"] is not JsonObject ed) _doc.Extra["editor"] = ed = new JsonObject();
            var col = Floats(ed["gizmoColor"], 4, new[] { 1f, 1f, 1f, 1f });
            var c4 = new Vec4(col[0], col[1], col[2], col[3]);
            if (ImGui.ColorEdit4("gizmo colour", ref c4)) { ed["gizmoColor"] = Arr(new[] { c4.X, c4.Y, c4.Z, c4.W }); Changed(editor); }
            var ext = Floats(ed["pickExtents"], 3, new[] { 0.5f, 0.5f, 0.5f });
            var v3 = new System.Numerics.Vector3(ext[0], ext[1], ext[2]);
            if (ImGui.DragFloat3("pick extents", ref v3, 0.05f, 0.05f, 50f)) { ed["pickExtents"] = Arr(new[] { v3.X, v3.Y, v3.Z }); Changed(editor); }
            ImGui.SeparatorText("defaults written when the editor places one");
            if (ed["defaults"] is not JsonObject defs) ed["defaults"] = defs = new JsonObject();
            foreach (var kv in defs.ToList())
            {
                string v = (string)kv.Value ?? "";
                ImGui.SetNextItemWidth(200f);
                if (ImGui.InputText(kv.Key, ref v, 128) && ImGui.IsItemDeactivatedAfterEdit()) { defs[kv.Key] = v; Changed(editor); }
                ImGui.SameLine(); if (ImGui.SmallButton("x##d" + kv.Key)) { defs.Remove(kv.Key); Changed(editor); }
            }
            ImGui.SetNextItemWidth(120f); ImGui.InputTextWithHint("##dk", "key", ref _newDefaultKey, 32);
            ImGui.SameLine(); ImGui.SetNextItemWidth(160f); ImGui.InputTextWithHint("##dv", "value", ref _newDefaultValue, 128);
            ImGui.SameLine(); if (ImGui.SmallButton("add##dd") && _newDefaultKey.Trim().Length > 0) { defs[_newDefaultKey.Trim()] = _newDefaultValue; _newDefaultKey = _newDefaultValue = ""; Changed(editor); }
            ImGui.SeparatorText("the + / - keys step (key, step)");
            if (ed["numeric"] is not JsonArray nums) ed["numeric"] = nums = new JsonArray();
            for (int i = 0; i < nums.Count; i++)
            {
                if (nums[i] is not JsonObject o) continue;
                string k = (string)o["key"] ?? ""; float step = o["step"] != null ? (float)o["step"] : 1f;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.InputText($"##nk{i}", ref k, 32) && ImGui.IsItemDeactivatedAfterEdit()) { o["key"] = k; Changed(editor); }
                ImGui.SameLine(); ImGui.SetNextItemWidth(80f);
                if (ImGui.DragFloat($"##ns{i}", ref step, 0.05f, 0.01f, 1000f)) { o["step"] = step; Changed(editor); }
                ImGui.SameLine(); if (ImGui.SmallButton($"x##n{i}")) { nums.RemoveAt(i); Changed(editor); break; }
            }
            if (nums.Count < 2 && ImGui.SmallButton("add##nn")) { nums.Add(new JsonObject { ["key"] = "", ["step"] = 1f }); Changed(editor); }
        }
        private string _newDefaultKey = "", _newDefaultValue = "";

        private static float[] Floats(JsonNode node, int count, float[] fallback)
        {
            if (node is not JsonArray a || a.Count < count) return fallback;
            var r = new float[count]; for (int i = 0; i < count; i++) r[i] = (float)a[i]; return r;
        }
        private static JsonArray Arr(float[] v) => new JsonArray(v.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());

        // ------------------------------------------------------------------ apply / save / revert

        /// <summary>Every edit lands in the library at once (unsaved), so the level's instances follow as you type.</summary>
        private void Changed(EditorController editor)
        {
            _dirty = true;
            try
            {
                string json = PrefabWriter.ToJson(_doc, _reference, _base);
                GamePrefabs.Library.AddJson(json, _name, null);
                if (_retarget == null) { EntityRegistry.Refresh(_name); editor.RebuildClass(_name); }
            }
            catch (Exception e) { Status(e.Message); }
        }

        public void Save(EditorController editor)
        {
            if (string.IsNullOrWhiteSpace(_name)) { Status("name the prefab first"); return; }
            try
            {
                PrefabWriter.Save(GamePrefabs.Library, _doc, _base, _file);
                EntityRegistry.Refresh(_name);
                editor.Sources.Invalidate();
                if (_retarget != null)
                {
                    // the instance this was made from now simply *is* one of these: its overrides live in the template
                    foreach (var key in _retarget.properties.Keys.Where(k => !IsUniversal(k)).ToList()) _retarget.properties.Remove(key);
                    _retarget.classname = _name;
                    editor.MarkDirty();
                    _retarget = null;
                }
                editor.RebuildClass(_name);
                _dirty = false;
                Status("Saved " + _file);
            }
            catch (Exception e) { Status("Save failed: " + e.Message); }
        }

        public void Revert(EditorController editor)
        {
            var lib = GamePrefabs.Library;
            try
            {
                if (lib.SourceOf(_name) != null) { lib.Reload(_name); Open(_name); EntityRegistry.Refresh(_name); editor.RebuildClass(_name); Status("Reverted to the file"); }
                else { IsOpen = false; Status("discarded"); }
            }
            catch (Exception e) { Status("Revert failed: " + e.Message); }
        }

        // ---- the scripted gate
        public string Name => _name;
        public bool Dirty => _dirty;
        public void SetFieldForScript(string component, string field, object value)
        {
            var cfg = _node.Components.Find(c => c.ComponentType.Name == component) ?? throw new InvalidOperationException("no " + component);
            cfg.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!.SetValue(cfg, value);
            _dirty = true;
        }
    }
}
