// Builds clay.h (vendored, see VERSION) as a shared library for the C# binding in
// src/RipNRage.Engine/UI/Clay. Exported symbols are the plain Clay_* / Clay__* functions.
#if defined(_WIN32)
#define CLAY_DLL           // __declspec(dllexport); __stdcall is the normal x64 convention there
#else
#pragma GCC visibility push(default)
#endif
#define CLAY_IMPLEMENTATION
#include "clay.h"

// Lets the binding verify at start-up that its struct layouts match this build of clay.h.
#include <stddef.h>
#if defined(_WIN32)
__declspec(dllexport)
#endif
int RnR_ClayLayoutSignature(int which)
{
    switch (which)
    {
        case 0: return (int)sizeof(Clay_ElementDeclaration);
        case 1: return (int)sizeof(Clay_RenderCommand);
        case 2: return (int)sizeof(Clay_TextElementConfig);
        case 3: return (int)offsetof(Clay_ElementDeclaration, floating);
        case 4: return (int)offsetof(Clay_ElementDeclaration, border);
        case 5: return (int)offsetof(Clay_RenderCommand, commandType);
        case 6: return (int)sizeof(Clay_ElementId);
        case 7: return (int)offsetof(Clay_ElementDeclaration, clip);
        case 8: return (int)sizeof(Clay_LayoutConfig);
        default: return -1;
    }
}
