using System;
using System.Runtime.InteropServices;

// Raw binding for Clay v0.14 (https://github.com/nicbarker/clay, vendored in native/src/clay). Mirrors clay.h 1:1:
// every struct is blittable and laid out exactly like the C one (C bool / packed enums are bytes), so they are passed
// by value and by pointer without marshalling. ClayNative.VerifyLayout() checks the sizes against the native build.
// Structured after Orcolom/clay-cs (which targets v0.13), rewritten for v0.14 and the engine's NativeLoader.
namespace RipNRage.Engine.UI.Clay
{
    // ---- enums (CLAY_PACKED_ENUM = uint8_t)
    public enum Clay_LayoutDirection : byte { LeftToRight, TopToBottom }
    public enum Clay_LayoutAlignmentX : byte { Left, Right, Center }
    public enum Clay_LayoutAlignmentY : byte { Top, Bottom, Center }
    public enum Clay_SizingType : byte { Fit, Grow, Percent, Fixed }
    public enum Clay_TextWrapMode : byte { Words, Newlines, None }
    public enum Clay_TextAlignment : byte { Left, Center, Right }
    public enum Clay_FloatingAttachPointType : byte { LeftTop, LeftCenter, LeftBottom, CenterTop, CenterCenter, CenterBottom, RightTop, RightCenter, RightBottom }
    public enum Clay_PointerCaptureMode : byte { Capture, Passthrough }
    public enum Clay_FloatingAttachToElement : byte { None, Parent, ElementWithId, Root }
    public enum Clay_FloatingClipToElement : byte { None, AttachedParent }
    public enum Clay_RenderCommandType : byte { None, Rectangle, Border, Text, Image, ScissorStart, ScissorEnd, Custom }
    public enum Clay_PointerDataInteractionState : byte { PressedThisFrame, Pressed, ReleasedThisFrame, Released }
    public enum Clay_ErrorType : byte { TextMeasurementFunctionNotProvided, ArenaCapacityExceeded, ElementsCapacityExceeded, TextMeasurementCapacityExceeded, DuplicateId, FloatingContainerParentNotFound, PercentageOver1, InternalError }

