using System;
using System.Collections.Generic;
using System.IO;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }

    /// <summary>GPU texture (RGBA8). Mirrors the bits of UnityEngine.Texture2D the game touches.</summary>
    public sealed class Texture2D : EngineObject, IDisposable
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public uint Handle { get; private set; }
        public int width { get; private set; }
        public int height { get; private set; }
        public FilterMode filterMode { get; private set; }
        public TextureWrapMode wrapMode { get; private set; }
        public bool HasMipmaps { get; private set; }
        public string Path { get; private set; }

        private Texture2D() { }

        public static unsafe Texture2D Create(Image image, FilterMode filter = FilterMode.Trilinear, TextureWrapMode wrap = TextureWrapMode.Clamp, bool mipmaps = true, string name = null)
        {
            var tex = new Texture2D { width = image.Width, height = image.Height, filterMode = filter, wrapMode = wrap, HasMipmaps = mipmaps, name = name ?? "Texture" };
            byte[] flipped = image.GetFlippedPixels(); // GL expects the bottom row first
            tex.Handle = GL.GenTexture();
            GL.BindTexture(GL.TEXTURE_2D, tex.Handle);
            GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
            fixed (byte* p = flipped)
                GL.TexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, image.Width, image.Height, 0, GL.RGBA, GL.UNSIGNED_BYTE, p);
            tex.ApplyParameters();
            if (mipmaps) GL.GenerateMipmap(GL.TEXTURE_2D);
            GL.BindTexture(GL.TEXTURE_2D, 0);
            return tex;
        }

        /// <summary>Creates a solid colour texture (for untextured geometry / UI fills).</summary>
        public static Texture2D CreateSolid(byte r, byte g, byte b, byte a = 255, string name = "Solid")
        {
            var img = new Image(4, 4);
            for (int i = 0; i < 16; i++) { img.Pixels[i * 4] = r; img.Pixels[i * 4 + 1] = g; img.Pixels[i * 4 + 2] = b; img.Pixels[i * 4 + 3] = a; }
            return Create(img, FilterMode.Point, TextureWrapMode.Repeat, false, name);
        }

        private static Texture2D _white;
        public static Texture2D White => _white ??= CreateSolid(255, 255, 255, 255, "White");

        /// <summary>Loads (and caches) a PNG from disk.</summary>
        public static Texture2D Load(string path, FilterMode filter = FilterMode.Trilinear, TextureWrapMode wrap = TextureWrapMode.Clamp, bool mipmaps = true)
        {
            string full = System.IO.Path.GetFullPath(path);
            if (Cache.TryGetValue(full, out var cached)) return cached;
            if (!File.Exists(full)) throw new FileNotFoundException("Texture not found: " + full);
            var img = ImageLoader.Load(full);
            var tex = Create(img, filter, wrap, mipmaps, System.IO.Path.GetFileNameWithoutExtension(full));
            tex.Path = full;
            Cache[full] = tex;
            return tex;
        }

        private void ApplyParameters()
        {
            int min, mag;
            switch (filterMode)
            {
                case FilterMode.Point:
                    min = (int)(HasMipmaps ? GL.NEAREST_MIPMAP_LINEAR : GL.NEAREST); mag = (int)GL.NEAREST; break;
                case FilterMode.Bilinear:
                    min = (int)(HasMipmaps ? GL.LINEAR_MIPMAP_LINEAR : GL.LINEAR); mag = (int)GL.LINEAR; break;
                default:
                    min = (int)(HasMipmaps ? GL.LINEAR_MIPMAP_LINEAR : GL.LINEAR); mag = (int)GL.LINEAR; break;
            }
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, min);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, mag);
            int w = (int)(wrapMode == TextureWrapMode.Repeat ? GL.REPEAT : GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, w);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, w);
            if (HasMipmaps) GL.TexParameterf(GL.TEXTURE_2D, GL.TEXTURE_MAX_ANISOTROPY, 4f);
        }

        public void Bind(int unit = 0)
        {
            GL.ActiveTexture(GL.TEXTURE0 + (uint)unit);
            GL.BindTexture(GL.TEXTURE_2D, Handle);
        }

        public void Dispose()
        {
            if (Handle != 0) { GL.DeleteTexture(Handle); Handle = 0; }
            if (Path != null) Cache.Remove(Path);
        }
    }
}
