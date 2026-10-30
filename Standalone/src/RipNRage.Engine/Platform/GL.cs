using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RipNRage.Engine.Platform
{
    /// <summary>
    /// OpenGL 3.3 core bindings loaded through glfwGetProcAddress using C# unmanaged function pointers.
    /// Only the entry points the renderer uses are bound.
    /// </summary>
    public static unsafe class GL
    {
        // ---- enums ----
        public const uint DEPTH_BUFFER_BIT = 0x00000100;
        public const uint COLOR_BUFFER_BIT = 0x00004000;
        public const uint TRIANGLES = 0x0004;
        public const uint LINES = 0x0001;
        public const uint TRIANGLE_STRIP = 0x0005;
        public const uint UNSIGNED_BYTE = 0x1401;
        public const uint UNSIGNED_SHORT = 0x1403;
        public const uint UNSIGNED_INT = 0x1405;
        public const uint FLOAT = 0x1406;
        public const uint RGBA = 0x1908;
        public const uint RGB = 0x1907;
        public const uint RGBA8 = 0x8058;
        public const uint SRGB8_ALPHA8 = 0x8C43;
        public const uint RED = 0x1903;
        public const uint R8 = 0x8229;
        public const uint TEXTURE_2D = 0x0DE1;
        public const uint TEXTURE0 = 0x84C0;
        public const uint TEXTURE_MIN_FILTER = 0x2801;
        public const uint TEXTURE_MAG_FILTER = 0x2800;
        public const uint TEXTURE_WRAP_S = 0x2802;
        public const uint TEXTURE_WRAP_T = 0x2803;
        public const uint TEXTURE_MAX_ANISOTROPY = 0x84FE;
        public const uint TEXTURE_SWIZZLE_RGBA = 0x8E46;
        public const uint NEAREST = 0x2600;
        public const uint LINEAR = 0x2601;
        public const uint LINEAR_MIPMAP_LINEAR = 0x2703;
        public const uint NEAREST_MIPMAP_LINEAR = 0x2702;
        public const uint CLAMP_TO_EDGE = 0x812F;
        public const uint REPEAT = 0x2901;
        public const uint UNPACK_ALIGNMENT = 0x0CF5;
        public const uint PACK_ALIGNMENT = 0x0D05;
        public const uint ARRAY_BUFFER = 0x8892;
        public const uint ELEMENT_ARRAY_BUFFER = 0x8893;
        public const uint STATIC_DRAW = 0x88E4;
        public const uint DYNAMIC_DRAW = 0x88E8;
        public const uint STREAM_DRAW = 0x88E0;
        public const uint VERTEX_SHADER = 0x8B31;
        public const uint FRAGMENT_SHADER = 0x8B30;
        public const uint COMPILE_STATUS = 0x8B81;
        public const uint LINK_STATUS = 0x8B82;
        public const uint INFO_LOG_LENGTH = 0x8B84;
        public const uint DEPTH_TEST = 0x0B71;
        public const uint BLEND = 0x0BE2;
        public const uint CULL_FACE = 0x0B44;
        public const uint SCISSOR_TEST = 0x0C11;
        public const uint MULTISAMPLE = 0x809D;
        public const uint FRAMEBUFFER_SRGB = 0x8DB9;
        public const uint SRC_ALPHA = 0x0302;
        public const uint ONE_MINUS_SRC_ALPHA = 0x0303;
        public const uint ONE = 1;
        public const uint DST_COLOR = 0x0306;
        public const uint ZERO = 0;
        public const uint LESS = 0x0201;
        public const uint LEQUAL = 0x0203;
        public const uint ALWAYS = 0x0207;
        public const uint BACK = 0x0405;
        public const uint FRONT = 0x0404;
        public const uint CW = 0x0900;
        public const uint CCW = 0x0901;
        public const uint VERSION = 0x1F02;
        public const uint RENDERER = 0x1F01;
        public const uint VENDOR = 0x1F00;
        public const uint MAX_TEXTURE_SIZE = 0x0D33;
        public const uint NO_ERROR = 0;
        public const uint FUNC_ADD = 0x8006;
        // framebuffers / HDR
        public const uint FRAMEBUFFER = 0x8D40;
        public const uint READ_FRAMEBUFFER = 0x8CA8;
        public const uint DRAW_FRAMEBUFFER = 0x8CA9;
        public const uint COLOR_ATTACHMENT0 = 0x8CE0;
        public const uint DEPTH_ATTACHMENT = 0x8D00;
        public const uint FRAMEBUFFER_COMPLETE = 0x8CD5;
        public const uint DEPTH_COMPONENT = 0x1902;
        public const uint DEPTH_COMPONENT24 = 0x81A6;
        public const uint RGBA16F = 0x881A;
        public const uint HALF_FLOAT = 0x140B;
        public const uint TEXTURE1 = 0x84C1;
        public const uint TEXTURE2 = 0x84C2;
        public const uint TEXTURE3 = 0x84C3;
        public const uint NONE = 0;
        public const uint POLYGON_OFFSET_FILL = 0x8037;
        public const uint TIME_ELAPSED = 0x88BF;
        public const uint SAMPLES_PASSED = 0x8914;
        public const uint ANY_SAMPLES_PASSED = 0x8C2F;
        public const uint SHADING_LANGUAGE_VERSION = 0x8B8C;
        public const uint TIMESTAMP = 0x8E28;
        public const uint QUERY_RESULT = 0x8866;
        public const uint QUERY_RESULT_AVAILABLE = 0x8867;
        public const uint TEXTURE_COMPARE_MODE = 0x884C;
        public const uint TEXTURE_COMPARE_FUNC = 0x884D;
        public const uint COMPARE_REF_TO_TEXTURE = 0x884E;

        // ---- function pointers ----
        private static delegate* unmanaged<uint, void> _clear;
        private static delegate* unmanaged<float, float, float, float, void> _clearColor;
        private static delegate* unmanaged<int, int, int, int, void> _viewport;
        private static delegate* unmanaged<uint, void> _enable;
        private static delegate* unmanaged<uint, void> _disable;
        private static delegate* unmanaged<uint, uint, void> _blendFunc;
        private static delegate* unmanaged<uint, void> _depthFunc;
        private static delegate* unmanaged<byte, void> _depthMask;
        private static delegate* unmanaged<uint, void> _cullFace;
        private static delegate* unmanaged<uint, void> _frontFace;
        private static delegate* unmanaged<uint, byte*> _getString;
        private static delegate* unmanaged<uint, int*, void> _getIntegerv;
        private static delegate* unmanaged<uint> _getError;
        private static delegate* unmanaged<int, uint*, void> _genTextures;
        private static delegate* unmanaged<int, uint*, void> _deleteTextures;
        private static delegate* unmanaged<uint, uint, void> _bindTexture;
        private static delegate* unmanaged<uint, void> _activeTexture;
        private static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> _texImage2D;
        private static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> _texSubImage2D;
        private static delegate* unmanaged<uint, uint, int, void> _texParameteri;
        private static delegate* unmanaged<uint, uint, int*, void> _texParameteriv;
        private static delegate* unmanaged<uint, uint, float, void> _texParameterf;
        private static delegate* unmanaged<uint, void> _generateMipmap;
        private static delegate* unmanaged<uint, int, void> _pixelStorei;
        private static delegate* unmanaged<int, uint*, void> _genBuffers;
        private static delegate* unmanaged<int, uint*, void> _deleteBuffers;
        private static delegate* unmanaged<uint, uint, void> _bindBuffer;
        private static delegate* unmanaged<uint, nint, void*, uint, void> _bufferData;
        private static delegate* unmanaged<uint, nint, nint, void*, void> _bufferSubData;
        private static delegate* unmanaged<int, uint*, void> _genVertexArrays;
        private static delegate* unmanaged<int, uint*, void> _deleteVertexArrays;
        private static delegate* unmanaged<uint, void> _bindVertexArray;
        private static delegate* unmanaged<uint, void> _enableVertexAttribArray;
        private static delegate* unmanaged<uint, int, uint, byte, int, void*, void> _vertexAttribPointer;
        private static delegate* unmanaged<uint, int, int, void> _drawArrays;
        private static delegate* unmanaged<uint, int, uint, void*, void> _drawElements;
        private static delegate* unmanaged<uint, uint> _createShader;
        private static delegate* unmanaged<uint, int, byte**, int*, void> _shaderSource;
        private static delegate* unmanaged<uint, void> _compileShader;
        private static delegate* unmanaged<uint, uint, int*, void> _getShaderiv;
        private static delegate* unmanaged<uint, int, int*, byte*, void> _getShaderInfoLog;
        private static delegate* unmanaged<uint, void> _deleteShader;
        private static delegate* unmanaged<uint> _createProgram;
        private static delegate* unmanaged<uint, uint, void> _attachShader;
        private static delegate* unmanaged<uint, void> _linkProgram;
        private static delegate* unmanaged<uint, uint, int*, void> _getProgramiv;
        private static delegate* unmanaged<uint, int, int*, byte*, void> _getProgramInfoLog;
        private static delegate* unmanaged<uint, void> _useProgram;
        private static delegate* unmanaged<uint, void> _deleteProgram;
        private static delegate* unmanaged<uint, byte*, int> _getUniformLocation;
        private static delegate* unmanaged<int, int, void> _uniform1i;
        private static delegate* unmanaged<int, float, void> _uniform1f;
        private static delegate* unmanaged<int, float, float, void> _uniform2f;
        private static delegate* unmanaged<int, float, float, float, void> _uniform3f;
        private static delegate* unmanaged<int, float, float, float, float, void> _uniform4f;
        private static delegate* unmanaged<int, int, byte, float*, void> _uniformMatrix4fv;
        private static delegate* unmanaged<int, int, int, int, uint, uint, void*, void> _readPixels;
        private static delegate* unmanaged<int, int, int, int, void> _scissor;
        private static delegate* unmanaged<float, void> _lineWidth;
        private static delegate* unmanaged<void> _finish;
        private static delegate* unmanaged<void> _flush;
        private static delegate* unmanaged<byte, byte, byte, byte, void> _colorMask;
        private static delegate* unmanaged<int, uint*, void> _genQueries;
        private static delegate* unmanaged<int, uint*, void> _deleteQueries;
        private static delegate* unmanaged<uint, uint, void> _beginQuery;
        private static delegate* unmanaged<uint, void> _endQuery;
        private static delegate* unmanaged<uint, uint, void> _queryCounter;
        private static delegate* unmanaged<uint, uint, int*, void> _getQueryObjectiv;
        private static delegate* unmanaged<uint, uint, ulong*, void> _getQueryObjectui64v;
        private static delegate* unmanaged<int, uint*, void> _genFramebuffers;
        private static delegate* unmanaged<int, uint*, void> _deleteFramebuffers;
        private static delegate* unmanaged<uint, uint, void> _bindFramebuffer;
        private static delegate* unmanaged<uint, uint, uint, uint, int, void> _framebufferTexture2D;
        private static delegate* unmanaged<uint, uint> _checkFramebufferStatus;
        private static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> _blitFramebuffer;
        private static delegate* unmanaged<uint, void> _drawBuffer;
        private static delegate* unmanaged<uint, void> _readBuffer;
        private static delegate* unmanaged<int, int, float*, void> _uniform3fv;
        private static delegate* unmanaged<int, int, float*, void> _uniform1fv;
        private static delegate* unmanaged<float, float, void> _polygonOffset;

        private static T Load<T>(Func<string, IntPtr> getProc, string name, bool optional = false) where T : unmanaged
        {
            IntPtr p = getProc(name);
            if (p == IntPtr.Zero && !optional) throw new InvalidOperationException("Missing OpenGL entry point: " + name);
            return Unsafe.As<IntPtr, T>(ref p);
        }

        private static class Unsafe
        {
            public static TTo As<TFrom, TTo>(ref TFrom source) where TFrom : unmanaged where TTo : unmanaged
            {
                fixed (TFrom* p = &source) return *(TTo*)p;
            }
        }

        public static void LoadFunctions(Func<string, IntPtr> getProc)
        {
            _clear = (delegate* unmanaged<uint, void>)getProc("glClear");
            _clearColor = (delegate* unmanaged<float, float, float, float, void>)getProc("glClearColor");
            _viewport = (delegate* unmanaged<int, int, int, int, void>)getProc("glViewport");
            _enable = (delegate* unmanaged<uint, void>)getProc("glEnable");
            _disable = (delegate* unmanaged<uint, void>)getProc("glDisable");
            _blendFunc = (delegate* unmanaged<uint, uint, void>)getProc("glBlendFunc");
            _depthFunc = (delegate* unmanaged<uint, void>)getProc("glDepthFunc");
            _depthMask = (delegate* unmanaged<byte, void>)getProc("glDepthMask");
            _cullFace = (delegate* unmanaged<uint, void>)getProc("glCullFace");
            _frontFace = (delegate* unmanaged<uint, void>)getProc("glFrontFace");
            _getString = (delegate* unmanaged<uint, byte*>)getProc("glGetString");
            _getIntegerv = (delegate* unmanaged<uint, int*, void>)getProc("glGetIntegerv");
            _getError = (delegate* unmanaged<uint>)getProc("glGetError");
            _genTextures = (delegate* unmanaged<int, uint*, void>)getProc("glGenTextures");
            _deleteTextures = (delegate* unmanaged<int, uint*, void>)getProc("glDeleteTextures");
            _bindTexture = (delegate* unmanaged<uint, uint, void>)getProc("glBindTexture");
            _activeTexture = (delegate* unmanaged<uint, void>)getProc("glActiveTexture");
            _texImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)getProc("glTexImage2D");
            _texSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)getProc("glTexSubImage2D");
            _texParameteri = (delegate* unmanaged<uint, uint, int, void>)getProc("glTexParameteri");
            _texParameteriv = (delegate* unmanaged<uint, uint, int*, void>)getProc("glTexParameteriv");
            _texParameterf = (delegate* unmanaged<uint, uint, float, void>)getProc("glTexParameterf");
            _generateMipmap = (delegate* unmanaged<uint, void>)getProc("glGenerateMipmap");
            _pixelStorei = (delegate* unmanaged<uint, int, void>)getProc("glPixelStorei");
            _genBuffers = (delegate* unmanaged<int, uint*, void>)getProc("glGenBuffers");
            _deleteBuffers = (delegate* unmanaged<int, uint*, void>)getProc("glDeleteBuffers");
            _bindBuffer = (delegate* unmanaged<uint, uint, void>)getProc("glBindBuffer");
            _bufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)getProc("glBufferData");
            _bufferSubData = (delegate* unmanaged<uint, nint, nint, void*, void>)getProc("glBufferSubData");
            _genVertexArrays = (delegate* unmanaged<int, uint*, void>)getProc("glGenVertexArrays");
            _deleteVertexArrays = (delegate* unmanaged<int, uint*, void>)getProc("glDeleteVertexArrays");
            _bindVertexArray = (delegate* unmanaged<uint, void>)getProc("glBindVertexArray");
            _enableVertexAttribArray = (delegate* unmanaged<uint, void>)getProc("glEnableVertexAttribArray");
            _vertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)getProc("glVertexAttribPointer");
            _drawArrays = (delegate* unmanaged<uint, int, int, void>)getProc("glDrawArrays");
            _drawElements = (delegate* unmanaged<uint, int, uint, void*, void>)getProc("glDrawElements");
            _createShader = (delegate* unmanaged<uint, uint>)getProc("glCreateShader");
            _shaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)getProc("glShaderSource");
            _compileShader = (delegate* unmanaged<uint, void>)getProc("glCompileShader");
            _getShaderiv = (delegate* unmanaged<uint, uint, int*, void>)getProc("glGetShaderiv");
            _getShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)getProc("glGetShaderInfoLog");
            _deleteShader = (delegate* unmanaged<uint, void>)getProc("glDeleteShader");
            _createProgram = (delegate* unmanaged<uint>)getProc("glCreateProgram");
            _attachShader = (delegate* unmanaged<uint, uint, void>)getProc("glAttachShader");
            _linkProgram = (delegate* unmanaged<uint, void>)getProc("glLinkProgram");
            _getProgramiv = (delegate* unmanaged<uint, uint, int*, void>)getProc("glGetProgramiv");
            _getProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)getProc("glGetProgramInfoLog");
            _useProgram = (delegate* unmanaged<uint, void>)getProc("glUseProgram");
            _deleteProgram = (delegate* unmanaged<uint, void>)getProc("glDeleteProgram");
            _getUniformLocation = (delegate* unmanaged<uint, byte*, int>)getProc("glGetUniformLocation");
            _uniform1i = (delegate* unmanaged<int, int, void>)getProc("glUniform1i");
            _uniform1f = (delegate* unmanaged<int, float, void>)getProc("glUniform1f");
            _uniform2f = (delegate* unmanaged<int, float, float, void>)getProc("glUniform2f");
            _uniform3f = (delegate* unmanaged<int, float, float, float, void>)getProc("glUniform3f");
            _uniform4f = (delegate* unmanaged<int, float, float, float, float, void>)getProc("glUniform4f");
            _uniformMatrix4fv = (delegate* unmanaged<int, int, byte, float*, void>)getProc("glUniformMatrix4fv");
            _readPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)getProc("glReadPixels");
            _scissor = (delegate* unmanaged<int, int, int, int, void>)getProc("glScissor");
            _lineWidth = (delegate* unmanaged<float, void>)getProc("glLineWidth");
            _finish = (delegate* unmanaged<void>)getProc("glFinish");
            _flush = (delegate* unmanaged<void>)getProc("glFlush");
            _colorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)getProc("glColorMask");
            // timer queries (core since 3.3; optional — the profiler disables GPU timing when missing)
            _genQueries = (delegate* unmanaged<int, uint*, void>)getProc("glGenQueries");
            _deleteQueries = (delegate* unmanaged<int, uint*, void>)getProc("glDeleteQueries");
            _beginQuery = (delegate* unmanaged<uint, uint, void>)getProc("glBeginQuery");
            _endQuery = (delegate* unmanaged<uint, void>)getProc("glEndQuery");
            _queryCounter = (delegate* unmanaged<uint, uint, void>)getProc("glQueryCounter");
            _getQueryObjectiv = (delegate* unmanaged<uint, uint, int*, void>)getProc("glGetQueryObjectiv");
            _getQueryObjectui64v = (delegate* unmanaged<uint, uint, ulong*, void>)getProc("glGetQueryObjectui64v");
            _genFramebuffers = (delegate* unmanaged<int, uint*, void>)getProc("glGenFramebuffers");
            _deleteFramebuffers = (delegate* unmanaged<int, uint*, void>)getProc("glDeleteFramebuffers");
            _bindFramebuffer = (delegate* unmanaged<uint, uint, void>)getProc("glBindFramebuffer");
            _framebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)getProc("glFramebufferTexture2D");
            _checkFramebufferStatus = (delegate* unmanaged<uint, uint>)getProc("glCheckFramebufferStatus");
            _blitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)getProc("glBlitFramebuffer");
            _drawBuffer = (delegate* unmanaged<uint, void>)getProc("glDrawBuffer");
            _readBuffer = (delegate* unmanaged<uint, void>)getProc("glReadBuffer");
            _uniform3fv = (delegate* unmanaged<int, int, float*, void>)getProc("glUniform3fv");
            _uniform1fv = (delegate* unmanaged<int, int, float*, void>)getProc("glUniform1fv");
            _polygonOffset = (delegate* unmanaged<float, float, void>)getProc("glPolygonOffset");

            if (_clear == null || _createShader == null || _genVertexArrays == null)
                throw new InvalidOperationException("Failed to load core OpenGL 3.3 functions. Is a 3.3+ core context current?");
        }

        // ---- wrappers ----
        public static void Clear(uint mask) => _clear(mask);
        public static void ClearColor(float r, float g, float b, float a) => _clearColor(r, g, b, a);
        public static void Viewport(int x, int y, int w, int h) => _viewport(x, y, w, h);
        public static void Enable(uint cap) => _enable(cap);
        public static void Disable(uint cap) => _disable(cap);
        public static void BlendFunc(uint src, uint dst) => _blendFunc(src, dst);
        public static void DepthFunc(uint f) => _depthFunc(f);
        public static void DepthMask(bool flag) => _depthMask(flag ? (byte)1 : (byte)0);
        public static void CullFace(uint mode) => _cullFace(mode);
        public static void FrontFace(uint mode) => _frontFace(mode);
        public static string GetString(uint name) { byte* p = _getString(name); return p == null ? "" : Marshal.PtrToStringAnsi((IntPtr)p); }
        public static int GetInteger(uint pname) { int v; _getIntegerv(pname, &v); return v; }
        public static uint GetError() => _getError();
        public static uint GenTexture() { uint t; _genTextures(1, &t); return t; }
        public static void DeleteTexture(uint t) => _deleteTextures(1, &t);
        public static void BindTexture(uint target, uint t) => _bindTexture(target, t);
        public static void ActiveTexture(uint unit) => _activeTexture(unit);
        public static void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, void* data) =>
            _texImage2D(target, level, internalFormat, w, h, border, format, type, data);
        public static void TexSubImage2D(uint target, int level, int x, int y, int w, int h, uint format, uint type, void* data) =>
            _texSubImage2D(target, level, x, y, w, h, format, type, data);
        public static void TexParameteri(uint target, uint pname, int v) => _texParameteri(target, pname, v);
        public static void TexParameteriv(uint target, uint pname, int* v) => _texParameteriv(target, pname, v);
        public static void TexParameterf(uint target, uint pname, float v) => _texParameterf(target, pname, v);
        public static void GenerateMipmap(uint target) => _generateMipmap(target);
        public static void PixelStorei(uint pname, int v) => _pixelStorei(pname, v);
        public static uint GenBuffer() { uint b; _genBuffers(1, &b); return b; }
        public static void DeleteBuffer(uint b) => _deleteBuffers(1, &b);
        public static void BindBuffer(uint target, uint b) => _bindBuffer(target, b);
        public static void BufferData(uint target, nint size, void* data, uint usage) => _bufferData(target, size, data, usage);
        public static void BufferSubData(uint target, nint offset, nint size, void* data) => _bufferSubData(target, offset, size, data);
        public static uint GenVertexArray() { uint v; _genVertexArrays(1, &v); return v; }
        public static void DeleteVertexArray(uint v) => _deleteVertexArrays(1, &v);
        public static void BindVertexArray(uint v) => _bindVertexArray(v);
        public static void EnableVertexAttribArray(uint i) => _enableVertexAttribArray(i);
        public static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, int offset) =>
            _vertexAttribPointer(index, size, type, normalized ? (byte)1 : (byte)0, stride, (void*)offset);
        public static void DrawArrays(uint mode, int first, int count) => _drawArrays(mode, first, count);
        public static void DrawElements(uint mode, int count, uint type, int offset) => _drawElements(mode, count, type, (void*)offset);
        public static uint CreateShader(uint type) => _createShader(type);

        public static void ShaderSource(uint shader, string source)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source + "\0");
            fixed (byte* p = bytes)
            {
                byte* pp = p;
                int len = bytes.Length - 1;
                _shaderSource(shader, 1, &pp, &len);
            }
        }

        public static void CompileShader(uint s) => _compileShader(s);
        public static int GetShaderi(uint s, uint pname) { int v; _getShaderiv(s, pname, &v); return v; }
        public static string GetShaderInfoLog(uint s)
        {
            int len = GetShaderi(s, INFO_LOG_LENGTH);
            if (len <= 1) return "";
            byte[] buf = new byte[len];
            fixed (byte* p = buf) { int written; _getShaderInfoLog(s, len, &written, p); return Encoding.UTF8.GetString(buf, 0, written); }
        }
        public static void DeleteShader(uint s) => _deleteShader(s);
        public static uint CreateProgram() => _createProgram();
        public static void AttachShader(uint p, uint s) => _attachShader(p, s);
        public static void LinkProgram(uint p) => _linkProgram(p);
        public static int GetProgrami(uint p, uint pname) { int v; _getProgramiv(p, pname, &v); return v; }
        public static string GetProgramInfoLog(uint p)
        {
            int len = GetProgrami(p, INFO_LOG_LENGTH);
            if (len <= 1) return "";
            byte[] buf = new byte[len];
            fixed (byte* b = buf) { int written; _getProgramInfoLog(p, len, &written, b); return Encoding.UTF8.GetString(buf, 0, written); }
        }
        public static void UseProgram(uint p) => _useProgram(p);
        public static void DeleteProgram(uint p) => _deleteProgram(p);
        public static int GetUniformLocation(uint p, string name)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* b = bytes) return _getUniformLocation(p, b);
        }
        public static void Uniform1i(int loc, int v) => _uniform1i(loc, v);
        public static void Uniform1f(int loc, float v) => _uniform1f(loc, v);
        public static void Uniform2f(int loc, float x, float y) => _uniform2f(loc, x, y);
        public static void Uniform3f(int loc, float x, float y, float z) => _uniform3f(loc, x, y, z);
        public static void Uniform4f(int loc, float x, float y, float z, float w) => _uniform4f(loc, x, y, z, w);
        public static void UniformMatrix4(int loc, Matrix4x4 m)
        {
            float* buf = stackalloc float[16];
            m.CopyTo(buf);
            _uniformMatrix4fv(loc, 1, 0, buf);
        }
        public static void ReadPixels(int x, int y, int w, int h, uint format, uint type, void* data) => _readPixels(x, y, w, h, format, type, data);
        public static void Scissor(int x, int y, int w, int h) => _scissor(x, y, w, h);
        public static void LineWidth(float w) => _lineWidth(w);
        public static void Finish() => _finish();
        public static void Flush() { if (_flush != null) _flush(); }
        public static void ColorMask(bool r, bool g, bool b, bool a) => _colorMask(r ? (byte)1 : (byte)0, g ? (byte)1 : (byte)0, b ? (byte)1 : (byte)0, a ? (byte)1 : (byte)0);

        /// <summary>True when the GL timer-query entry points were found (GPU stage timing possible).</summary>
        public static bool HasTimerQueries => _genQueries != null && _beginQuery != null && _endQuery != null && _getQueryObjectiv != null && _getQueryObjectui64v != null && _queryCounter != null;
        public static uint GenQuery() { uint q; _genQueries(1, &q); return q; }
        public static void DeleteQuery(uint q) => _deleteQueries(1, &q);
        public static void BeginQuery(uint target, uint q) => _beginQuery(target, q);
        public static void EndQuery(uint target) => _endQuery(target);
        public static void QueryCounter(uint q, uint target) => _queryCounter(q, target);
        public static bool QueryResultAvailable(uint q) { int v; _getQueryObjectiv(q, QUERY_RESULT_AVAILABLE, &v); return v != 0; }
        public static ulong QueryResult64(uint q) { ulong v; _getQueryObjectui64v(q, QUERY_RESULT, &v); return v; }
        public static uint GenFramebuffer() { uint f; _genFramebuffers(1, &f); return f; }
        public static void DeleteFramebuffer(uint f) => _deleteFramebuffers(1, &f);
        public static void BindFramebuffer(uint target, uint f) => _bindFramebuffer(target, f);
        public static void FramebufferTexture2D(uint target, uint attachment, uint texTarget, uint tex, int level) => _framebufferTexture2D(target, attachment, texTarget, tex, level);
        public static uint CheckFramebufferStatus(uint target) => _checkFramebufferStatus(target);
        public static void DrawBuffer(uint buf) => _drawBuffer(buf);
        public static void ReadBuffer(uint buf) => _readBuffer(buf);
        public static void Uniform3fv(int loc, int count, float* v) => _uniform3fv(loc, count, v);
        public static void Uniform1fv(int loc, int count, float* v) => _uniform1fv(loc, count, v);
        public static void PolygonOffset(float factor, float units) => _polygonOffset(factor, units);
        public static void BlitFramebuffer(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, uint mask, uint filter) => _blitFramebuffer(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);
    }
}
