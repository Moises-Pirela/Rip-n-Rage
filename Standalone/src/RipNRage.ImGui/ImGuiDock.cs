using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace RipNRage.ImGuiIntegration
{
    /// <summary>
    /// Dear ImGui's DockBuilder API. cimgui exports it, but ImGui.NET does not wrap it (it lives in imgui_internal.h),
    /// so the few entry points needed to lay panels out the first time an editor runs are bound here by hand.
    /// Signatures follow cimgui's generated C API for the version ImGui.NET ships (see the package's cimgui build).
    /// </summary>
    public static class ImGuiDock
    {
        private const string Lib = "cimgui";

        [DllImport(Lib, EntryPoint = "igDockBuilderAddNode", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint AddNode(uint nodeId, ImGuiDockNodeFlags flags);

        [DllImport(Lib, EntryPoint = "igDockBuilderRemoveNode", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RemoveNode(uint nodeId);

        [DllImport(Lib, EntryPoint = "igDockBuilderSetNodeSize", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetNodeSize(uint nodeId, Vec2 size);

        [DllImport(Lib, EntryPoint = "igDockBuilderSetNodePos", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetNodePos(uint nodeId, Vec2 pos);

        [DllImport(Lib, EntryPoint = "igDockBuilderSplitNode", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint SplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatio, out uint outIdAtDir, out uint outIdAtOppositeDir);

        [DllImport(Lib, EntryPoint = "igDockBuilderDockWindow", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DockWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string windowName, uint nodeId);

        [DllImport(Lib, EntryPoint = "igDockBuilderFinish", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Finish(uint nodeId);

        /// <summary>
        /// Splits <paramref name="dockspaceId"/> into a left column, a right column and an optional bottom strip,
        /// leaving the centre for the viewport panel. Call once, when no saved layout exists.
        /// </summary>
        public static void BuildDefaultLayout(uint dockspaceId, Vec2 size, string leftWindow, string rightWindow,
                                              float leftRatio = 0.2f, float rightRatio = 0.26f,
                                              string bottomWindow = null, float bottomRatio = 0.25f)
        {
            RemoveNode(dockspaceId);
            AddNode(dockspaceId, (ImGuiDockNodeFlags)(1 << 10)); // ImGuiDockNodeFlags_DockSpace (internal)
            SetNodePos(dockspaceId, Vec2.Zero);
            SetNodeSize(dockspaceId, size);

            uint centre = dockspaceId;
            SplitNode(centre, ImGuiDir.Left, leftRatio, out uint left, out centre);
            SplitNode(centre, ImGuiDir.Right, rightRatio, out uint right, out centre);
            if (leftWindow != null) DockWindow(leftWindow, left);
            if (rightWindow != null) DockWindow(rightWindow, right);
            if (bottomWindow != null)
            {
                SplitNode(centre, ImGuiDir.Down, bottomRatio, out uint bottom, out centre);
                DockWindow(bottomWindow, bottom);
            }
            DockWindow("Viewport", centre);
            Finish(dockspaceId);
        }
    }
}
