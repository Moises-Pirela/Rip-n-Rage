using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content.Level;
using RipNRage.ImGuiIntegration;
using RipNRage.Level;
using Vec2 = System.Numerics.Vector2;

namespace RipNRage.Editor
{
    /// <summary>
    /// The Entity window (10.6): the selected map entities' class and every field of every component their prefab
    /// gives them, editable. The window never touches an entity — it writes <b>map keys</b> and asks the level to
    /// re-preview that one thing, the same path a load takes, so what it shows is what the game will build. A value
    /// equal to the prefab's is a key removed, not written; a value that fails to bake is a message on its row and the
    /// old value stays. The Level tab is the same grid over the worldspawn, applied live through its singleton.
    /// </summary>
    public partial class EditorController
    {
        public bool showInspector = true;
        private string _inspectorFilter = "";
        private int _inspectorTab; // 0 entity, 1 level
        private readonly List<PropertyRow> _inspectorRows = new List<PropertyRow>();
        private string _inspectorSignature;
        private string _inspectorError;
        private string _inspectorClipboard = "";
        private int _addComponentIndex;
        private readonly HashSet<string> _presentComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Action> _groupActions = new Dictionary<string, Action>();
        private readonly PrefabWindow _prefabWindow = new PrefabWindow();
        private EditorSources _sources;

