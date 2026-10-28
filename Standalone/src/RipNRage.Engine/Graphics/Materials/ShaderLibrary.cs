using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Named shader programs loaded from <c>shaders/&lt;name&gt;.glsl</c> files and hot-reloaded when
    /// they change on disk. A file contains both stages:
    /// <code>
    /// #pragma vertex
    /// ... GLSL ...
    /// #pragma fragment
    /// #include "lighting.glsl"
    /// ... GLSL ...
    /// </code>
    /// <c>#include</c> resolves relative to <c>shaders/include/</c>. When a file is missing the
    /// embedded built-in source of the same name is used, so the engine also works without the folder.
    /// A compile error keeps the previous program alive (or a magenta fallback) and is logged.
    /// </summary>
    public static class ShaderLibrary
    {
        private sealed class Entry
        {
            public Shader shader;
            public string name;
            public string path;                       // null = embedded only
            public readonly List<(string file, DateTime stamp)> deps = new List<(string, DateTime)>();
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> EmbeddedIncludes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (string vs, string fs)> Embedded = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        private static double _nextPoll;

        public static string Directory { get; private set; }
        public static event Action<Shader> Reloaded;

        static ShaderLibrary()
        {
            EmbeddedIncludes["lighting.glsl"] = BuiltinShaders.LightingCommon;
            Embedded["mesh"] = (BuiltinShaders.MeshVert, BuiltinShaders.MeshFrag);
            Embedded["sprite"] = (BuiltinShaders.SpriteVert, BuiltinShaders.SpriteFrag);
            Embedded["ui"] = (BuiltinShaders.UiVert, BuiltinShaders.UiFrag);
            Embedded["sky"] = (BuiltinShaders.SkyVert, BuiltinShaders.SkyFrag);
        }

        /// <summary>Finds the shaders folder (explicit, RIPNRAGE_SHADERS, or a "shaders" dir up from cwd / exe).</summary>
        public static string Locate(string explicitDir)
        {
            if (!string.IsNullOrEmpty(explicitDir) && System.IO.Directory.Exists(explicitDir)) return Path.GetFullPath(explicitDir);
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_SHADERS");
            if (!string.IsNullOrEmpty(env) && System.IO.Directory.Exists(env)) return env;
            foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "shaders");
                    if (System.IO.Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "sprite.glsl"))) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        public static void Initialize(string directory)
        {
            Directory = directory;
            if (Directory != null) Debug.LogWarning("Shaders: " + Directory + " (hot reload on)");
        }

        /// <summary>Registers an embedded fallback source for a shader name (engine internals).</summary>
        public static void RegisterEmbedded(string name, string vertex, string fragment) => Embedded[name] = (vertex, fragment);

        /// <summary>Returns the shader for a name, loading it (file first, embedded second).</summary>
        public static Shader Get(string name)
        {
            if (Entries.TryGetValue(name, out var e)) return e.shader;
            e = new Entry { name = name };
            if (!TryCompile(e, out string error))
            {
                Debug.LogError($"Shader '{name}' failed to compile: {error}");
                if (e.shader == null) e.shader = new Shader(BuiltinShaders.ErrorVert, BuiltinShaders.ErrorFrag, name + ".error");
            }
            Entries[name] = e;
            return e.shader;
        }

        public static bool Exists(string name) =>
            Entries.ContainsKey(name) || Embedded.ContainsKey(name) || (Directory != null && File.Exists(Path.Combine(Directory, name + ".glsl")));

        /// <summary>Creates a shader from inline source and registers it under a name (no file backing).</summary>
        public static Shader Register(string name, string vertex, string fragment)
        {
            Embedded[name] = (vertex, fragment);
            Entries.Remove(name);
            return Get(name);
        }

        /// <summary>Call once per frame: recompiles shaders whose files (or includes) changed.</summary>
        public static void Poll(double now)
        {
            if (Directory == null || now < _nextPoll) return;
            _nextPoll = now + 0.5;
            foreach (var e in Entries.Values)
            {
                if (e.path == null)
                {
                    // an embedded shader may have gained a file
                    string p = Path.Combine(Directory, e.name + ".glsl");
                    if (!File.Exists(p)) continue;
                }
                bool changed = false;
                foreach (var (file, stamp) in e.deps)
                {
                    try { if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) != stamp) { changed = true; break; } }
                    catch (IOException) { }
                }
                if (e.path == null || changed)
                {
                    if (TryCompile(e, out string error)) { Debug.LogWarning($"Shader '{e.name}' reloaded"); Reloaded?.Invoke(e.shader); }
                    else Debug.LogError($"Shader '{e.name}' reload failed (keeping previous program): {error}");
                }
            }
        }

        public static void ReloadAll()
        {
            foreach (var e in Entries.Values)
                if (TryCompile(e, out string error)) Reloaded?.Invoke(e.shader);
                else Debug.LogError($"Shader '{e.name}' reload failed: {error}");
        }

        private static bool TryCompile(Entry e, out string error)
        {
            error = null;
            e.deps.Clear();
            string vs, fs;
            string file = Directory != null ? Path.Combine(Directory, e.name + ".glsl") : null;
            try
            {
                if (file != null && File.Exists(file))
                {
                    e.path = file;
                    e.deps.Add((file, File.GetLastWriteTimeUtc(file)));
                    ParseFile(File.ReadAllText(file), e, out vs, out fs);
                }
                else if (Embedded.TryGetValue(e.name, out var src))
                {
                    e.path = null;
                    vs = ResolveIncludes(src.vs, e); fs = ResolveIncludes(src.fs, e);
                }
                else { error = "no file " + (file ?? e.name + ".glsl") + " and no embedded source"; return false; }

                if (e.shader == null) e.shader = new Shader(vs, fs, e.name);
                else e.shader.Recompile(vs, fs);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ParseFile(string text, Entry e, out string vs, out string fs)
        {
            var vert = new StringBuilder(); var frag = new StringBuilder(); var common = new StringBuilder();
            int stage = 0; // 0 common, 1 vertex, 2 fragment
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.StartsWith("#pragma vertex")) { stage = 1; continue; }
                if (t.StartsWith("#pragma fragment")) { stage = 2; continue; }
                (stage == 1 ? vert : stage == 2 ? frag : common).Append(raw).Append('\n');
            }
            if (vert.Length == 0 || frag.Length == 0) throw new InvalidDataException("shader file needs '#pragma vertex' and '#pragma fragment' sections");
            string header = common.ToString();
            vs = ResolveIncludes(InsertAfterVersion(header, vert.ToString()), e);
            fs = ResolveIncludes(InsertAfterVersion(header, frag.ToString()), e);
        }

        /// <summary>Common text (before the first #pragma) is shared by both stages; a #version line must stay first.</summary>
        private static string InsertAfterVersion(string common, string stage)
        {
            if (common.Trim().Length == 0) return stage;
            var lines = stage.Split('\n');
            int i = 0;
            while (i < lines.Length && lines[i].Trim().Length == 0) i++;
            if (i < lines.Length && lines[i].TrimStart().StartsWith("#version"))
                return string.Join("\n", lines, 0, i + 1) + "\n" + common + string.Join("\n", lines, i + 1, lines.Length - i - 1);
            return common + stage;
        }

        private static readonly Regex IncludeRx = new Regex("^\\s*#include\\s+[\"<]([^\">]+)[\">]", RegexOptions.Multiline);

        private static string ResolveIncludes(string src, Entry e, int depth = 0)
        {
            if (depth > 8) throw new InvalidDataException("include recursion too deep");
            return IncludeRx.Replace(src, m =>
            {
                string inc = m.Groups[1].Value;
                string body = null;
                if (Directory != null)
                {
                    string p = Path.Combine(Directory, "include", inc);
                    if (!File.Exists(p)) p = Path.Combine(Directory, inc);
                    if (File.Exists(p)) { body = File.ReadAllText(p); e.deps.Add((p, File.GetLastWriteTimeUtc(p))); }
                }
                if (body == null && !EmbeddedIncludes.TryGetValue(inc, out body)) throw new FileNotFoundException("include not found: " + inc);
                return "\n// ---- begin " + inc + "\n" + ResolveIncludes(body, e, depth + 1) + "\n// ---- end " + inc + "\n";
            });
        }
    }
}
