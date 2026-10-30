using System;
using System.Runtime.InteropServices;

namespace RipNRage.Engine.Platform
{
    /// <summary>Which GPU the process should ask the driver stack for.</summary>
    public enum GpuPreference
    {
        /// <summary>Leave the choice to the driver / OS.</summary>
        Auto,
        /// <summary>Ask for the discrete (high-performance) GPU on hybrid systems (Linux PRIME offload; on Windows this is a per-app OS setting, see <see cref="GpuInfo.Remedies"/>).</summary>
        High,
        /// <summary>Explicitly allow a software rasterizer without the warning (automation on headless machines).</summary>
        AllowSoftware,
    }

    /// <summary>
    /// Identifies the OpenGL implementation that ended up rendering the frame and, above all, whether it is a
    /// <b>software rasterizer running on the CPU</b> (Mesa llvmpipe / softpipe / swrast, Microsoft's GDI Generic or
    /// Basic Render Driver, Apple's software renderer, SwiftShader…). With one of those the whole renderer —
    /// shadows, lighting, fog, post-processing — executes on the CPU and nothing in the engine can make it fast;
    /// the fix is on the machine/driver side, so the engine reports it loudly with the likely remedies.
    /// </summary>
    public static class GpuInfo
    {
        public static string Vendor { get; private set; } = "";
        public static string Renderer { get; private set; } = "";
        public static string Version { get; private set; } = "";
        public static string GlslVersion { get; private set; } = "";
        /// <summary>True when the renderer string identifies a CPU rasterizer.</summary>
        public static bool IsSoftware { get; private set; }
        /// <summary>True for integrated GPUs on systems that usually also have a discrete one (laptops).</summary>
        public static bool IsIntegrated { get; private set; }
        /// <summary>Human readable summary for logs / the overlay.</summary>
        public static string Summary { get; private set; } = "";
        /// <summary>Suggestions when the renderer is software or integrated; empty otherwise.</summary>
        public static string Remedies { get; private set; } = "";
        public static GpuPreference Preference { get; private set; } = GpuPreference.Auto;

        private static readonly string[] SoftwareMarkers =
        {
            "llvmpipe", "softpipe", "swrast", "software rasterizer", "software renderer", "gdi generic",
            "microsoft basic render", "swiftshader", "mesa offscreen", "osmesa", "lavapipe", "apple software",
        };
        private static readonly string[] IntegratedMarkers = { "intel(r) hd", "intel(r) uhd", "intel(r) iris", "intel hd", "intel uhd", "iris xe", "iris(r) xe", "radeon graphics", "radeon(tm) graphics", "vega 8", "vega 11", "vega 3", "radeon 680m", "radeon 780m" };

        /// <summary>
        /// Call <b>before</b> GLFW is initialised: applies the GPU preference to the native process environment so
        /// the GL library picks it up when it loads (Linux: Mesa <c>DRI_PRIME</c>, NVIDIA PRIME render offload).
        /// </summary>
        public static void ApplyPreference(GpuPreference pref)
        {
            Preference = pref;
            if (pref != GpuPreference.High) return;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // .NET keeps its own copy of the environment on Unix; the GL library reads the native one → setenv
                NativeSetEnv("DRI_PRIME", "1");
                NativeSetEnv("__NV_PRIME_RENDER_OFFLOAD", "1");
                NativeSetEnv("__GLX_VENDOR_LIBRARY_NAME", "nvidia", overwrite: false);
                NativeSetEnv("__VK_LAYER_NV_optimus", "NVIDIA_only", overwrite: false);
            }
            // Windows: the discrete GPU is a per-application OS choice (Settings → Display → Graphics) or a driver
            // control-panel profile; a managed executable cannot export the NvOptimusEnablement symbol.
        }

