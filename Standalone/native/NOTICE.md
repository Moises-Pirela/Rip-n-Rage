Third-party native libraries bundled for convenience (Linux x64 builds taken from Ubuntu 24.04 packages):

* `libglfw.so.3` — GLFW 3.3.10, zlib/libpng licence (https://www.glfw.org/license.html)
* `libopenal.so.1` — OpenAL Soft 1.23.1, LGPL-2.0 (https://github.com/kcat/openal-soft); loaded dynamically, replaceable.

On other platforms the same libraries are located through the system loader (see README).
