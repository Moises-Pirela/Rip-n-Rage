using System;

namespace RipNRage.Engine
{
    /// <summary>Console logging with the UnityEngine.Debug API.</summary>
    public static class Debug
    {
        public static bool Verbose = false;
        public static Action<string> LogSink;

        public static void Log(object message) { if (Verbose) Write("[Log] " + message); }
        public static void LogWarning(object message) => Write("[Warn] " + message);
        public static void LogError(object message) => Write("[Error] " + message);
        public static void LogErrorFormat(string format, params object[] args) => Write("[Error] " + string.Format(format, args));
        public static void LogFormat(string format, params object[] args) { if (Verbose) Write("[Log] " + string.Format(format, args)); }
        public static void LogException(Exception e) => Write("[Exception] " + e);

        /// <summary>Debug rays are collected and drawn by the renderer as lines (only when Verbose).</summary>
        public static void DrawRay(Vector3 start, Vector3 dir, Color color) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color) { }

        private static void Write(string s)
        {
            if (LogSink != null) LogSink(s);
            else Console.WriteLine(s);
        }
    }

    /// <summary>Bit mask of layers (UnityEngine.LayerMask subset).</summary>
    public struct LayerMask
    {
        public int value;
        public LayerMask(int value) { this.value = value; }
        public static implicit operator int(LayerMask m) => m.value;
        public static implicit operator LayerMask(int v) => new LayerMask(v);
        public static int NameToLayer(string name) => Layers.NameToLayer(name);
        public static int GetMask(params string[] names)
        {
            int mask = 0;
            foreach (var n in names) { int l = Layers.NameToLayer(n); if (l >= 0) mask |= 1 << l; }
            return mask;
        }
        public bool Contains(int layer) => (value & (1 << layer)) != 0;
        public static readonly LayerMask Everything = new LayerMask(~0);
    }

    /// <summary>Layer table of the Unity project (ProjectSettings/TagManager.asset).</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int TransparentFX = 1;
        public const int IgnoreRaycast = 2;
        public const int Player = 3;
        public const int Water = 4;
        public const int UI = 5;
        public const int Projectile = 6;
        public const int VisionSensor = 7;
        public const int Wall = 8;
        public const int PostProcessing = 9;
        public const int FPS = 10;
        public const int Walkable = 11;
        public const int Enemies = 12;
        public const int PlayerCamera = 13;
        public const int Pickup = 14;
        public const int OnlyGroundCollision = 15;

        private static readonly string[] Names =
        {
            "Default", "TransparentFX", "Ignore Raycast", "Player", "Water", "UI", "Projectile", "VisionSensor",
            "Wall", "PostProcessing", "FPS", "Walkable", "Enemies", "PlayerCamera", "Pickup", "OnlyGroundCollision",
        };

        public static int NameToLayer(string name) => System.Array.IndexOf(Names, name);
        public static string LayerToName(int layer) => layer >= 0 && layer < Names.Length ? Names[layer] : "";
    }
}
