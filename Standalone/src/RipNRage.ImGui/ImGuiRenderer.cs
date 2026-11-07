using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Platform;

namespace RipNRage.ImGuiIntegration
{
    /// <summary>
    /// OpenGL 3.3 backend for Dear ImGui: uploads the font atlas once and replays each frame's draw lists
    /// (one streamed vertex / index buffer, a scissor rect per command). Equivalent to imgui_impl_opengl3,
    /// written against the engine's own GL binding so no extra native dependency is needed.
    /// </summary>
    internal sealed unsafe class ImGuiRenderer : IDisposable
    {
        private uint _vao, _vbo, _ebo, _fontTexture;
        private Shader _shader;
        private int _vboSize, _eboSize;

        private const string VertexSrc = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
uniform mat4 uProjection;
out vec2 vUV;
out vec4 vColor;
void main()
{
    vUV = aUV;
    vColor = aColor;
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
}";

        private const string FragmentSrc = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
uniform sampler2D uTexture;
out vec4 fragColor;
void main()
{
    fragColor = vColor * texture(uTexture, vUV);
}";

        public ImGuiRenderer()
        {
            _shader = new Shader(VertexSrc, FragmentSrc, "imgui");
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            int stride = sizeof(ImDrawVert);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, GL.FLOAT, false, stride, 0);                                  // pos
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, GL.FLOAT, false, stride, 8);                                  // uv
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, GL.UNSIGNED_BYTE, true, stride, 16);                          // packed RGBA
            GL.BindVertexArray(0);

            RebuildFontAtlas();
        }

        /// <summary>
        /// Uploads ImGui's font atlas (call again after adding fonts). The texture is created here rather than through
        /// <see cref="Texture2D"/> because that flips rows for the game's bottom-up convention, which would invert the
        /// atlas against ImGui's top-down UVs.
        /// </summary>
        public void RebuildFontAtlas()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

            if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
            _fontTexture = GL.GenTexture();
            GL.BindTexture(GL.TEXTURE_2D, _fontTexture);
            GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
            GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, width, height, 0, GL.RGBA, GL.UNSIGNED_BYTE, (void*)pixels);
            // sampled 1:1 in screen space; bilinear keeps glyphs smooth if a panel is ever scaled
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
            GL.BindTexture(GL.TEXTURE_2D, 0);

            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
        }

        /// <summary>
        /// Texture handle for <see cref="ImGui.Image(IntPtr, System.Numerics.Vector2)"/>. Engine textures are stored
        /// bottom-up, so pass uv0 = (0,1) and uv1 = (1,0) when drawing one (see <see cref="ImGuiController.Image"/>).
        /// </summary>
        public static IntPtr TextureId(Texture2D texture) => texture == null ? IntPtr.Zero : (IntPtr)texture.Handle;

        public void Render(ImDrawDataPtr drawData, int framebufferWidth, int framebufferHeight)
        {
            if (drawData.CmdListsCount == 0 || framebufferWidth <= 0 || framebufferHeight <= 0) return;

            // ImGui works in points; the framebuffer may be denser (Retina)
            drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

            GL.Enable(GL.BLEND);
            GL.BlendEquation(GL.FUNC_ADD);
            GL.BlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.ONE, GL.ONE_MINUS_SRC_ALPHA);
            GL.Disable(GL.CULL_FACE);
            GL.Disable(GL.DEPTH_TEST);
            GL.Enable(GL.SCISSOR_TEST);
            GL.Viewport(0, 0, framebufferWidth, framebufferHeight);

            // ImGui's origin is top-left, so the orthographic projection flips Y
            var io = ImGui.GetIO();
            float l = drawData.DisplayPos.X, r = l + drawData.DisplaySize.X;
            float t = drawData.DisplayPos.Y, b = t + drawData.DisplaySize.Y;
            var projection = new[]
            {
                2f / (r - l),       0f,                 0f, 0f,
                0f,                 2f / (t - b),       0f, 0f,
                0f,                 0f,                -1f, 0f,
                (r + l) / (l - r), (t + b) / (b - t),   0f, 1f,
            };

            _shader.Use();
            _shader.SetMatrixRaw("uProjection", projection);
            _shader.SetInt("uTexture", 0);
            GL.ActiveTexture(GL.TEXTURE0);
            GL.BindVertexArray(_vao);

            var clipOffset = drawData.DisplayPos;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr list = drawData.CmdLists[n];

                int vtxBytes = list.VtxBuffer.Size * sizeof(ImDrawVert);
                int idxBytes = list.IdxBuffer.Size * sizeof(ushort);
                GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
                if (vtxBytes > _vboSize) { _vboSize = Math.Max(vtxBytes, _vboSize * 2); GL.BufferData(GL.ARRAY_BUFFER, _vboSize, null, GL.STREAM_DRAW); }
                GL.BufferSubData(GL.ARRAY_BUFFER, 0, vtxBytes, (void*)list.VtxBuffer.Data);
                GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
                if (idxBytes > _eboSize) { _eboSize = Math.Max(idxBytes, _eboSize * 2); GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, _eboSize, null, GL.STREAM_DRAW); }
                GL.BufferSubData(GL.ELEMENT_ARRAY_BUFFER, 0, idxBytes, (void*)list.IdxBuffer.Data);

                for (int i = 0; i < list.CmdBuffer.Size; i++)
                {
                    ImDrawCmdPtr cmd = list.CmdBuffer[i];
                    if (cmd.UserCallback != IntPtr.Zero) continue; // the editors do not use draw callbacks

                    float cx = cmd.ClipRect.X - clipOffset.X, cy = cmd.ClipRect.Y - clipOffset.Y;
                    float cz = cmd.ClipRect.Z - clipOffset.X, cw = cmd.ClipRect.W - clipOffset.Y;
                    if (cz <= cx || cw <= cy) continue;
                    // GL scissor counts from the bottom edge
                    GL.Scissor((int)cx, (int)(framebufferHeight - cw), (int)(cz - cx), (int)(cw - cy));

                    GL.BindTexture(GL.TEXTURE_2D, (uint)(nint)cmd.GetTexID());
                    GL.DrawElementsBaseVertex(GL.TRIANGLES, (int)cmd.ElemCount, GL.UNSIGNED_SHORT, (int)(cmd.IdxOffset * sizeof(ushort)), (int)cmd.VtxOffset);
                }
            }

            GL.BindVertexArray(0);
            GL.Disable(GL.SCISSOR_TEST);
            GL.BindTexture(GL.TEXTURE_2D, 0);
            // leave the state the rest of the engine expects: a plain source-alpha blend for both colour and alpha.
            // ImGui's separate alpha function would otherwise still be set when the next frame's UI batch runs, which
            // leaves the framebuffer's alpha channel wrong (visible in screenshots).
            GL.BlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        }

        public void Dispose()
        {
            if (_fontTexture != 0) { GL.DeleteTexture(_fontTexture); _fontTexture = 0; }
            _shader?.Dispose();
            if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
            if (_ebo != 0) { GL.DeleteBuffer(_ebo); _ebo = 0; }
        }
    }
}
