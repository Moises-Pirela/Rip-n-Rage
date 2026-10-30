using System;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.PostFx
{
    /// <summary>Offscreen framebuffer with a colour texture (RGBA16F or RGBA8) and an optional depth texture.</summary>
    public sealed unsafe class RenderTarget : IDisposable
    {
        public uint Framebuffer { get; private set; }
        public uint ColorTexture { get; private set; }
        public uint DepthTexture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Hdr { get; }
        public bool HasDepth { get; }
        /// <summary>Bilinear (true) or nearest (false) colour sampling. Nearest is much cheaper on software renderers for 1:1 passes.</summary>
        public bool Linear { get; }
        /// <summary>Depth-only target (shadow maps): no colour attachment.</summary>
        public bool DepthOnly { get; }

        public RenderTarget(int width, int height, bool hdr, bool depth, bool linear = true, bool depthOnly = false)
        {
            Hdr = hdr; HasDepth = depth || depthOnly; Linear = linear; DepthOnly = depthOnly;
            Create(width, height);
        }

        private void Create(int width, int height)
        {
            Width = Math.Max(1, width); Height = Math.Max(1, height);
            Framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(GL.FRAMEBUFFER, Framebuffer);
            if (!DepthOnly)
            {
                ColorTexture = GL.GenTexture();
                GL.BindTexture(GL.TEXTURE_2D, ColorTexture);
                if (Hdr) GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA16F, Width, Height, 0, GL.RGBA, GL.HALF_FLOAT, null);
                else GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, Width, Height, 0, GL.RGBA, GL.UNSIGNED_BYTE, null);
                uint filter = Linear ? GL.LINEAR : GL.NEAREST;
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)filter);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)filter);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
                GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, ColorTexture, 0);
            }
            else
            {
                GL.DrawBuffer(GL.NONE);
                GL.ReadBuffer(GL.NONE);
            }

            if (HasDepth)
            {
                DepthTexture = GL.GenTexture();
                GL.BindTexture(GL.TEXTURE_2D, DepthTexture);
                GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.DEPTH_COMPONENT24, Width, Height, 0, GL.DEPTH_COMPONENT, GL.UNSIGNED_INT, null);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.NEAREST);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.NEAREST);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
                GL.FramebufferTexture2D(GL.FRAMEBUFFER, GL.DEPTH_ATTACHMENT, GL.TEXTURE_2D, DepthTexture, 0);
            }

            uint status = GL.CheckFramebufferStatus(GL.FRAMEBUFFER);
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
            GL.BindTexture(GL.TEXTURE_2D, 0);
            if (status != GL.FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"Framebuffer incomplete (0x{status:X})");
        }

        public void Resize(int width, int height)
        {
            if (width == Width && height == Height) return;
            bool compare = DepthCompare;
            Release();
            Create(width, height);
            if (compare) SetDepthCompare(true);
        }

        public void Bind()
        {
            GL.BindFramebuffer(GL.FRAMEBUFFER, Framebuffer);
            GL.Viewport(0, 0, Width, Height);
        }

        public static void BindDefault(int width, int height)
        {
            GL.BindFramebuffer(GL.FRAMEBUFFER, 0);
            GL.Viewport(0, 0, width, height);
        }

        public void BindColor(int unit)
        {
            GL.ActiveTexture(GL.TEXTURE0 + (uint)unit);
            GL.BindTexture(GL.TEXTURE_2D, ColorTexture);
        }

        public void BindDepth(int unit)
        {
            GL.ActiveTexture(GL.TEXTURE0 + (uint)unit);
            GL.BindTexture(GL.TEXTURE_2D, DepthTexture);
        }

        /// <summary>
        /// Switches the depth texture to hardware depth comparison (<c>sampler2DShadow</c>: one fetch returns a
        /// bilinearly filtered 0..1 shadow factor) or back to plain depth reads. Shadow maps use compare mode;
        /// a texture in compare mode must not be sampled with a regular <c>sampler2D</c>.
        /// </summary>
        public void SetDepthCompare(bool enabled)
        {
            if (DepthTexture == 0) return;
            GL.BindTexture(GL.TEXTURE_2D, DepthTexture);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_COMPARE_MODE, (int)(enabled ? GL.COMPARE_REF_TO_TEXTURE : GL.NONE));
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_COMPARE_FUNC, (int)GL.LEQUAL);
            uint filter = enabled ? GL.LINEAR : GL.NEAREST;
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)filter);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)filter);
            GL.BindTexture(GL.TEXTURE_2D, 0);
            DepthCompare = enabled;
        }

        public bool DepthCompare { get; private set; }

        /// <summary>Copies this target's depth buffer into <paramref name="dst"/> (same size) with glBlitFramebuffer.</summary>
        public void BlitDepthTo(RenderTarget dst)
        {
            GL.BindFramebuffer(GL.READ_FRAMEBUFFER, Framebuffer);
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, dst.Framebuffer);
            GL.BlitFramebuffer(0, 0, Width, Height, 0, 0, dst.Width, dst.Height, GL.DEPTH_BUFFER_BIT, GL.NEAREST);
            GL.BindFramebuffer(GL.FRAMEBUFFER, dst.Framebuffer);
        }

        private void Release()
        {
            if (Framebuffer != 0) { GL.DeleteFramebuffer(Framebuffer); Framebuffer = 0; }
            if (ColorTexture != 0) { GL.DeleteTexture(ColorTexture); ColorTexture = 0; }
            if (DepthTexture != 0) { GL.DeleteTexture(DepthTexture); DepthTexture = 0; }
        }

        public void Dispose() => Release();
    }
}
