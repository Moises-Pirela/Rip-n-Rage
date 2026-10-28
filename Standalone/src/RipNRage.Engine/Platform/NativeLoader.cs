using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RipNRage.Engine.Platform
{
    /// <summary>
    /// Resolves native libraries (GLFW, OpenAL) across platforms without any NuGet packages.
    /// Search order: RIPNRAGE_NATIVE_DIR, &lt;app&gt;/native/&lt;rid&gt;, &lt;app&gt;, then the system loader
    /// with the platform specific file names.
    /// </summary>
    public static class NativeLoader
    {
        public const string GlfwLib = "glfw";
        public const string OpenAlLib = "openal";

        private static readonly Dictionary<string, string[]> Candidates = new Dictionary<string, string[]>
        {
            [GlfwLib] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "glfw3.dll", "glfw.dll", "libglfw.dll" }
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new[] { "libglfw.3.dylib", "libglfw.dylib", "/opt/homebrew/lib/libglfw.3.dylib", "/usr/local/lib/libglfw.3.dylib" }
                    : new[] { "libglfw.so.3", "libglfw.so", "libglfw.so.3.3", "libglfw.so.3.4" },
            [OpenAlLib] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "OpenAL32.dll", "soft_oal.dll", "openal32.dll" }
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new[] { "libopenal.1.dylib", "libopenal.dylib", "/opt/homebrew/lib/libopenal.1.dylib", "/System/Library/Frameworks/OpenAL.framework/OpenAL" }
                    : new[] { "libopenal.so.1", "libopenal.so" },
        };

        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);
        }

        private static string Rid
        {
            get
            {
                string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "arm64",
                    Architecture.X86 => "x86",
                    _ => "x64",
                };
                return os + "-" + arch;
            }
        }

        private static IEnumerable<string> SearchDirs()
        {
            string env = Environment.GetEnvironmentVariable("RIPNRAGE_NATIVE_DIR");
            if (!string.IsNullOrEmpty(env)) yield return env;
            string baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "native", Rid);
            yield return Path.Combine(baseDir, "runtimes", Rid, "native");
            yield return baseDir;
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!Candidates.TryGetValue(libraryName, out var names)) return IntPtr.Zero;

            foreach (var dir in SearchDirs())
            {
                foreach (var n in names)
                {
                    if (Path.IsPathRooted(n)) continue;
                    string full = Path.Combine(dir, n);
                    if (File.Exists(full) && NativeLibrary.TryLoad(full, out var h)) return h;
                }
            }
            foreach (var n in names)
                if (NativeLibrary.TryLoad(n, out var h)) return h;

            return IntPtr.Zero;
        }

        public static bool IsAvailable(string libraryName)
        {
            Register();
            return Resolve(libraryName, typeof(NativeLoader).Assembly, null) != IntPtr.Zero;
        }
    }
}
