Third-party native libraries bundled for convenience. They are loaded dynamically and can be replaced with any
compatible build. If a bundled library fails to load, NativeLoader falls back to the system library paths.

| Folder | File | Library | Source |
|---|---|---|---|
| `win-x64/` | `glfw3.dll` | GLFW 3.4 | official release `glfw-3.4.bin.WIN64.zip` (`lib-vc2022`), github.com/glfw/glfw |
| `win-x64/` | `soft_oal.dll` | OpenAL Soft 1.24.3 | official release `openal-soft-1.24.3-bin.zip` (`bin/Win64`), github.com/kcat/openal-soft |
| `linux-x64/` | `libglfw.so.3` | GLFW 3.4 | Debian 13 package `libglfw3_3.4-3_amd64.deb` (needs glibc 2.34+) |
| `linux-x64/` | `libopenal.so.1` | OpenAL Soft 1.24.2 | Debian 13 package `libopenal1_1.24.2-1_amd64.deb` (needs glibc 2.38+) |
| `osx-arm64/` | `libglfw.3.dylib` | GLFW 3.4 | official release `glfw-3.4.bin.MACOS.zip` (`lib-arm64`) |
| `osx-x64/` | `libglfw.3.dylib` | GLFW 3.4 | official release `glfw-3.4.bin.MACOS.zip` (`lib-x86_64`) |

macOS has no bundled OpenAL: the loader uses Homebrew's `openal-soft` if installed, otherwise the system
`OpenAL.framework`.

Licences:

* GLFW: zlib/libpng licence, see `licenses/GLFW-LICENSE.md` (https://www.glfw.org/license.html)
* OpenAL Soft: LGPL-2.0, see `licenses/OpenAL-Soft-COPYING.txt`. Source code: https://github.com/kcat/openal-soft
