using System;
using System.Collections.Generic;
using System.Reflection;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Particles;

namespace RipNRage.VfxEditor
{
    /// <summary>Drawing + mouse interaction of the editor panels (immediate mode, runs inside the UI pass).</summary>
    public sealed partial class VfxEditor
    {
        private const float LeftW = 250f, RightW = 372f, TopH = 30f, BottomH = 24f;
        private static readonly FieldInfo[] EmitterFields = typeof(ParticleEmitter).GetFields(BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo[] EffectFields = typeof(ParticleEffect).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public void DrawUi(UiBatch ui)
        {
            _ui.BeginFrame(ui);
            _ui.BeginWidgets();
            _ui.Modal = _paletteOpen || _prompt.Active || _showHelp;
            float W = ui.Width, H = ui.Height;
            _viewport = new Rect(LeftW, BottomH, W - LeftW - RightW, H - TopH - BottomH);

            DrawTopBar(new Rect(0, H - TopH, W, TopH));
            DrawLeftPanel(new Rect(0, BottomH, LeftW, H - TopH - BottomH));
            DrawRightPanel(new Rect(W - RightW, BottomH, RightW, H - TopH - BottomH));
            DrawBottomBar(new Rect(0, 0, W, BottomH));
            DrawViewportOverlay();

            if (_paletteOpen) DrawPalette(new Rect(W * 0.5f - 340f, H * 0.5f - 240f, 680f, 480f));
            if (_showHelp) DrawHelp(new Rect(W * 0.5f - 330f, H * 0.5f - 250f, 660f, 500f));
            _prompt.Draw(_ui, new Rect(W * 0.5f - 220f, H * 0.5f - 45f, 440f, 90f));

            HandleViewportInput();
        }

        // ------------------------------------------------------------------ bars

        private void DrawTopBar(Rect r)
        {
            _ui.Panel(r);
            float x = r.x + 8f, y = r.yMax - 7f;
            string title = _effect != null ? _effect.name.ToUpperInvariant() + (Dirty && _dirty.Contains(_effect.name) ? " *" : "") : "(NO EFFECT)";
            _ui.Label("VFX EDITOR", new Vector2(x, y), ImUi.Accent); x += _ui.TextWidth("VFX EDITOR") + 18f;
            _ui.Label(title, new Vector2(x, y), ImUi.Text); x += _ui.TextWidth(title) + 18f;
            if (_effect != null)
            {
                float dur = _effect.Duration;
                string info = $"{_effect.emitters.Count} EMITTERS   {(dur > 0f ? dur.ToString("0.00") + "S" : "OPEN ENDED")}   CAP {Capacity(_effect)}";
                _ui.Label(info, new Vector2(x, y), ImUi.TextDim);
            }
            float bw = 64f, bh = r.height - 8f, bx = r.xMax - 8f - bw, by = r.y + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), "HELP F1")) _showHelp = !_showHelp; bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), "SAVE", Dirty, Dirty ? ImUi.Good : (Color?)null)) SaveAll(); bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), "REPLAY")) Respawn(); bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), "LOOP", _loop)) { _loop = !_loop; if (_instance != null) _instance.loop = _loop; } bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), _paused ? "PLAY" : "PAUSE", _paused)) _paused = !_paused; bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), SurfaceName())) SetSurfaceMode(_surfaceMode + 1); bx -= bw + 4f;
            if (_ui.Button(new Rect(bx, by, bw, bh), $"{_timeScale:0.##}X")) _timeScale = _timeScale >= 1f ? 0.25f : _timeScale >= 0.5f ? 1f : 0.5f;
        }

        private static int Capacity(ParticleEffect fx)
        {
            int n = 0;
            foreach (var e in fx.emitters) if (e.enabled) n += e.Capacity;
            return n;
        }

        private void DrawBottomBar(Rect r)
        {
            _ui.Panel(r);
            float y = r.yMax - 5f;
            string stats = $"FX {ParticleWorld.LiveInstances}  PARTICLES {ParticleWorld.LiveParticles}  DRAWS {Application.Current.Renderer.Particles.DrawCalls}  FPS {Application.Current.Fps:0}   LMB PLACE  RMB ORBIT  WHEEL ZOOM  SPACE REPLAY  T SURFACE  L LOOP  CTRL+S SAVE";
            _ui.Label(stats, new Vector2(r.x + 8f, y), ImUi.TextDim);
            if (Time.unscaledTime < _statusUntil)
            {
                float w = _ui.TextWidth(_status) + 16f;
                _ui.Batch.FillRect(new Rect(r.xMax - w - 6f, r.y + 2f, w, r.height - 4f), new Color(0.95f, 0.65f, 0.2f, 0.9f));
                _ui.Label(_status, new Vector2(r.xMax - w + 2f, y), new Color(0.08f, 0.08f, 0.1f, 1f));
            }
        }

        private void DrawViewportOverlay()
        {
            // anchor marker: a small cross where the effect plays and a line along its forward axis
            if (rig?.camera == null) return;
            var win = Application.Current.Window;
            Vector3 sp = rig.camera.WorldToScreenPoint(_anchorPos, win.Width, win.Height);
            if (sp.z > 0f && _viewport.Contains(new Vector2(sp.x, sp.y)))
            {
                var c = new Color(1f, 0.8f, 0.3f, 0.6f);
                _ui.Batch.FillRect(new Rect(sp.x - 7f, sp.y - 0.5f, 14f, 1f), c);
                _ui.Batch.FillRect(new Rect(sp.x - 0.5f, sp.y - 7f, 1f, 14f), c);
            }
            if (_effect != null && _effectLabelTimer >= 0)
            {
                string s = SurfaceName() + (IsTrail(_effect) ? "  (TRAIL FOLLOWS THE DUMMY)" : IsTracer(_effect) ? "  (TRACER FLIES IN FROM THE LEFT)" : "");
                _ui.Label(s, new Vector2(_viewport.x + 10f, _viewport.yMax - 8f), new Color(1f, 1f, 1f, 0.5f));
            }
        }

        // ------------------------------------------------------------------ left: effects + emitters

        private void DrawLeftPanel(Rect r)
        {
            float half = r.height * 0.5f;
            var top = new Rect(r.x, r.y + r.height - half, r.width, half);
            var bottom = new Rect(r.x, r.y, r.width, r.height - half);

            _ui.Panel(top, "EFFECTS");
            float bh = 22f, bw = (top.width - 16f - 6f) / 3f;
            float by = top.yMax - _ui.LineHeight - bh - 4f;
            if (_ui.Button(new Rect(top.x + 6f, by, bw, bh), "NEW")) NewEffect();
            if (_ui.Button(new Rect(top.x + 6f + bw + 3f, by, bw, bh), "DUP", false, null, _effect != null)) DuplicateEffect();
            if (_ui.Button(new Rect(top.x + 6f + (bw + 3f) * 2f, by, bw, bh), "DEL", false, ImUi.Danger, _effect != null)) DeleteEffect();
            var list = new Rect(top.x + 4f, top.y + 4f, top.width - 8f, by - top.y - 8f);
            float rowH = _ui.LineHeight;
            _listScroll = _ui.ScrollArea(list, _listScroll, _names.Count * rowH);
            _ui.Batch.PushClip(list);
            float y = list.yMax + _listScroll;
            foreach (var n in _names)
            {
                var row = new Rect(list.x, y - rowH, list.width - 6f, rowH);
                y -= rowH;
                if (row.yMax < list.y || row.y > list.yMax) continue;
                bool sel = _effect != null && string.Equals(n, _effect.name, StringComparison.OrdinalIgnoreCase);
                int click = _ui.Row(row, n, sel, null, _dirty.Contains(n) ? "*" : null);
                if (click == 1 && !sel) Select(n);
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(list, _listScroll, _names.Count * rowH);

            _ui.Panel(bottom, "EMITTERS");
            float by2 = bottom.yMax - _ui.LineHeight - bh - 4f;
            float bw4 = (bottom.width - 16f - 9f) / 4f;
            bool hasSel = SelectedEmitter != null;
            if (_ui.Button(new Rect(bottom.x + 6f, by2, bw4, bh), "ADD", false, null, _effect != null)) AddEmitter();
            if (_ui.Button(new Rect(bottom.x + 6f + (bw4 + 3f), by2, bw4, bh), "DEL", false, ImUi.Danger, hasSel)) RemoveEmitter();
            if (_ui.Button(new Rect(bottom.x + 6f + (bw4 + 3f) * 2f, by2, bw4, bh), "UP", false, null, hasSel)) MoveEmitter(-1);
            if (_ui.Button(new Rect(bottom.x + 6f + (bw4 + 3f) * 3f, by2, bw4, bh), "DOWN", false, null, hasSel)) MoveEmitter(1);
            var elist = new Rect(bottom.x + 4f, bottom.y + 4f, bottom.width - 8f, by2 - bottom.y - 8f);
            int count = _effect != null ? _effect.emitters.Count + 1 : 0;
            _emitterScroll = _ui.ScrollArea(elist, _emitterScroll, count * rowH);
            _ui.Batch.PushClip(elist);
            y = elist.yMax + _emitterScroll;
            if (_effect != null)
            {
                var row0 = new Rect(elist.x, y - rowH, elist.width - 6f, rowH); y -= rowH;
                if (_ui.Row(row0, "[EFFECT SETTINGS]", _selEmitter < 0, ImUi.Accent) == 1) _selEmitter = -1;
                for (int i = 0; i < _effect.emitters.Count; i++)
                {
                    var e = _effect.emitters[i];
                    var row = new Rect(elist.x, y - rowH, elist.width - 6f, rowH); y -= rowH;
                    if (row.yMax < elist.y || row.y > elist.yMax) continue;
                    int live = _instance != null && i < _instance.emitters.Length ? _instance.emitters[i].count : 0;
                    int click = _ui.Row(row, (e.enabled ? "" : "(OFF) ") + e.name, i == _selEmitter, e.enabled ? (Color?)null : ImUi.TextDim, live > 0 ? live.ToString() : null);
                    if (click == 1) _selEmitter = i;
                    if (click == 2) { e.enabled = !e.enabled; MarkStructural(); }
                }
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(elist, _emitterScroll, count * rowH);
        }

        // ------------------------------------------------------------------ right: property grid

        private void DrawRightPanel(Rect r)
        {
            var em = SelectedEmitter;
            _ui.Panel(r, em != null ? "EMITTER: " + em.name.ToUpperInvariant() : _effect != null ? "EFFECT: " + _effect.name.ToUpperInvariant() : "PROPERTIES");
            if (_effect == null) return;
            var area = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - _ui.LineHeight - 10f);
            float rowH = 24f;
            float content = EstimateGridHeight(em != null ? EmitterFields : EffectFields, rowH);
            _gridScroll = _ui.ScrollArea(area, _gridScroll, content);
            _ui.Batch.PushClip(area);
            float y = area.yMax + _gridScroll;
            if (em != null) DrawGrid(em, _emitterDefaults, EmitterFields, area, ref y, rowH, true);
            else DrawGrid(_effect, _effectDefaults, EffectFields, area, ref y, rowH, false);
            _ui.Batch.PopClip();
            _ui.ScrollBar(area, _gridScroll, content);
        }

        private float EstimateGridHeight(FieldInfo[] fields, float rowH)
        {
            float h = 0f;
            foreach (var f in fields)
            {
                if (Skip(f)) continue;
                if (f.GetCustomAttribute<VfxGroupAttribute>() != null) h += rowH;
                h += rowH;
                var tip = f.GetCustomAttribute<VfxTipAttribute>();
                if (tip != null) h += rowH * 0.7f;
            }
            return h + rowH;
        }

        private static bool Skip(FieldInfo f) => f.Name == "emitters";

        /// <summary>Reflection-driven widgets: one row per public field, widget by type, guided by the Vfx* attributes.</summary>
        private void DrawGrid(object target, object defaults, FieldInfo[] fields, Rect area, ref float y, float rowH, bool emitter)
        {
            float x = area.x + 2f, w = area.width - 10f;
            foreach (var f in fields)
            {
                if (Skip(f)) continue;
                var group = f.GetCustomAttribute<VfxGroupAttribute>();
                if (group != null)
                {
                    y -= rowH;
                    _ui.Batch.FillRect(new Rect(x, y + 3f, w, rowH - 6f), new Color(1f, 1f, 1f, 0.06f));
                    _ui.Label(group.name.ToUpperInvariant(), new Vector2(x + 4f, y + rowH - 5f), ImUi.Accent);
                }
                y -= rowH;
                var row = new Rect(x, y + 2f, w, rowH - 4f);
                if (row.yMax >= area.y - rowH && row.y <= area.yMax + rowH) DrawField(target, defaults, f, row, emitter);
                var tip = f.GetCustomAttribute<VfxTipAttribute>();
                if (tip != null)
                {
                    y -= rowH * 0.7f;
                    if (y + rowH * 0.7f >= area.y - rowH && y <= area.yMax)
                        _ui.Label(_ui.Fit(tip.text, w - 8f, _ui.FontScale * 0.8f), new Vector2(x + 6f, y + rowH * 0.7f - 3f), ImUi.TextDim, _ui.FontScale * 0.8f);
                }
            }
        }

        private void DrawField(object target, object defaults, FieldInfo f, Rect row, bool emitter)
        {
            string label = Pretty(f.Name);
            var range = f.GetCustomAttribute<VfxRangeAttribute>();
            float min = range?.min ?? -10000f, max = range?.max ?? 10000f, step = range?.step ?? 0.01f;
            object value = f.GetValue(target);
            object def = f.GetValue(defaults);
            float labelW = row.width * 0.42f;
            var labelPos = new Vector2(row.x + 2f, row.y + (row.height + BitmapFont.GlyphH * _ui.FontScale) * 0.5f);
            var cell = new Rect(row.x + labelW, row.y, row.width - labelW, row.height);

            if (f.FieldType == typeof(float))
            {
                _ui.Label(label, labelPos, ImUi.Text);
                float v = (float)value, nv = _ui.DragValue(cell, null, v, step, min, max, (float)def);
                if (nv != v) { f.SetValue(target, nv); Changed(f); }
            }
            else if (f.FieldType == typeof(int))
            {
                _ui.Label(label, labelPos, ImUi.Text);
                int v = (int)value;
                float nv = _ui.DragValue(cell, null, v, Math.Max(1f, step), min, max, (int)def, "0");
                if ((int)nv != v) { f.SetValue(target, (int)nv); Changed(f); }
            }
            else if (f.FieldType == typeof(bool))
            {
                bool v = (bool)value, nv = _ui.Toggle(cell, label, v);
                if (nv != v) { f.SetValue(target, nv); Changed(f); }
            }
            else if (f.FieldType.IsEnum)
            {
                _ui.Label(label, labelPos, ImUi.Text);
                var values = Enum.GetValues(f.FieldType);
                int idx = Array.IndexOf(values, value);
                int d = _ui.Cell(cell, value.ToString().ToUpperInvariant());
                if (d != 0) { idx = ((idx + d) % values.Length + values.Length) % values.Length; f.SetValue(target, values.GetValue(idx)); Changed(f); }
            }
            else if (f.FieldType == typeof(string))
            {
                string v = (string)value ?? "";
                if (f.GetCustomAttribute<VfxSpriteAttribute>() != null)
                {
                    _ui.Label(label, labelPos, ImUi.Text);
                    DrawSpriteCell(cell, v, picked => { f.SetValue(target, picked); Changed(f); });
                }
                else if (f.GetCustomAttribute<VfxEffectRefAttribute>() != null)
                {
                    _ui.Label(label, labelPos, ImUi.Text);
                    int d = _ui.Cell(cell, string.IsNullOrEmpty(v) ? "(NONE)" : v.ToUpperInvariant());
                    if (d != 0)
                    {
                        var opts = new List<string> { "" }; opts.AddRange(_names);
                        int i = Math.Max(0, opts.FindIndex(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase)));
                        i = ((i + d) % opts.Count + opts.Count) % opts.Count;
                        f.SetValue(target, opts[i]); Changed(f);
                    }
                }
                else
                {
                    _ui.Label(label, labelPos, ImUi.Text);
                    if (_ui.Button(cell, string.IsNullOrEmpty(v) ? "(EMPTY)" : v))
                    {
                        if (f.Name == "name" && !emitter) _prompt.Open("RENAME EFFECT", v, RenameEffect);
                        else _prompt.Open(label, v, s => { f.SetValue(target, s); Changed(f); });
                    }
                }
            }
            else if (f.FieldType == typeof(List<string>))
            {
                var list = (List<string>)value ?? new List<string>();
                _ui.Label($"{label} ({list.Count})", labelPos, ImUi.Text);
                float third = cell.width / 3f;
                if (_ui.Button(new Rect(cell.x, cell.y, third - 2f, cell.height), "+ ADD")) OpenPalette(picked => { list.Add(picked); f.SetValue(target, list); Changed(f); });
                if (_ui.Button(new Rect(cell.x + third, cell.y, third - 2f, cell.height), "- LAST", false, null, list.Count > 0)) { list.RemoveAt(list.Count - 1); Changed(f); }
                if (_ui.Button(new Rect(cell.x + third * 2f, cell.y, third - 2f, cell.height), "CLEAR", false, ImUi.Danger, list.Count > 0)) { list.Clear(); Changed(f); }
            }
            else if (f.FieldType == typeof(FloatRange))
            {
                _ui.Label(label, labelPos, ImUi.Text);
                var v = (FloatRange)value; var dv = (FloatRange)def;
                float half = cell.width * 0.5f;
                float rstep = range?.step ?? (MathF.Abs(v.Max) >= 20f ? 1f : MathF.Abs(v.Max) >= 2f ? 0.1f : 0.01f);
                float rmin = range?.min ?? -10000f, rmax = range?.max ?? 10000f;
                float a = _ui.DragValue(new Rect(cell.x, cell.y, half - 2f, cell.height), "MIN", v.min, rstep, rmin, rmax, dv.min);
                float b = _ui.DragValue(new Rect(cell.x + half, cell.y, half - 2f, cell.height), "MAX", v.max, rstep, rmin, rmax, dv.max);
                if (a != v.min || b != v.max) { f.SetValue(target, new FloatRange(a, Math.Max(a, b))); Changed(f); }
            }
            else if (f.FieldType == typeof(Color))
            {
                _ui.Label(label, labelPos, ImUi.Text);
                var c = (Color)value; var dc = (Color)def;
                float sw = cell.height;
                _ui.Swatch(new Rect(cell.x, cell.y, sw, cell.height), c);
                float cw = (cell.width - sw - 4f) / 4f;
                float cx = cell.x + sw + 4f;
                float r = _ui.DragValue(new Rect(cx, cell.y, cw - 2f, cell.height), "R", c.r, 0.01f, 0f, 4f, dc.r); cx += cw;
                float g = _ui.DragValue(new Rect(cx, cell.y, cw - 2f, cell.height), "G", c.g, 0.01f, 0f, 4f, dc.g); cx += cw;
                float b = _ui.DragValue(new Rect(cx, cell.y, cw - 2f, cell.height), "B", c.b, 0.01f, 0f, 4f, dc.b); cx += cw;
                float a = _ui.DragValue(new Rect(cx, cell.y, cw - 2f, cell.height), "A", c.a, 0.01f, 0f, 1f, dc.a);
                if (r != c.r || g != c.g || b != c.b || a != c.a) { f.SetValue(target, new Color(r, g, b, a)); Changed(f); }
            }
            else if (f.FieldType == typeof(Vector3))
            {
                _ui.Label(label, labelPos, ImUi.Text);
                var v = (Vector3)value; var dv = (Vector3)def;
                float cw = cell.width / 3f;
                float vx = _ui.DragValue(new Rect(cell.x, cell.y, cw - 2f, cell.height), "X", v.x, 0.01f, -100f, 100f, dv.x);
                float vy = _ui.DragValue(new Rect(cell.x + cw, cell.y, cw - 2f, cell.height), "Y", v.y, 0.01f, -100f, 100f, dv.y);
                float vz = _ui.DragValue(new Rect(cell.x + cw * 2f, cell.y, cw - 2f, cell.height), "Z", v.z, 0.01f, -100f, 100f, dv.z);
                if (vx != v.x || vy != v.y || vz != v.z) { f.SetValue(target, new Vector3(vx, vy, vz)); Changed(f); }
            }
            else
            {
                _ui.Label(label, labelPos, ImUi.TextDim);
                _ui.Label(value?.ToString() ?? "null", new Vector2(cell.x + 4f, labelPos.y), ImUi.TextDim);
            }
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
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ sprite cell + palette

        private void DrawSpriteCell(Rect cell, string reference, Action<string> accept)
        {
            var sprite = SpriteLibrary.Resolve(reference);
            float sw = cell.height;
            if (sprite != null)
            {
                _ui.Batch.FillRect(new Rect(cell.x, cell.y, sw, sw), new Color(0.2f, 0.2f, 0.22f, 1f));
                DrawSpriteFit(sprite, new Rect(cell.x + 1f, cell.y + 1f, sw - 2f, sw - 2f));
            }
            if (_ui.Button(new Rect(cell.x + sw + 2f, cell.y, cell.width - sw - 2f, cell.height), string.IsNullOrEmpty(reference) ? "(PICK SPRITE)" : reference))
                OpenPalette(accept);
        }

        private void DrawSpriteFit(Sprite sprite, Rect box)
        {
            float sx = box.width / sprite.rect.width, sy = box.height / sprite.rect.height;
            float s = MathF.Min(sx, sy);
            float w = sprite.rect.width * s, h = sprite.rect.height * s;
            _ui.Batch.Draw(sprite, new Rect(box.x + (box.width - w) * 0.5f, box.y + (box.height - h) * 0.5f, w, h), Color.white);
        }

        private void OpenPalette(Action<string> accept)
        {
            _paletteOpen = true;
            _paletteAccept = accept;
            _paletteScroll = 0f;
        }

        private void DrawPalette(Rect r)
        {
            _ui.Modal = false; // the palette's own widgets must react
            _ui.Batch.FillRect(new Rect(0, 0, _ui.Batch.Width, _ui.Batch.Height), new Color(0f, 0f, 0f, 0.45f));
            _ui.Panel(r, "PICK A SPRITE   (ESC = CANCEL)");
            var atlases = new List<(string key, SpriteAtlas atlas)>(SpriteLibrary.Atlases);
            // Particles first
            int pi = atlases.FindIndex(a => a.key == "Particles");
            if (pi > 0) { var p = atlases[pi]; atlases.RemoveAt(pi); atlases.Insert(0, p); }
            if (atlases.Count == 0) return;
            _paletteAtlas = Math.Clamp(_paletteAtlas, 0, atlases.Count - 1);

            // atlas tabs
            float tx = r.x + 6f, ty = r.yMax - _ui.LineHeight - 26f;
            for (int i = 0; i < atlases.Count; i++)
            {
                float tw = _ui.TextWidth(atlases[i].key) + 14f;
                if (tx + tw > r.xMax - 6f) { tx = r.x + 6f; ty -= 24f; }
                if (_ui.Button(new Rect(tx, ty, tw, 22f), atlases[i].key, i == _paletteAtlas)) { _paletteAtlas = i; _paletteScroll = 0f; }
                tx += tw + 4f;
            }
            var (key, atlas) = atlases[_paletteAtlas];
            var grid = new Rect(r.x + 6f, r.y + 6f, r.width - 12f, ty - r.y - 10f);
            const float cellW = 104f, cellH = 96f;
            int cols = Math.Max(1, (int)(grid.width / cellW));
            var names = new List<string>(atlas.Sprites.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            int rows = (names.Count + cols - 1) / cols;
            _paletteScroll = _ui.ScrollArea(grid, _paletteScroll, rows * cellH);
            _ui.Batch.PushClip(grid);
            for (int i = 0; i < names.Count; i++)
            {
                int cx = i % cols, cy = i / cols;
                var box = new Rect(grid.x + cx * cellW, grid.yMax + _paletteScroll - (cy + 1) * cellH, cellW - 4f, cellH - 4f);
                if (box.yMax < grid.y || box.y > grid.yMax) continue;
                bool hover = _ui.Hover(box);
                _ui.Batch.FillRect(box, hover ? ImUi.ButtonHover : new Color(0.14f, 0.15f, 0.18f, 1f));
                _ui.Border(box, hover ? ImUi.Accent : ImUi.PanelBorder);
                var sprite = atlas.Sprites[names[i]];
                DrawSpriteFit(sprite, new Rect(box.x + 6f, box.y + 20f, box.width - 12f, box.height - 26f));
                _ui.Label(_ui.Fit(names[i], box.width - 6f, 1.2f), new Vector2(box.x + 3f, box.y + 16f), hover ? ImUi.Accent : ImUi.TextDim, 1.2f);
                if (hover && _ui.MouseDown && !_ui.ClickConsumed)
                {
                    _ui.ClickConsumed = true;
                    string reference = key + "/" + names[i];
                    _paletteOpen = false;
                    var accept = _paletteAccept; _paletteAccept = null;
                    accept?.Invoke(reference);
                }
            }
            _ui.Batch.PopClip();
            _ui.ScrollBar(grid, _paletteScroll, rows * cellH);
            if (_ui.MouseDown && !_ui.ClickConsumed && !r.Contains(_ui.Mouse)) { _paletteOpen = false; _paletteAccept = null; _ui.ClickConsumed = true; }
            _ui.Modal = true;
        }

        // ------------------------------------------------------------------ help

        private void DrawHelp(Rect r)
        {
            _ui.Batch.FillRect(new Rect(0, 0, _ui.Batch.Width, _ui.Batch.Height), new Color(0f, 0f, 0f, 0.55f));
            _ui.Panel(r, "VFX EDITOR — HELP   (F1 / ESC CLOSES)");
            string[] lines =
            {
                "EFFECTS: NEW (CTRL+N)  DUP (CTRL+D)  DEL (SHIFT+DEL)  F2 RENAME  PGUP/PGDN PREVIOUS/NEXT  CTRL+S SAVE ALL",
                "EMITTERS: ADD (INS / CTRL+E)  DEL  UP/DOWN + CTRL REORDER  RMB ON A ROW TOGGLES IT  ARROWS SELECT",
                "GRID: DRAG A NUMBER LEFT/RIGHT (SHIFT X10, CTRL /10)  WHEEL +/-  RMB RESETS TO DEFAULT",
                "      ENUM CELLS: LMB NEXT / RMB PREVIOUS   SPRITE: CLICK TO OPEN THE PALETTE   FRAMES: + ADD / - LAST",
                "PREVIEW: SPACE REPLAY  L LOOP  P PAUSE  [ ] SPEED  T WALL/FLOOR/AIR  LMB PLACES THE EFFECT ON A SURFACE",
                "         RMB DRAG ORBITS  WHEEL ZOOMS  TRAILS FOLLOW THE FLYING DUMMY  TRACERS FLY IN TOWARDS THE ANCHOR",
                "UNDO: CTRL+Z / CTRL+Y (PER EFFECT)",
                "",
                "EFFECT SPACE: Z = FORWARD = SURFACE NORMAL (IMPACTS) OR FIRE DIRECTION (TRACERS); OFFSETS ARE IN METRES.",
                "RENDER MODES: BILLBOARD FACES THE CAMERA, STRETCHED FOLLOWS VELOCITY (SPARKS/TRACERS),",
                "  HORIZONTAL LIES ON THE GROUND, ALIGNED LIES ON THE HIT SURFACE (DECALS, FLASHES, SHOCKWAVES).",
                "COLLISION: PARTICLES RAYCAST AGAINST THE WORLD; COLLISION EFFECT = ANOTHER EFFECT SPAWNED WHERE THEY LAND.",
                "BINDINGS: vfx/bindings.json MAPS SURFACES (CONCRETE, WOOD, METAL, GLASS, SAND, WATER, FLESH),",
                "  AMMO TYPES (TRACER + SCALES), EXPLOSIONS, TRAILS AND DEATHS TO EFFECT NAMES. THE GAME HOT-RELOADS SAVES.",
            };
            float y = r.yMax - _ui.LineHeight - 10f;
            foreach (var l in lines) { _ui.Label(l, new Vector2(r.x + 12f, y), l.StartsWith("  ") || l.StartsWith("      ") ? ImUi.TextDim : ImUi.Text); y -= _ui.LineHeight; }
        }
    }
}