        private static unsafe void NativeSetEnv(string name, string value, bool overwrite = true)
        {
            try
            {
                if (!overwrite && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) return;
                if (!NativeLibrary.TryLoad("libc.so.6", out var libc) && !NativeLibrary.TryLoad("libc", out libc)) return;
                var fn = (delegate* unmanaged<byte*, byte*, int, int>)NativeLibrary.GetExport(libc, "setenv");
                byte[] n = System.Text.Encoding.UTF8.GetBytes(name + "\0"), v = System.Text.Encoding.UTF8.GetBytes(value + "\0");
                fixed (byte* pn = n) fixed (byte* pv = v) fn(pn, pv, 1);
                Environment.SetEnvironmentVariable(name, value);
                Debug.LogWarning($"gpu: {name}={value}");
            }
            catch (Exception e) { Debug.LogWarning($"gpu: could not set {name}: {e.Message}"); }
        }

        /// <summary>Call once the GL context is current: reads the strings and classifies the renderer.</summary>
        public static void Query()
        {
            Vendor = GL.GetString(GL.VENDOR) ?? "";
            Renderer = GL.GetString(GL.RENDERER) ?? "";
            Version = GL.GetString(GL.VERSION) ?? "";
            GlslVersion = GL.GetString(GL.SHADING_LANGUAGE_VERSION) ?? "";
            string r = (Vendor + " " + Renderer).ToLowerInvariant();
            IsSoftware = false;
            foreach (var m in SoftwareMarkers) if (r.Contains(m)) { IsSoftware = true; break; }
            IsIntegrated = false;
            if (!IsSoftware) foreach (var m in IntegratedMarkers) if (r.Contains(m)) { IsIntegrated = true; break; }

            Summary = $"{Renderer} ({Vendor}) — OpenGL {Version}, GLSL {GlslVersion}" + (IsSoftware ? "  [SOFTWARE RASTERIZER: RENDERING ON THE CPU]" : IsIntegrated ? "  [integrated GPU]" : "");
            Remedies = BuildRemedies();
        }

        private static string BuildRemedies()
        {
            if (!IsSoftware && !IsIntegrated) return "";
            var sb = new System.Text.StringBuilder();
            if (IsSoftware)
            {
                sb.AppendLine("The OpenGL context is a CPU software rasterizer — the GPU is not used at all. Likely causes and fixes:");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string always = Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE");
                    if (!string.IsNullOrEmpty(always)) sb.AppendLine($"  * LIBGL_ALWAYS_SOFTWARE={always} is set in the environment: unset it.");
                    string display = Environment.GetEnvironmentVariable("DISPLAY");
                    if (display == ":1") sb.AppendLine("  * DISPLAY=:1 — tools/env.sh defaults to the headless Xvfb display when DISPLAY is unset; run from a desktop session (DISPLAY=:0) or export DISPLAY before run.sh.");
                    sb.AppendLine("  * No usable DRI driver for this GPU (check `glxinfo -B`; install mesa-vulkan/DRI drivers or the vendor driver; on NVIDIA make sure the nvidia GLX library is the active vendor).");
                    sb.AppendLine("  * Remote / virtual display (SSH X forwarding, VNC, a VM without 3D acceleration) always gives llvmpipe.");
                    sb.AppendLine("  * Hybrid laptop with the discrete GPU powered off: run with --gpu high (sets DRI_PRIME / PRIME render offload) or prefix `DRI_PRIME=1`.");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    sb.AppendLine("  * 'GDI Generic' / 'Microsoft Basic Render Driver' means no OpenGL ICD was found: install the GPU vendor's driver (Windows Update's basic driver has no OpenGL).");
                    sb.AppendLine("  * Remote Desktop sessions have no hardware OpenGL; run locally.");
                    sb.AppendLine("  * Hybrid laptop: Settings → System → Display → Graphics → add RipNRage.exe / dotnet.exe → 'High performance' (or the NVIDIA/AMD control panel per-app profile).");
                }
                else sb.AppendLine("  * macOS fell back to the software renderer: the requested 3.3 core profile is not hardware accelerated on this machine, or the app runs under Rosetta without GPU access.");
            }
            else if (IsIntegrated)
            {
                sb.AppendLine("Rendering on an integrated GPU. If this machine also has a discrete GPU:");
                sb.AppendLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "  * Settings → System → Display → Graphics → add the executable → 'High performance' (or the vendor control panel)."
                    : "  * run with --gpu high (DRI_PRIME=1 / NVIDIA PRIME render offload).");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