    // ---- basic structs
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_String { public byte isStaticallyAllocated; public int length; public byte* chars; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_StringSlice { public int length; public byte* chars; public byte* baseChars; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_Arena { public nuint nextAllocation; public nuint capacity; public byte* memory; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_Dimensions { public float width, height; public Clay_Dimensions(float w, float h) { width = w; height = h; } }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_Vector2 { public float x, y; public Clay_Vector2(float x, float y) { this.x = x; this.y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_Color { public float r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_BoundingBox { public float x, y, width, height; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_ElementId { public uint id, offset, baseId; public Clay_String stringId; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ElementIdArray { public int capacity, length; public Clay_ElementId* internalArray; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_CornerRadius { public float topLeft, topRight, bottomLeft, bottomRight; }

    // ---- layout
    [StructLayout(LayoutKind.Sequential)] public struct Clay_ChildAlignment { public Clay_LayoutAlignmentX x; public Clay_LayoutAlignmentY y; }
    /// <summary>union { Clay_SizingMinMax minMax; float percent; } size; type — percent shares the slot of min.</summary>
    [StructLayout(LayoutKind.Sequential)] public struct Clay_SizingAxis { public float min, max; public Clay_SizingType type; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_Sizing { public Clay_SizingAxis width, height; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_Padding { public ushort left, right, top, bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Clay_LayoutConfig
    {
        public Clay_Sizing sizing;
        public Clay_Padding padding;
        public ushort childGap;
        public Clay_ChildAlignment childAlignment;
        public Clay_LayoutDirection layoutDirection;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Clay_TextElementConfig
    {
        public void* userData;
        public Clay_Color textColor;
        public ushort fontId, fontSize, letterSpacing, lineHeight;
        public Clay_TextWrapMode wrapMode;
        public Clay_TextAlignment textAlignment;
    }

    [StructLayout(LayoutKind.Sequential)] public struct Clay_AspectRatioElementConfig { public float aspectRatio; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ImageElementConfig { public void* imageData; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_FloatingAttachPoints { public Clay_FloatingAttachPointType element, parent; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Clay_FloatingElementConfig
    {
        public Clay_Vector2 offset;
        public Clay_Dimensions expand;
        public uint parentId;
        public short zIndex;
        public Clay_FloatingAttachPoints attachPoints;
        public Clay_PointerCaptureMode pointerCaptureMode;
        public Clay_FloatingAttachToElement attachTo;
        public Clay_FloatingClipToElement clipTo;
    }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_CustomElementConfig { public void* customData; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_ClipElementConfig { public byte horizontal, vertical; public Clay_Vector2 childOffset; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_BorderWidth { public ushort left, right, top, bottom, betweenChildren; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_BorderElementConfig { public Clay_Color color; public Clay_BorderWidth width; }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Clay_ElementDeclaration
    {
        public Clay_ElementId id;
        public Clay_LayoutConfig layout;
        public Clay_Color backgroundColor;
        public Clay_CornerRadius cornerRadius;
        public Clay_AspectRatioElementConfig aspectRatio;
        public Clay_ImageElementConfig image;
        public Clay_FloatingElementConfig floating;
        public Clay_CustomElementConfig custom;
        public Clay_ClipElementConfig clip;
        public Clay_BorderElementConfig border;
        public void* userData;
    }

    // ---- render output
    [StructLayout(LayoutKind.Sequential)] public struct Clay_TextRenderData { public Clay_StringSlice stringContents; public Clay_Color textColor; public ushort fontId, fontSize, letterSpacing, lineHeight; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_RectangleRenderData { public Clay_Color backgroundColor; public Clay_CornerRadius cornerRadius; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ImageRenderData { public Clay_Color backgroundColor; public Clay_CornerRadius cornerRadius; public void* imageData; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_CustomRenderData { public Clay_Color backgroundColor; public Clay_CornerRadius cornerRadius; public void* customData; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_ClipRenderData { public byte horizontal, vertical; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_BorderRenderData { public Clay_Color color; public Clay_CornerRadius cornerRadius; public Clay_BorderWidth width; }

    [StructLayout(LayoutKind.Explicit)]
    public struct Clay_RenderData
    {
        [FieldOffset(0)] public Clay_RectangleRenderData rectangle;
        [FieldOffset(0)] public Clay_TextRenderData text;
        [FieldOffset(0)] public Clay_ImageRenderData image;
        [FieldOffset(0)] public Clay_CustomRenderData custom;
        [FieldOffset(0)] public Clay_BorderRenderData border;
        [FieldOffset(0)] public Clay_ClipRenderData clip;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Clay_RenderCommand
    {
        public Clay_BoundingBox boundingBox;
        public Clay_RenderData renderData;
        public void* userData;
        public uint id;
        public short zIndex;
        public Clay_RenderCommandType commandType;
    }

    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_RenderCommandArray { public int capacity, length; public Clay_RenderCommand* internalArray; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ScrollContainerData { public Clay_Vector2* scrollPosition; public Clay_Dimensions scrollContainerDimensions, contentDimensions; public Clay_ClipElementConfig config; public byte found; }
    [StructLayout(LayoutKind.Sequential)] public struct Clay_ElementData { public Clay_BoundingBox boundingBox; public byte found; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ErrorData { public Clay_ErrorType errorType; public Clay_String errorText; public void* userData; }
    [StructLayout(LayoutKind.Sequential)] public unsafe struct Clay_ErrorHandler { public delegate* unmanaged[Cdecl]<Clay_ErrorData, void> errorHandlerFunction; public void* userData; }

    /// <summary>P/Invoke entry points of the native clay library (libclay.dylib / libclay.so / clay.dll).</summary>
    public static unsafe class ClayNative
    {
        private const string Lib = Platform.NativeLoader.ClayLib;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint Clay_MinMemorySize();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_Arena Clay_CreateArenaWithCapacityAndMemory(nuint capacity, void* memory);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetPointerState(Clay_Vector2 position, byte pointerDown);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr Clay_Initialize(Clay_Arena arena, Clay_Dimensions layoutDimensions, Clay_ErrorHandler errorHandler);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr Clay_GetCurrentContext();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetCurrentContext(IntPtr context);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_UpdateScrollContainers(byte enableDragScrolling, Clay_Vector2 scrollDelta, float deltaTime);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetLayoutDimensions(Clay_Dimensions dimensions);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_BeginLayout();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_RenderCommandArray Clay_EndLayout();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_ElementData Clay_GetElementData(Clay_ElementId id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte Clay_Hovered();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte Clay_PointerOver(Clay_ElementId elementId);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_ScrollContainerData Clay_GetScrollContainerData(Clay_ElementId id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetMeasureTextFunction(delegate* unmanaged[Cdecl]<Clay_StringSlice, Clay_TextElementConfig*, void*, Clay_Dimensions> fn, void* userData);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetDebugModeEnabled(byte enabled);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte Clay_IsDebugModeEnabled();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetCullingEnabled(byte enabled);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int Clay_GetMaxElementCount();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetMaxElementCount(int maxElementCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_SetMaxMeasureTextCacheWordCount(int count);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay_ResetMeasureTextCache();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay__OpenElement();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay__ConfigureOpenElementPtr(Clay_ElementDeclaration* config);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay__CloseElement();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_ElementId Clay__HashString(Clay_String key, uint offset, uint seed);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void Clay__OpenTextElement(Clay_String text, Clay_TextElementConfig* textConfig);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Clay_TextElementConfig* Clay__StoreTextElementConfig(Clay_TextElementConfig config);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint Clay__GetParentElementId();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int RnR_ClayLayoutSignature(int which);

        /// <summary>Throws when the managed structs don't match the loaded native library (wrong clay.h version / stale build).</summary>
        public static void VerifyLayout()
        {
            (int which, int managed, string what)[] checks =
            {
                (0, sizeof(Clay_ElementDeclaration), "sizeof(Clay_ElementDeclaration)"),
                (1, sizeof(Clay_RenderCommand), "sizeof(Clay_RenderCommand)"),
                (2, sizeof(Clay_TextElementConfig), "sizeof(Clay_TextElementConfig)"),
                (3, (int)Marshal.OffsetOf<Clay_ElementDeclaration>(nameof(Clay_ElementDeclaration.floating)), "offsetof(floating)"),
                (4, (int)Marshal.OffsetOf<Clay_ElementDeclaration>(nameof(Clay_ElementDeclaration.border)), "offsetof(border)"),
                (5, (int)Marshal.OffsetOf<Clay_RenderCommand>(nameof(Clay_RenderCommand.commandType)), "offsetof(commandType)"),
                (6, sizeof(Clay_ElementId), "sizeof(Clay_ElementId)"),
                (7, (int)Marshal.OffsetOf<Clay_ElementDeclaration>(nameof(Clay_ElementDeclaration.clip)), "offsetof(clip)"),
                (8, sizeof(Clay_LayoutConfig), "sizeof(Clay_LayoutConfig)"),
            };
            foreach (var c in checks)
            {
                int native = RnR_ClayLayoutSignature(c.which);
                if (native != c.managed)
                    throw new InvalidOperationException($"Clay binding mismatch: {c.what} is {native} in the native library but {c.managed} in C#. Rebuild it with tools/build-clay.sh.");
            }
        }
    }
}
