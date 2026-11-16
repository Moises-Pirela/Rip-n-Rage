using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ImGuiNET;
using RipNRage.Engine.Ecs;
using Vec2 = System.Numerics.Vector2;
using Vec3 = System.Numerics.Vector3;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.ImGuiIntegration
{
    /// <summary>
    /// One editable value in a <see cref="PropertyGrid"/>. The grid knows nothing about where a value lives: a row
    /// carries the value to show, the default to compare against, and the two callbacks that write it back or put the
    /// default back. The level editor builds rows over map keys, the prefab window over config fields, the particle
    /// editor over emitter objects — one grid.
    /// </summary>
    public sealed class PropertyRow
    {
        public string Id, Label, Group, Tip;
        public Type Type;
        public object Value, Default;
        /// <summary>The value is not the default (drawn in the accent colour; right-click puts the default back).</summary>
        public bool Overridden;
        /// <summary>Several things are selected and they disagree: the first value is shown, an edit writes all of them.</summary>
        public bool Mixed;
        public bool ReadOnly;
        /// <summary>A problem the last write caused, shown under the row.</summary>
        public string Error;
        public InspectRangeAttribute Range;
        public bool IsColor;
        public AssetKind? Asset;
        /// <summary>Called with the new value on every change (a drag calls it every frame).</summary>
        public Action<object> Set;
        /// <summary>Called once when an edit begins (the undo step).</summary>
        public Action Began;
        /// <summary>Puts the default back (removes an override).</summary>
        public Action Revert;

        /// <summary>A row for a reflected field: label, type, hints and the default from <paramref name="defaults"/>.</summary>
        public static PropertyRow ForField(FieldInfo f, object target, object defaults, string group = null)
        {
            var row = new PropertyRow
            {
                Id = (group ?? "") + "." + f.Name, Label = f.Name, Group = group, Type = f.FieldType,
                Value = f.GetValue(target), Default = defaults != null ? f.GetValue(defaults) : null,
                Range = f.GetCustomAttribute<InspectRangeAttribute>(), Tip = f.GetCustomAttribute<InspectTipAttribute>()?.text,
                IsColor = f.GetCustomAttribute<InspectColorAttribute>() != null, Asset = f.GetCustomAttribute<InspectAssetAttribute>()?.kind,
            };
            if (f.FieldType == typeof(EntityRef)) row.Asset = AssetKind.Entity;
            row.Overridden = defaults != null && !PropertyGrid.ValuesEqual(row.Value, row.Default);
            return row;
        }
    }

    /// <summary>Where the grid gets names for its combos: the registry behind an asset kind, the entities of a scene.</summary>
    public interface IPropertySources
    {
        IReadOnlyList<string> Names(AssetKind kind);
    }

    /// <summary>
    /// A reflection-free property grid (10.6): rows in, widgets by type, edits out through the rows' callbacks.
    /// Numbers drag (Shift ×10, Alt ÷10 as ImGui does), enums are combos, colours get a picker, asset names a combo
    /// over the registry with free text for anything else, nullable numbers a "set" box. An overridden row is drawn in
    /// the accent colour and reverts on right-click; a filter hides the rows whose label does not contain it.
    /// </summary>
    public static class PropertyGrid
    {
        public static readonly Vec4 Accent = new Vec4(1f, 0.78f, 0.25f, 1f);
        public static readonly Vec4 Dim = new Vec4(0.55f, 0.58f, 0.63f, 1f);
        public static readonly Vec4 Danger = new Vec4(0.9f, 0.3f, 0.25f, 1f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Draws every row (grouped under collapsing headers when they carry a group). True when any row wrote a value.</summary>
        public static bool Draw(IReadOnlyList<PropertyRow> rows, string filter, IPropertySources sources, float labelWidth = 170f, Action<string> groupExtras = null)
        {
            bool changed = false;
            string group = null; bool groupOpen = true;
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(filter) && !Matches(row, filter)) continue;
                if (row.Group != group)
                {
                    group = row.Group;
                    groupOpen = group == null || ImGui.CollapsingHeader(group, ImGuiTreeNodeFlags.DefaultOpen);
                    if (group != null) groupExtras?.Invoke(group);
                }
                if (!groupOpen) continue;
                if (Field(row, sources, labelWidth)) changed = true;
            }
            return changed;
        }

        private static bool Matches(PropertyRow row, string filter) =>
            row.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) || (row.Group != null && row.Group.Contains(filter, StringComparison.OrdinalIgnoreCase));

        /// <summary>One row. True when it wrote a value this frame.</summary>
        public static bool Field(PropertyRow row, IPropertySources sources, float labelWidth = 170f)
        {
            ImGui.PushID(row.Id ?? row.Label);
            bool changed = false;
            // the label column
            if (row.Overridden) ImGui.TextColored(Accent, "* " + row.Label); else if (row.ReadOnly) ImGui.TextColored(Dim, row.Label); else ImGui.TextUnformatted(row.Label);
            if (ImGui.IsItemHovered())
            {
                if (row.Tip != null || row.Overridden || row.Mixed)
                    ImGui.SetTooltip((row.Tip ?? "") + (row.Overridden ? (row.Tip != null ? "\n" : "") + "overridden - right-click to revert" : "") + (row.Mixed ? "\nthe selection disagrees; an edit writes all of them" : ""));
                if (row.Overridden && row.Revert != null && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { row.Revert(); changed = true; }
            }
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(-1f);
            if (row.Mixed) ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vec4(0.3f, 0.28f, 0.2f, 1f));
            if (row.ReadOnly) ImGui.BeginDisabled();
            try { changed |= Widget(row, sources); }
            finally
            {
                if (row.ReadOnly) ImGui.EndDisabled();
                if (row.Mixed) ImGui.PopStyleColor();
            }
            if (row.Overridden && row.Revert != null && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { row.Revert(); changed = true; }
            if (row.Error != null) ImGui.TextColored(Danger, "   " + row.Error);
            ImGui.PopID();
            return changed;
        }

        private static bool Widget(PropertyRow row, IPropertySources sources)
        {
            var t = row.Type;
            object v = row.Value;
            float min = row.Range?.min ?? -1e6f, max = row.Range?.max ?? 1e6f, step = row.Range?.step ?? 0.01f;

            if (t == typeof(float))
            {
                float f = v is float x ? x : 0f;
                bool c = ImGui.DragFloat("##v", ref f, step, min, max, "%.4g");
                return Commit(row, c, f);
            }
            if (t == typeof(int))
            {
                int i = v is int x ? x : 0;
                bool c = ImGui.DragInt("##v", ref i, MathF.Max(0.1f, step), (int)min, (int)max);
                return Commit(row, c, i);
            }
            if (t == typeof(bool))
            {
                bool b = v is bool x && x;
                bool c = ImGui.Checkbox("##v", ref b);
                return Commit(row, c, b);
            }
            if (t.IsEnum)
            {
                var names = Enum.GetNames(t); var values = Enum.GetValues(t);
                int idx = v != null ? Array.IndexOf(values, v) : 0; if (idx < 0) idx = 0;
                bool c = ImGui.Combo("##v", ref idx, names, names.Length);
                return Commit(row, c, values.GetValue(idx));
            }
            if (t == typeof(float?))
            {
                float? n = v as float?;
                bool has = n.HasValue;
                bool c = ImGui.Checkbox("##set", ref has);
                ImGui.SameLine();
                float f = n ?? 0f;
                ImGui.SetNextItemWidth(-1f);
                ImGui.BeginDisabled(!has);
                bool c2 = ImGui.DragFloat("##v", ref f, step, min, max, "%.4g");
                ImGui.EndDisabled();
                if (c || c2) return Commit(row, true, has ? f : (float?)null);
                return false;
            }
            if (t == typeof(float[]))
            {
                var arr = v as float[] ?? Array.Empty<float>();
                if (row.IsColor && (arr.Length == 3 || arr.Length == 4))
                {
                    if (arr.Length == 3) { var c3 = new Vec3(arr[0], arr[1], arr[2]); bool c = ImGui.ColorEdit3("##v", ref c3, ImGuiColorEditFlags.Float); return Commit(row, c, new[] { c3.X, c3.Y, c3.Z }); }
                    var c4 = new Vec4(arr[0], arr[1], arr[2], arr[3]); bool cc = ImGui.ColorEdit4("##v", ref c4, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.AlphaBar); return Commit(row, cc, new[] { c4.X, c4.Y, c4.Z, c4.W });
                }
                switch (arr.Length)
                {
                    case 2: { var a = new Vec2(arr[0], arr[1]); bool c = ImGui.DragFloat2("##v", ref a, step, min, max, "%.4g"); return Commit(row, c, new[] { a.X, a.Y }); }
                    case 3: { var a = new Vec3(arr[0], arr[1], arr[2]); bool c = ImGui.DragFloat3("##v", ref a, step, min, max, "%.4g"); return Commit(row, c, new[] { a.X, a.Y, a.Z }); }
                    case 4: { var a = new Vec4(arr[0], arr[1], arr[2], arr[3]); bool c = ImGui.DragFloat4("##v", ref a, step, min, max, "%.4g"); return Commit(row, c, new[] { a.X, a.Y, a.Z, a.W }); }
                    default:
                    {
                        // other lengths (or none yet): text, "a b c"
                        string text = Join(arr);
                        if (TextInput(row, ref text) && TryParseFloats(text, out var parsed)) return Commit(row, true, parsed);
                        return false;
                    }
                }
            }
            if (t == typeof(string) || t == typeof(EntityRef))
            {
                string s = v is EntityRef er ? er.name ?? "" : v as string ?? "";
                bool c = row.Asset.HasValue ? AssetInput(row, ref s, sources) : TextInput(row, ref s);
                if (!c) return false;
                return Commit(row, true, t == typeof(EntityRef) ? (object)new EntityRef(s) : s);
            }
            // anything else is shown, not edited (a convex hull's vertex table)
            ImGui.TextColored(Dim, Describe(v));
            return false;
        }

        private static bool Commit(PropertyRow row, bool changed, object value)
        {
            if (ImGui.IsItemActivated()) row.Began?.Invoke();
            if (!changed || row.ReadOnly) return false;
            row.Value = value;
            row.Set?.Invoke(value);
            return true;
        }

        /// <summary>A text box that commits when the edit ends (Enter or focus lost), not on every keystroke.</summary>
        private static bool TextInput(PropertyRow row, ref string text)
        {
            string buf = text;
            ImGui.InputText("##v", ref buf, 256);
            if (ImGui.IsItemActivated()) row.Began?.Invoke();
            if (ImGui.IsItemDeactivatedAfterEdit() && buf != text) { text = buf; return true; }
            return false;
        }

        /// <summary>A text box with a list of the registry's names beside it; free text stays allowed.</summary>
        private static bool AssetInput(PropertyRow row, ref string text, IPropertySources sources)
        {
            float w = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(MathF.Max(40f, w - 26f));
            bool c = TextInput(row, ref text);
            ImGui.SameLine(0f, 2f);
            var names = sources?.Names(row.Asset!.Value);
            if (ImGui.Button("v", new Vec2(22f, 0f))) ImGui.OpenPopup("##pick");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(names == null || names.Count == 0 ? $"no {row.Asset} names are loaded here - type one" : $"pick a {row.Asset}");
            if (ImGui.BeginPopup("##pick"))
            {
                if (names == null || names.Count == 0) ImGui.TextColored(Dim, "(none loaded)");
                else
                {
                    ImGui.SetNextItemWidth(220f);
                    ImGui.InputTextWithHint("##filter", "filter", ref _pickFilter, 64);
                    if (ImGui.BeginChild("##list", new Vec2(220f, MathF.Min(300f, 18f * MathF.Min(names.Count, 16) + 8f))))
                    {
                        foreach (var n in names)
                        {
                            if (!string.IsNullOrEmpty(_pickFilter) && !n.Contains(_pickFilter, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ImGui.Selectable(n, string.Equals(n, text, StringComparison.OrdinalIgnoreCase))) { text = n; c = true; ImGui.CloseCurrentPopup(); }
                        }
                    }
                    ImGui.EndChild();
                }
                ImGui.EndPopup();
            }
            if (c) row.Began?.Invoke();
            return c;
        }
        private static string _pickFilter = "";

        // ------------------------------------------------------------------ values as text (what a map key holds)

        /// <summary>A value as the text a map key or an override holds: numbers invariant, arrays space-separated, bools 0 / 1, enums by name.</summary>
        public static string ToText(object v)
        {
            switch (v)
            {
                case null: return "";
                case float f: return f.ToString("0.####", Inv);
                case double d: return d.ToString("0.####", Inv);
                case int i: return i.ToString(Inv);
                case bool b: return b ? "1" : "0";
                case string s: return s;
                case EntityRef e: return e.name ?? "";
                case float[] a: return Join(a);
                case Enum en: return en.ToString();
            }
            return v.ToString();
        }

        public static string Join(float[] a)
        {
            if (a == null) return "";
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString("0.####", Inv);
            return string.Join(" ", parts);
        }

        public static bool TryParseFloats(string text, out float[] values)
        {
            var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, Inv, out values[i])) return false;
            return true;
        }

        /// <summary>Value equality for what a config field holds (arrays by element, nullables by value).</summary>
        public static bool ValuesEqual(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is float[] fa && b is float[] fb)
            {
                if (fa.Length != fb.Length) return false;
                for (int i = 0; i < fa.Length; i++) if (fa[i] != fb[i]) return false;
                return true;
            }
            if (a is float[][] ja && b is float[][] jb)
            {
                if (ja.Length != jb.Length) return false;
                for (int i = 0; i < ja.Length; i++) if (!ValuesEqual(ja[i], jb[i])) return false;
                return true;
            }
            if (a is EntityRef ea && b is EntityRef eb) return string.Equals(ea.name ?? "", eb.name ?? "", StringComparison.Ordinal);
            return a.Equals(b);
        }

        private static string Describe(object v) => v switch
        {
            null => "null",
            float[][] rows => $"{rows.Length} rows",
            Array arr => $"{arr.Length} items",
            _ => v.ToString(),
        };
    }
}
