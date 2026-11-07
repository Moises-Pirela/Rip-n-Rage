using System;
using System.Collections.Generic;
using System.Reflection;
using ImGuiNET;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Particles;
using RipNRage.ImGuiIntegration;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.VfxEditor
{
    /// <summary>
    /// The editor's Dear ImGui panels: effect and emitter lists on the left, a reflection-driven property grid on the
    /// right, a sprite palette and the help window. The 3D preview is the engine's own render, so the dock space keeps
    /// its central node empty and the preview shows through it.
    /// </summary>
    public sealed partial class VfxEditor
    {
        private static readonly FieldInfo[] EmitterFields = typeof(ParticleEmitter).GetFields(BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo[] EffectFields = typeof(ParticleEffect).GetFields(BindingFlags.Public | BindingFlags.Instance);

        private static readonly Vec4 Accent = new Vec4(1f, 0.78f, 0.25f, 1f);
        private static readonly Vec4 Dim = new Vec4(0.55f, 0.58f, 0.63f, 1f);
        private static readonly Vec4 DangerCol = new Vec4(0.8f, 0.25f, 0.2f, 1f);
        private static readonly Vec4 GoodCol = new Vec4(0.3f, 0.75f, 0.35f, 1f);

        /// <summary>Declares every panel for this frame. Called from Update, inside the ImGui frame.</summary>
        private void BuildUi()
        {
            DrawMenuBar();
            float statusH = ImGui.GetFrameHeight();
            uint dockspace = _imgui.BeginDockHost("##vfx", statusH);
            if (!_layoutBuilt)
            {
                _layoutBuilt = true;
                if (!_imgui.HasSavedLayout)
                    ImGuiDock.BuildDefaultLayout(dockspace, ImGui.GetWindowSize(), "Effects", "Properties", 0.17f, 0.3f, "Emitters", 0.3f);
            }
            _imgui.EndDockHost();

            DrawEffectsPanel();
            DrawEmittersPanel();
            DrawPropertiesPanel();
            DrawStatusBar(statusH);
            if (_showHelp) DrawHelp();
            DrawPalette();
            DrawPrompt();

            // the preview owns the whole window; ImGui tells us when the pointer belongs to a panel instead
            var win = Application.Current.Window;
            _viewport = new Rect(0f, 0f, win.Width, win.Height);
        }

        // ------------------------------------------------------------------ bars

        private void DrawMenuBar()
        {
            if (!ImGui.BeginMainMenuBar()) return;
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New effect", "Ctrl+N")) NewEffect();
                if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, _effect != null)) DuplicateEffect();
                if (ImGui.MenuItem("Rename...", "F2", false, _effect != null)) Prompt("Rename effect", _effect.name, RenameEffect);
                if (ImGui.MenuItem("Delete", "Shift+Del", false, _effect != null)) DeleteEffect();
                ImGui.Separator();
                if (ImGui.MenuItem("Save all", "Ctrl+S", false, Dirty)) SaveAll();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _undo.Count > 0)) Undo();
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _redo.Count > 0)) Redo();
                ImGui.Separator();
                if (ImGui.MenuItem("Add emitter", "Ins", false, _effect != null)) AddEmitter();
                if (ImGui.MenuItem("Remove emitter", "Del", false, SelectedEmitter != null)) RemoveEmitter();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Preview"))
            {
                if (ImGui.MenuItem("Replay", "Space")) Respawn();
                if (ImGui.MenuItem("Loop", "L", _loop)) { _loop = !_loop; if (_instance != null) _instance.loop = _loop; }
                if (ImGui.MenuItem(_paused ? "Play" : "Pause", "P")) _paused = !_paused;
                if (ImGui.MenuItem("Surface: " + SurfaceName(), "T")) SetSurfaceMode(_surfaceMode + 1);
                ImGui.Separator();
                foreach (float scale in TimeScales)
                    if (ImGui.MenuItem($"{scale:0.##}x speed", null, Math.Abs(_timeScale - scale) < 0.01f)) _timeScale = scale;
                ImGui.EndMenu();
            }
            if (ImGui.MenuItem("Help")) _showHelp = !_showHelp;

            if (_effect != null)
            {
                ImGui.Spacing();
                float dur = _effect.Duration;
                ImGui.TextColored(Dirty && _dirty.Contains(_effect.name) ? Accent : Dim,
                    $"{_effect.name}{(Dirty && _dirty.Contains(_effect.name) ? " *" : "")}   {_effect.emitters.Count} emitters   {(dur > 0f ? dur.ToString("0.00") + "s" : "open ended")}   cap {Capacity(_effect)}");
            }
            ImGui.EndMainMenuBar();
        }

        private static readonly float[] TimeScales = { 0.25f, 0.5f, 1f, 2f };

        private static int Capacity(ParticleEffect fx)
        {
            int n = 0;
            foreach (var e in fx.emitters) if (e.enabled) n += e.Capacity;
            return n;
        }

        private void DrawStatusBar(float height)
        {
            if (_imgui.BeginStatusBar("##status", height))
            {
                if (Time.unscaledTime < _statusUntil)
                {
                    ImGui.TextColored(Accent, _status);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("   ");
                    ImGui.SameLine();
                }
                ImGui.TextColored(Dim, $"fx {ParticleWorld.LiveInstances}   particles {ParticleWorld.LiveParticles}   draws {Application.Current.Renderer.Particles.DrawCalls}   fps {Application.Current.Fps:0}"
                                     + "     LMB place   RMB orbit   wheel zoom   Space replay   T surface   L loop   Ctrl+S save");
            }
            ImGui.End();
        }

        // ------------------------------------------------------------------ left: effects + emitters

        private void DrawEffectsPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(250f, 400f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Effects")) { ImGui.End(); return; }

            float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
            if (ImGui.Button("New", new Vec2(w, 0f))) NewEffect();
            ImGui.SameLine();
            ImGui.BeginDisabled(_effect == null);
            if (ImGui.Button("Dup", new Vec2(w, 0f))) DuplicateEffect();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, DangerCol);
            if (ImGui.Button("Del", new Vec2(w, 0f))) DeleteEffect();
            ImGui.PopStyleColor();
            ImGui.EndDisabled();

            if (ImGui.BeginListBox("##effects", new Vec2(-1f, -1f)))
            {
                foreach (var n in _names)
                {
                    bool sel = _effect != null && string.Equals(n, _effect.name, StringComparison.OrdinalIgnoreCase);
                    bool unsaved = _dirty.Contains(n);
                    if (unsaved) ImGui.PushStyleColor(ImGuiCol.Text, Accent);
                    if (ImGui.Selectable((unsaved ? "* " : "") + n, sel) && !sel) Select(n);
                    if (unsaved) ImGui.PopStyleColor();
                }
                ImGui.EndListBox();
            }
            ImGui.End();
        }

        private void DrawEmittersPanel()
        {
            ImGui.SetNextWindowSize(new Vec2(250f, 300f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Emitters")) { ImGui.End(); return; }
            bool hasSel = SelectedEmitter != null;

            float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 3f) / 4f;
            ImGui.BeginDisabled(_effect == null);
            if (ImGui.Button("Add", new Vec2(w, 0f))) AddEmitter();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!hasSel);
            if (ImGui.Button("Del", new Vec2(w, 0f))) RemoveEmitter();
            ImGui.SameLine();
            if (ImGui.Button("Up", new Vec2(w, 0f))) MoveEmitter(-1);
            ImGui.SameLine();
            if (ImGui.Button("Down", new Vec2(w, 0f))) MoveEmitter(1);
            ImGui.EndDisabled();

            if (_effect != null && ImGui.BeginListBox("##emitters", new Vec2(-1f, -1f)))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Accent);
                if (ImGui.Selectable("[effect settings]", _selEmitter < 0)) _selEmitter = -1;
                ImGui.PopStyleColor();
                for (int i = 0; i < _effect.emitters.Count; i++)
                {
                    var e = _effect.emitters[i];
                    int live = _instance != null && i < _instance.emitters.Length ? _instance.emitters[i].count : 0;
                    string label = (e.enabled ? "" : "(off) ") + e.name + (live > 0 ? "   " + live : "");
                    if (!e.enabled) ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    if (ImGui.Selectable(label + "##em" + i, i == _selEmitter)) _selEmitter = i;
                    if (!e.enabled) ImGui.PopStyleColor();
                    if (ImGui.BeginPopupContextItem("##emctx" + i))
                    {
                        if (ImGui.MenuItem(e.enabled ? "Disable" : "Enable")) { e.enabled = !e.enabled; MarkStructural(); }
                        ImGui.EndPopup();
                    }
                }
                ImGui.EndListBox();
            }
            ImGui.End();
        }

        // ------------------------------------------------------------------ right: property grid

        private void DrawPropertiesPanel()
        {
            var em = SelectedEmitter;
            ImGui.SetNextWindowSize(new Vec2(380f, 700f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Properties")) { ImGui.End(); return; }
            if (_effect == null)
            {
                ImGui.TextColored(Dim, "No effect selected.");
                ImGui.End();
                return;
            }
            ImGui.TextColored(Accent, em != null ? "Emitter: " + em.name : "Effect: " + _effect.name);
            ImGui.Separator();
            // the field label is drawn by ImGui to the right of the widget; keep a fixed gutter for it
            ImGui.PushItemWidth(-ImGui.GetContentRegionAvail().X * 0.42f);
            if (em != null) DrawGrid(em, _emitterDefaults, EmitterFields, true);
            else DrawGrid(_effect, _effectDefaults, EffectFields, false);
            ImGui.PopItemWidth();
            ImGui.End();
        }

        private static bool Skip(FieldInfo f) => f.Name == "emitters";

        /// <summary>Reflection-driven widgets: one row per public field, widget by type, guided by the Vfx* attributes.</summary>
        private void DrawGrid(object target, object defaults, FieldInfo[] fields, bool emitter)
        {
            foreach (var f in fields)
            {
                if (Skip(f)) continue;
                var group = f.GetCustomAttribute<VfxGroupAttribute>();
                if (group != null) ImGui.SeparatorText(group.name);
                ImGui.PushID(f.Name);
                DrawField(target, defaults, f, emitter);
                var tip = f.GetCustomAttribute<VfxTipAttribute>();
                if (tip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip.text);
                ImGui.PopID();
            }
        }

        private void DrawField(object target, object defaults, FieldInfo f, bool emitter)
        {
            string label = Pretty(f.Name);
            var range = f.GetCustomAttribute<VfxRangeAttribute>();
            float min = range?.min ?? -10000f, max = range?.max ?? 10000f, step = range?.step ?? 0.01f;
            object value = f.GetValue(target);
            object def = f.GetValue(defaults);

            if (f.FieldType == typeof(float))
            {
                float v = (float)value;
                if (ImGui.DragFloat(label, ref v, step, min, max)) { f.SetValue(target, v); Changed(f); }
                ResetOnRightClick(f, target, def);
            }
            else if (f.FieldType == typeof(int))
            {
                int v = (int)value;
                if (ImGui.DragInt(label, ref v, Math.Max(0.2f, step), (int)min, (int)max)) { f.SetValue(target, v); Changed(f); }
                ResetOnRightClick(f, target, def);
            }
            else if (f.FieldType == typeof(bool))
            {
                bool v = (bool)value;
                if (ImGui.Checkbox(label, ref v)) { f.SetValue(target, v); Changed(f); }
            }
            else if (f.FieldType.IsEnum)
            {
                var values = Enum.GetValues(f.FieldType);
                int idx = Array.IndexOf(values, value);
                string[] names = Enum.GetNames(f.FieldType);
                if (ImGui.Combo(label, ref idx, names, names.Length)) { f.SetValue(target, values.GetValue(idx)); Changed(f); }
            }
            else if (f.FieldType == typeof(string))
            {
                string v = (string)value ?? "";
                if (f.GetCustomAttribute<VfxSpriteAttribute>() != null)
                {
                    DrawSpriteField(label, v, picked => { f.SetValue(target, picked); Changed(f); });
                }
                else if (f.GetCustomAttribute<VfxEffectRefAttribute>() != null)
                {
                    var opts = new List<string> { "(none)" };
                    opts.AddRange(_names);
                    int i = Math.Max(0, opts.FindIndex(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase)));
                    if (ImGui.Combo(label, ref i, opts.ToArray(), opts.Count)) { f.SetValue(target, i == 0 ? "" : opts[i]); Changed(f); }
                }
                else
                {
                    string buffer = v;
                    if (ImGui.InputText(label, ref buffer, 128))
                    {
                        if (f.Name == "name" && !emitter) { /* renaming the effect moves its file: use F2 */ }
                        else { f.SetValue(target, buffer); Changed(f); }
                    }
                    if (f.Name == "name" && !emitter && ImGui.IsItemDeactivatedAfterEdit()) RenameEffect(buffer);
                }
            }
            else if (f.FieldType == typeof(List<string>))
            {
                var list = (List<string>)value ?? new List<string>();
                ImGui.TextUnformatted($"{label} ({list.Count})");
                ImGui.SameLine();
                if (ImGui.SmallButton("+ add")) OpenPalette(picked => { list.Add(picked); f.SetValue(target, list); Changed(f); });
                ImGui.SameLine();
                ImGui.BeginDisabled(list.Count == 0);
                if (ImGui.SmallButton("- last")) { list.RemoveAt(list.Count - 1); Changed(f); }
                ImGui.SameLine();
                if (ImGui.SmallButton("clear")) { list.Clear(); Changed(f); }
                ImGui.EndDisabled();
            }
            else if (f.FieldType == typeof(FloatRange))
            {
                var v = (FloatRange)value; var dv = (FloatRange)def;
                float rstep = range?.step ?? (MathF.Abs(v.Max) >= 20f ? 1f : MathF.Abs(v.Max) >= 2f ? 0.1f : 0.01f);
                float rmin = range?.min ?? -10000f, rmax = range?.max ?? 10000f;
                var pair = new Vec2(v.min, v.max);
                if (ImGui.DragFloat2(label, ref pair, rstep, rmin, rmax))
                { f.SetValue(target, new FloatRange(pair.X, Math.Max(pair.X, pair.Y))); Changed(f); }
                ResetOnRightClick(f, target, def);
            }
            else if (f.FieldType == typeof(Color))
            {
                var c = (Color)value;
                var v4 = new Vec4(c.r, c.g, c.b, c.a);
                // HDR: particle tints go above 1 for additive glow
                if (ImGui.ColorEdit4(label, ref v4, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.AlphaBar))
                { f.SetValue(target, new Color(v4.X, v4.Y, v4.Z, v4.W)); Changed(f); }
                ResetOnRightClick(f, target, def);
            }
            else if (f.FieldType == typeof(Vector3))
            {
                var v = (Vector3)value;
                var v3 = new System.Numerics.Vector3(v.x, v.y, v.z);
                if (ImGui.DragFloat3(label, ref v3, 0.01f, -100f, 100f))
                { f.SetValue(target, new Vector3(v3.X, v3.Y, v3.Z)); Changed(f); }
                ResetOnRightClick(f, target, def);
            }
            else
            {
                ImGui.TextColored(Dim, $"{label}: {value?.ToString() ?? "null"}");
            }
        }

        /// <summary>Right-click a value to put the default back (the old editor's behaviour).</summary>
        private void ResetOnRightClick(FieldInfo f, object target, object def)
        {
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { f.SetValue(target, def); Changed(f); }
        }

        /// <summary>Fields that change capacity / sprites / timing restart the preview; everything else is live.</summary>
        private static readonly HashSet<string> StructuralFields = new HashSet<string>
        {
            "enabled", "sprite", "frames", "burstMin", "burstMax", "rate", "duration", "lifetime", "delay", "lifetimeFromDistance", "hasLight", "scale", "name",
        };

        private void Changed(FieldInfo f)
        {
            if (StructuralFields.Contains(f.Name)) MarkStructural(); else MarkChanged();
            if (f.Name == "name" && f.DeclaringType == typeof(ParticleEffect)) RefreshNames();
        }

        private static string Pretty(string fieldName)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in fieldName)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ sprite field + palette

        private void DrawSpriteField(string label, string reference, Action<string> accept)
        {
            var sprite = SpriteLibrary.Resolve(reference);
            float h = ImGui.GetFrameHeight();
            if (sprite != null)
            {
                ImGuiController.Image(sprite, new Vec2(h, h));
                ImGui.SameLine();
            }
            if (ImGui.Button((string.IsNullOrEmpty(reference) ? "(pick sprite)" : reference) + "##" + label, new Vec2(ImGui.CalcItemWidth() - (sprite != null ? h + ImGui.GetStyle().ItemSpacing.X : 0f), 0f)))
                OpenPalette(accept);
            ImGui.SameLine();
            ImGui.TextUnformatted(label);
        }

        private void OpenPalette(Action<string> accept)
        {
            _paletteOpen = true;
            _paletteAccept = accept;
        }

        private void DrawPalette()
        {
            if (_paletteOpen && !ImGui.IsPopupOpen("Pick a sprite")) ImGui.OpenPopup("Pick a sprite");
            if (!_paletteOpen) return;
            var centre = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vec2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vec2(680f, 520f), ImGuiCond.Appearing);
            if (!ImGui.BeginPopupModal("Pick a sprite", ref _paletteOpen)) return;

            var atlases = new List<(string key, SpriteAtlas atlas)>(SpriteLibrary.Atlases);
            int pi = atlases.FindIndex(a => a.key == "Particles");   // the one you nearly always want, first
            if (pi > 0) { var p = atlases[pi]; atlases.RemoveAt(pi); atlases.Insert(0, p); }
            if (atlases.Count > 0 && ImGui.BeginTabBar("##atlases"))
            {
                for (int i = 0; i < atlases.Count; i++)
                {
                    if (!ImGui.BeginTabItem(atlases[i].key)) continue;
                    DrawPaletteGrid(atlases[i].key, atlases[i].atlas);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.EndPopup();
        }

        private void DrawPaletteGrid(string key, SpriteAtlas atlas)
        {
            const float cell = 96f;
            if (!ImGui.BeginChild("##grid", new Vec2(-1f, -1f))) { ImGui.EndChild(); return; }
            var names = new List<string>(atlas.Sprites.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            int cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));
            for (int i = 0; i < names.Count; i++)
            {
                if (i % cols != 0) ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.PushID(i);
                var sprite = atlas.Sprites[names[i]];
                if (SpriteButton(sprite, new Vec2(cell - 16f, cell - 32f)))
                {
                    _paletteOpen = false;
                    ImGui.CloseCurrentPopup();
                    var accept = _paletteAccept; _paletteAccept = null;
                    accept?.Invoke(key + "/" + names[i]);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(names[i]);
                ImGui.TextUnformatted(Truncate(names[i], 11));
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
            ImGui.SetNextWindowSize(new Vec2(760f, 500f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Help", ref _showHelp)) { ImGui.End(); return; }
            ImGui.SeparatorText("Effects");
            Help("Ctrl+N / Ctrl+D", "New / duplicate");
            Help("F2 / Shift+Del", "Rename / delete");
            Help("PgUp / PgDn", "Previous / next effect");
            Help("Ctrl+S", "Save all");
            Help("Ctrl+Z / Ctrl+Y", "Undo / redo (per effect)");
            ImGui.SeparatorText("Emitters");
            Help("Ins / Del", "Add / remove");
            Help("Ctrl+Up / Ctrl+Down", "Reorder");
            Help("Right-click a row", "Enable or disable it");
            ImGui.SeparatorText("Property grid");
            Help("Drag a number", "Shift x10, Ctrl /10");
            Help("Right-click a value", "Back to the default");
            Help("Sprite field", "Click to open the palette");
            ImGui.SeparatorText("Preview");
            Help("Space / L / P", "Replay / loop / pause");
            Help("[ ]", "Speed");
            Help("T", "Wall / floor / air");
            Help("LMB", "Place the effect on a surface");
            Help("RMB drag / wheel", "Orbit / zoom");
            ImGui.Spacing();
            ImGui.TextWrapped("Effect space: Z = forward = the surface normal (impacts) or the fire direction (tracers); offsets are in metres. "
                            + "Render modes: billboard faces the camera, stretched follows velocity (sparks, tracers), horizontal lies on the ground, "
                            + "aligned lies on the hit surface (decals, flashes, shockwaves).");
            ImGui.TextWrapped("Collision: particles raycast against the world; a collision effect is another effect spawned where they land.");
            ImGui.TextWrapped("vfx/bindings.json maps surfaces (concrete, wood, metal, glass, sand, water, flesh), ammo types, explosions, trails "
                            + "and deaths to effect names. The game hot-reloads what you save.");
            ImGui.End();
        }

        private static void Help(string keys, string what)
        {
            ImGui.TextColored(Accent, keys);
            ImGui.SameLine(200f);
            ImGui.TextWrapped(what);
        }

        // ------------------------------------------------------------------ viewport overlay

        /// <summary>A cross at the anchor the effect plays from, drawn over the 3D preview (under the panels).</summary>
        public void DrawUi(UiBatch ui)
        {
            if (rig?.camera == null) return;
            var win = Application.Current.Window;
            Vector3 sp = rig.camera.WorldToScreenPoint(_anchorPos, win.Width, win.Height);
            if (sp.z > 0f)
            {
                var c = new Color(1f, 0.8f, 0.3f, 0.6f);
                float s = MathF.Max(1f, _imgui?.PixelRatio ?? 1f);
                ui.FillRect(new Rect(sp.x - 7f * s, sp.y - 0.5f * s, 14f * s, s), c);
                ui.FillRect(new Rect(sp.x - 0.5f * s, sp.y - 7f * s, s, 14f * s), c);
            }
        }
    }
}