        private static readonly string[] UniversalKeys = { "classname", "origin", "angle", "targetname" };
        private static bool IsUniversal(string key) => Array.Exists(UniversalKeys, u => u.Equals(key, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ the window

        private void DrawInspector()
        {
            _sources ??= new EditorSources(this);
            ImGui.SetNextWindowSize(new Vec2(360f, 700f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Entity", ref showInspector)) { ImGui.End(); return; }
            if (ImGui.BeginTabBar("##tabs"))
            {
                if (ImGui.BeginTabItem("Entity")) { _inspectorTab = 0; DrawEntityTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Level")) { _inspectorTab = 1; DrawLevelTab(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            ImGui.End();
            _prefabWindow.Draw(this);
        }

        private void DrawEntityTab()
        {
            if (_selEntities.Count == 0)
            {
                ImGui.TextColored(PropertyGrid.Dim, "Nothing selected. Click an entity in Entity mode, or place one.");
                return;
            }
            var first = _selEntities[0];
            var def = EntityRegistry.Get(first.classname);
            bool sameClass = _selEntities.All(e => e.classname.Equals(first.classname, StringComparison.OrdinalIgnoreCase));

            // ---- header: the class (a combo swaps it), its description, the instance count, the actions
            var classes = EntityRegistry.All;
            int ci = -1; for (int i = 0; i < classes.Count; i++) if (classes[i].classname.Equals(first.classname, StringComparison.OrdinalIgnoreCase)) ci = i;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##class", sameClass ? first.classname : "(several classes)"))
            {
                for (int i = 0; i < classes.Count; i++)
                    if (ImGui.Selectable(classes[i].classname, i == ci) && i != ci) SwapClass(_selEntities, classes[i].classname);
                ImGui.EndCombo();
            }
            if (def != null) ImGui.TextWrapped(def.description ?? "");
            if (_selEntities.Count > 1) ImGui.TextColored(PropertyGrid.Dim, $"{_selEntities.Count} selected - an edit writes all of them; '*' = overridden on any");
            if (ImGui.Button("Copy")) CopyProperties();
            ImGui.SameLine();
            if (ImGui.Button("Paste")) PasteProperties();
            if (def?.prefab != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Open prefab")) _prefabWindow.Open(def.prefab);
                ImGui.SameLine();
                if (ImGui.Button("Save as prefab...")) _prefabWindow.NewFrom(first, this);
            }
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##filter", "filter fields", ref _inspectorFilter, 64);
            if (_inspectorError != null) ImGui.TextColored(PropertyGrid.Danger, _inspectorError);
            ImGui.Separator();

            // ---- rows: rebuilt when the selection or the document changed
            string signature = Signature(_selEntities);
            if (signature != _inspectorSignature) { BuildRows(_selEntities, isLevel: false); _inspectorSignature = signature; }
            if (ImGui.BeginChild("##rows"))
            {
                PropertyGrid.Draw(_inspectorRows, _inspectorFilter, _sources, 170f, DrawGroupExtras);
                if (sameClass && def?.prefab != null) DrawAddComponent(first, def);
            }
            ImGui.EndChild();
        }

        private void DrawLevelTab()
        {
            ImGui.TextWrapped("The level's environment (worldspawn): sun, sky and fog override the tunables where set; the kill height; the sky's spin. Applied live.");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##filter", "filter fields", ref _inspectorFilter, 64);
            ImGui.Separator();
            var targets = new List<MapEntity> { doc.worldspawn };
            string signature = "worldspawn|" + Signature(targets);
            if (signature != _inspectorSignature) { BuildRows(targets, isLevel: true); _inspectorSignature = signature; }
            if (ImGui.BeginChild("##rows")) PropertyGrid.Draw(_inspectorRows, _inspectorFilter, _sources, 170f, null);
            ImGui.EndChild();
        }

        /// <summary>A small "x" beside a component's header removes it from the instance (10.6).</summary>
        private void DrawGroupExtras(string group)
        {
            if (!_groupActions.TryGetValue(group, out var action)) return;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 4f);
            if (ImGui.SmallButton("x##" + group)) action();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("remove this component from the selected instance(s)");
        }

        private void DrawAddComponent(MapEntity first, EntityDefinition def)
        {
            var names = PrefabLibrary.ConfigNames().Where(n => !_presentComponents.Contains(n)).OrderBy(n => n).ToArray();
            if (names.Length == 0) return;
            ImGui.Separator();
            _addComponentIndex = Math.Clamp(_addComponentIndex, 0, names.Length - 1);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
            ImGui.Combo("##add", ref _addComponentIndex, names, names.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add")) AddComponent(_selEntities, names[_addComponentIndex]);
        }

        // ------------------------------------------------------------------ rows

        private static string Signature(List<MapEntity> targets)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in targets)
            {
                sb.Append(e.GetHashCode()).Append(':');
                foreach (var kv in e.properties) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>Every key but the loader's own, as the overrides a level applies.</summary>
        private static List<KeyValuePair<string, string>> Overrides(MapEntity e)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var kv in e.properties) if (!IsUniversal(kv.Key)) list.Add(kv);
            return list;
        }

        private void BuildRows(List<MapEntity> targets, bool isLevel)
        {
            _inspectorRows.Clear(); _groupActions.Clear(); _presentComponents.Clear(); _inspectorError = null;
            var first = targets[0];
            string className = isLevel ? "worldspawn" : first.classname;
            if (!isLevel)
            {
                // the instance's own keys
                _inspectorRows.Add(new PropertyRow
                {
                    Id = "origin", Label = "origin", Group = "Instance", Type = typeof(float[]), Value = Arr(first.origin),
                    Mixed = targets.Any(e => e.origin != first.origin), Range = new InspectRangeAttribute(-4096f, 4096f, 0.05f),
                    Began = PushUndo, Set = v => Apply(targets, e => e.origin = Vec((float[])v)),
                });
                _inspectorRows.Add(new PropertyRow
                {
                    Id = "angle", Label = "angle", Group = "Instance", Type = typeof(float), Value = first.angle,
                    Mixed = targets.Any(e => e.angle != first.angle), Range = new InspectRangeAttribute(-360f, 360f, 1f), Tip = "yaw in degrees",
                    Began = PushUndo, Set = v => Apply(targets, e => e.angle = (float)v),
                });
                _inspectorRows.Add(new PropertyRow
                {
                    Id = "targetname", Label = "targetname", Group = "Instance", Type = typeof(string), Value = first.Get("targetname", ""),
                    Mixed = targets.Count > 1, ReadOnly = targets.Count > 1, Tip = "the name connectors and references use",
                    Error = TargetnameClash(first), Began = PushUndo,
                    Set = v => { var s = (string)v; if (string.IsNullOrEmpty(s)) first.properties.Remove("targetname"); else first.Set("targetname", s); Rebuilt(targets); },
                });
            }
            var def = isLevel ? null : EntityRegistry.Get(className);
            string prefabName = isLevel ? "worldspawn" : def?.prefab;
            if (prefabName == null)
            {
                if (def == null) _inspectorError = $"unknown class '{className}'";
                return; // a code class (the player start): only its instance keys
            }
            var lib = GamePrefabs.Library;
            Prefab template, inst;
            var others = new List<Prefab>();
            try
            {
                template = lib.Get(prefabName);
                inst = lib.Get(prefabName, Overrides(first));
                for (int i = 1; i < targets.Count; i++) others.Add(lib.Get(prefabName, Overrides(targets[i])));
            }
            catch (Exception ex) { _inspectorError = Short(ex.Message); return; }

            // aliases: one path → its row carries the alias; several paths → one row under "Keys", and the paths it
            // drives are not offered on their own (editing them apart is how a map goes inconsistent)
            var pathToAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in template.Keys)
            {
                if (kv.Value.Length == 1) { pathToAlias[kv.Value[0]] = kv.Key; continue; }
                foreach (var p in kv.Value) covered.Add(p);
                if (!TryFind(inst, kv.Value[0], out var cfg, out var field)) continue;
                TryFind(template, kv.Value[0], out var tcfg, out _);
                var row = PropertyRow.ForField(field, cfg, tcfg, "Keys");
                row.Id = "key:" + kv.Key; row.Label = kv.Key;
                row.Tip = "drives " + string.Join(", ", kv.Value);
                row.Overridden = targets.Any(e => e.properties.ContainsKey(kv.Key));
                row.Mixed = others.Any(o => TryFind(o, kv.Value[0], out var oc, out var of) && !PropertyGrid.ValuesEqual(of.GetValue(oc), row.Value));
                string key = kv.Key;
                row.Began = PushUndo;
                row.Set = v => WriteKey(targets, key, PropertyGrid.ToText(v), row, isLevel);
                row.Revert = () => RemoveKey(targets, key, isLevel);
                _inspectorRows.Add(row);
            }
            WalkNode(inst, template, others, "", targets, pathToAlias, covered, isLevel);
            foreach (var c in inst.Components) _presentComponents.Add(c.ComponentType.Name);
        }

        private void WalkNode(Prefab instNode, Prefab templNode, List<Prefab> otherNodes, string prefix, List<MapEntity> targets,
                              Dictionary<string, string> pathToAlias, HashSet<string> covered, bool isLevel)
        {
            foreach (var cfg in instNode.Components)
            {
                string compName = cfg.ComponentType.Name;
                string group = (prefix.Length == 0 ? "" : prefix.TrimEnd('/') + " > ") + compName;
                var tcfg = templNode?.Components.Find(c => c.GetType() == cfg.GetType());
                bool instanceAdded = tcfg == null; // "+Component" on the instance: compared against a fresh config
                var reference = tcfg ?? (IComponentConfig)Activator.CreateInstance(cfg.GetType());
                if (!isLevel && prefix.Length == 0)
                {
                    string cn = compName; bool added = instanceAdded;
                    _groupActions[group] = () => RemoveComponent(targets, cn, added);
                }
                foreach (var field in cfg.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.GetCustomAttribute<InspectHiddenAttribute>() != null) continue;
                    string path = prefix + compName + "." + field.Name;
                    if (covered.Contains(path)) continue;
                    string key = pathToAlias.TryGetValue(path, out var alias) ? alias : path;
                    var row = PropertyRow.ForField(field, cfg, reference, group);
                    row.Id = path;
                    if (alias != null) { row.Label = field.Name + "  (" + alias + ")"; }
                    row.Overridden = targets.Any(e => e.properties.ContainsKey(key)) || (instanceAdded && row.Overridden);
                    foreach (var o in otherNodes)
                    {
                        var ocfg = o?.Components.Find(c => c.GetType() == cfg.GetType());
                        if (ocfg == null || !PropertyGrid.ValuesEqual(field.GetValue(ocfg), row.Value)) { row.Mixed = true; break; }
                    }
                    if (field.FieldType == typeof(float[][])) row.ReadOnly = true;
                    row.Began = PushUndo;
                    row.Set = v => WriteKey(targets, key, PropertyGrid.ToText(v), row, isLevel);
                    row.Revert = () => RemoveKey(targets, key, isLevel);
                    _inspectorRows.Add(row);
                }
            }
            foreach (var child in instNode.Children)
            {
                var tchild = templNode?.Child(child.Name);
                var ochildren = otherNodes.Select(o => o?.Child(child.Name)).ToList();
                WalkNode(child, tchild, ochildren, prefix + child.Name + "/", targets, pathToAlias, covered, isLevel);
            }
        }

        /// <summary>The config and field a path (<c>child/Config.field</c>) names on a prefab tree.</summary>
        private static bool TryFind(Prefab root, string path, out IComponentConfig cfg, out FieldInfo field)
        {
            cfg = null; field = null;
            var segs = path.Split('/');
            var node = root;
            for (int i = 0; i < segs.Length - 1 && node != null; i++) node = node.Child(segs[i]);
            if (node == null) return false;
            string last = segs[segs.Length - 1];
            int dot = last.LastIndexOf('.');
            if (dot <= 0) return false;
            var type = ComponentRegistry.FindConfig(last.Substring(0, dot));
            cfg = node.Components.Find(c => c.GetType() == type);
            field = cfg?.GetType().GetField(last.Substring(dot + 1), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return field != null;
        }

        // ------------------------------------------------------------------ writes

        private void WriteKey(List<MapEntity> targets, string key, string text, PropertyRow row, bool isLevel)
        {
            var previous = targets.Select(e => e.properties.TryGetValue(key, out var v) ? v : null).ToList();
            foreach (var e in targets) e.Set(key, text);
            try { Rebuilt(targets, isLevel); row.Error = null; }
            catch (Exception ex)
            {
                row.Error = Short(ex.Message);
                for (int i = 0; i < targets.Count; i++) { if (previous[i] == null) targets[i].properties.Remove(key); else targets[i].Set(key, previous[i]); }
                try { Rebuilt(targets, isLevel); } catch { }
                return;
            }
            // a value equal to the prefab's own is not an override: drop the key again (the row's Overridden state follows)
            if (!isLevel) PruneDefaultKeys(targets, key);
            _inspectorSignature = null;
        }

        private void RemoveKey(List<MapEntity> targets, string key, bool isLevel)
        {
            PushUndo();
            foreach (var e in targets) e.properties.Remove(key);
            try { Rebuilt(targets, isLevel); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
        }

        /// <summary>A key whose value bakes to exactly the prefab's own leaves the map.</summary>
        private void PruneDefaultKeys(List<MapEntity> targets, string key)
        {
            var lib = GamePrefabs.Library;
            foreach (var e in targets)
            {
                var def = EntityRegistry.Get(e.classname);
                if (def?.prefab == null || !e.properties.ContainsKey(key)) continue;
                var paths = lib.ResolveKey(def.prefab, key);
                if (paths == null || paths.Any(p => p.Contains('+') || p.Contains('-'))) continue;
                try
                {
                    var template = lib.Get(def.prefab);
                    var with = lib.Get(def.prefab, Overrides(e));
                    bool allDefault = paths.All(p => TryFind(with, p, out var c1, out var f1) && TryFind(template, p, out var c2, out var f2) && PropertyGrid.ValuesEqual(f1.GetValue(c1), f2.GetValue(c2)));
                    if (allDefault) e.properties.Remove(key);
                }
                catch { }
            }
        }

        private void Apply(List<MapEntity> targets, Action<MapEntity> change)
        {
            foreach (var e in targets) change(e);
            Rebuilt(targets);
            _inspectorSignature = null;
        }

        /// <summary>Re-previews just these entities (or re-bakes the worldspawn), and marks the document dirty.</summary>
        private void Rebuilt(List<MapEntity> targets, bool isLevel = false)
        {
            _dirty = true;
            if (isLevel) { LevelLoader.BakeWorldspawn(doc); return; }
            if (_preview == null) return;
            foreach (var e in targets) LevelLoader.RebuildPreview(_preview, e);
        }

        private string TargetnameClash(MapEntity e)
        {
            string name = e.Get("targetname", "");
            if (string.IsNullOrEmpty(name)) return null;
            int n = doc.entities.Count(x => x.Get("targetname", "").Equals(name, StringComparison.OrdinalIgnoreCase));
            return n > 1 ? $"'{name}' is used by {n} entities" : null;
        }

        private static string Short(string message)
        {
            int nl = message.IndexOf('\n');
            return nl > 0 ? message.Substring(0, nl) : message;
        }

        private static float[] Arr(Vector3 v) => new[] { v.x, v.y, v.z };
        private static Vector3 Vec(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        // ------------------------------------------------------------------ actions (also the scripted gate's API)

        /// <summary>Changes the class of the selection, keeping the keys the new class knows and dropping the rest (with a count).</summary>
        public void SwapClass(List<MapEntity> targets, string newClass)
        {
            var def = EntityRegistry.Get(newClass);
            if (def == null) { Status("no class " + newClass); return; }
            PushUndo();
            int dropped = 0;
            foreach (var e in targets)
            {
                e.classname = newClass;
                foreach (var key in e.properties.Keys.ToList())
                {
                    if (IsUniversal(key)) continue;
                    if (def.prefab == null || GamePrefabs.Library.ResolveKey(def.prefab, key) == null) { e.properties.Remove(key); dropped++; }
                }
            }
            try { Rebuilt(targets); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
            Status(dropped == 0 ? $"{targets.Count} -> {newClass}" : $"{targets.Count} -> {newClass}; {dropped} key(s) the class does not know were dropped");
        }

        /// <summary>The first selected entity's keys (but the loader's own) as "key value" lines, to the clipboard.</summary>
        public void CopyProperties()
        {
            if (_selEntities.Count == 0) return;
            var lines = Overrides(_selEntities[0]).Select(kv => kv.Key + " " + kv.Value);
            _inspectorClipboard = string.Join("\n", lines);
            try { ImGui.SetClipboardText(_inspectorClipboard); } catch { }
            Status($"Copied {lines.Count()} key(s)");
        }

        /// <summary>Applies copied "key value" lines to every selected entity; keys its class does not know are skipped.</summary>
        public void PasteProperties()
        {
            if (_selEntities.Count == 0) return;
            string text = _inspectorClipboard;
            try { var os = ImGui.GetClipboardText(); if (!string.IsNullOrWhiteSpace(os) && os.Contains(' ')) text = os; } catch { }
            if (string.IsNullOrWhiteSpace(text)) { Status("Nothing to paste"); return; }
            PushUndo();
            int applied = 0, skipped = 0;
            foreach (var e in _selEntities)
            {
                var def = EntityRegistry.Get(e.classname);
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim(); if (t.Length == 0) continue;
                    int sp = t.IndexOf(' ');
                    string key = sp < 0 ? t : t.Substring(0, sp), value = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                    if (IsUniversal(key) || def?.prefab == null || GamePrefabs.Library.ResolveKey(def.prefab, key) == null) { skipped++; continue; }
                    e.Set(key, value); applied++;
                }
            }
            try { Rebuilt(_selEntities); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
            Status($"Pasted {applied} key(s)" + (skipped > 0 ? $", skipped {skipped} the class does not know" : ""));
        }

        /// <summary>Adds a component to the selected instances ("+Component" key).</summary>
        public void AddComponent(List<MapEntity> targets, string configOrComponent)
        {
            PushUndo();
            string name = ComponentRegistry.FindConfig(configOrComponent) is Type t ? ComponentRegistry.Get(((IComponentConfig)Activator.CreateInstance(t)).ComponentType).Type.Name : configOrComponent;
            foreach (var e in targets) { e.properties.Remove("-" + name); e.Set("+" + name, ""); }
            try { Rebuilt(targets); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
        }

        /// <summary>Removes a component from the selected instances: one they added goes with its keys, one the prefab gives them is "-Component".</summary>
        public void RemoveComponent(List<MapEntity> targets, string component, bool instanceAdded)
        {
            PushUndo();
            foreach (var e in targets)
            {
                foreach (var key in e.properties.Keys.ToList())
                    if (key.Equals("+" + component, StringComparison.OrdinalIgnoreCase) || key.StartsWith(component + ".", StringComparison.OrdinalIgnoreCase)) e.properties.Remove(key);
                if (!instanceAdded) e.Set("-" + component, "");
            }
            try { Rebuilt(targets); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
        }

        // ---- the scripted gate drives the model, not the widgets
        public MapEntity SelectFirst(string classname)
        {
            var e = doc.entities.Find(x => x.classname.Equals(classname, StringComparison.OrdinalIgnoreCase));
            ClearSelection();
            if (e != null) { _selEntities.Add(e); mode = EditMode.Entity; }
            return e;
        }
        public void SelectAlso(MapEntity e) { if (e != null && !_selEntities.Contains(e)) _selEntities.Add(e); }
        public IReadOnlyList<MapEntity> Selection => _selEntities;
        public void InspectorSet(string key, string text)
        {
            if (_selEntities.Count == 0) return;
            PushUndo();
            var row = new PropertyRow { Label = key };
            WriteKey(_selEntities, key, text, row, isLevel: false);
            if (row.Error != null) Status(row.Error);
        }
        public void InspectorRevert(string key) { if (_selEntities.Count > 0) RemoveKey(_selEntities, key, isLevel: false); }
        public void LevelSet(string key, string text) { PushUndo(); WriteKey(new List<MapEntity> { doc.worldspawn }, key, text, new PropertyRow { Label = key }, isLevel: true); }
        public PrefabWindow Prefabs => _prefabWindow;
        /// <summary>The preview the inspector rebuilds into (the prefab window re-previews a class through it).</summary>
        internal LoadedLevel Preview => _preview;
        internal void RebuildClass(string classname)
        {
            if (_preview == null) return;
            foreach (var e in doc.entities)
                if (e.classname.Equals(classname, StringComparison.OrdinalIgnoreCase))
                    try { LevelLoader.RebuildPreview(_preview, e); } catch (Exception ex) { Status(Short(ex.Message)); }
            _inspectorSignature = null;
        }
        internal void MarkDirty() { _dirty = true; _inspectorSignature = null; }
        internal EditorSources Sources => _sources ??= new EditorSources(this);
    }

    /// <summary>Where the grid's combos get their names: the asset folders and registries, the map's targetnames.</summary>
    internal sealed class EditorSources : IPropertySources
    {
        private readonly EditorController _editor;
        private readonly Dictionary<AssetKind, List<string>> _cache = new Dictionary<AssetKind, List<string>>();
        public EditorSources(EditorController editor) { _editor = editor; }

        public IReadOnlyList<string> Names(AssetKind kind)
        {
            // the map's names change as it is edited; the rest are what the process loaded
            if (kind == AssetKind.Entity)
            {
                var names = new List<string> { "root" };
                foreach (var e in _editor.doc.entities) { var t = e.Get("targetname", ""); if (!string.IsNullOrEmpty(t) && !names.Contains(t)) names.Add(t); }
                if (_editor.Selection.Count > 0 && EntityRegistry.Get(_editor.Selection[0].classname)?.prefab is string p)
                    try { foreach (var c in GamePrefabs.Library.Get(p).Children) names.Add(c.Name); } catch { }
                return names;
            }
            if (_cache.TryGetValue(kind, out var cached)) return cached;
            var list = new List<string>();
            try
            {
                switch (kind)
                {
                    case AssetKind.Texture:
                        list.Add("white");
                        string texDir = Application.AssetPath("Textures");
                        if (Directory.Exists(texDir))
                            foreach (var f in Directory.GetFiles(texDir, "*.png", SearchOption.AllDirectories).OrderBy(f => f))
                                list.Add("Textures/" + Path.GetRelativePath(texDir, f).Replace('\\', '/'));
                        break;
                    case AssetKind.Sprite:
                        foreach (var (key, atlas) in SpriteLibrary.Atlases) foreach (var n in atlas.Names.OrderBy(n => n)) list.Add(key + "/" + n);
                        foreach (var kv in SpriteLibrary.LooseSprites) list.Add(kv.Key);
                        break;
                    case AssetKind.Material: foreach (var m in MaterialLibrary.All) list.Add(m.name); break;
                    case AssetKind.Mesh: list.Add("box"); list.Add("plane"); foreach (var n in RipNRage.Engine.Ecs.Rendering.MeshLibrary.Names) if (!list.Contains(n)) list.Add(n); break;
                    case AssetKind.LevelTexture: list.AddRange(LevelTextures.Names); break;
                    case AssetKind.Surface: list.AddRange(Surfaces.All); break;
                    case AssetKind.Classname: foreach (var d in EntityRegistry.All) list.Add(d.classname); break;
                    case AssetKind.Sound: string au = Application.AssetPath("Audio"); if (Directory.Exists(au)) foreach (var f in Directory.GetFiles(au, "*.wav").OrderBy(f => f)) list.Add("Audio/" + Path.GetFileName(f)); break;
                    case AssetKind.Effect: string vfx = Path.Combine(Application.dataPath, "..", "vfx"); if (Directory.Exists(vfx)) foreach (var f in Directory.GetFiles(vfx, "*.json").OrderBy(f => f)) list.Add(Path.GetFileNameWithoutExtension(f)); break;
                    case AssetKind.Animator: string anim = Path.Combine(Application.dataPath, "..", "animations"); if (Directory.Exists(anim)) foreach (var f in Directory.GetFiles(anim, "*.controller*", SearchOption.AllDirectories).OrderBy(f => f)) list.Add(Path.GetFileNameWithoutExtension(f)); break;
                }
            }
            catch (Exception e) { Debug.LogWarning($"inspector: could not list {kind} names: {e.Message}"); }
            return _cache[kind] = list;
        }

        /// <summary>Forget the cached lists (a prefab was saved, an asset folder changed).</summary>
        public void Invalidate() => _cache.Clear();
    }
}
