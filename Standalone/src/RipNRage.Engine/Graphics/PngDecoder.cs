using System;
using System.IO;
using System.IO.Compression;

namespace RipNRage.Engine.Graphics
{
    /// <summary>Decoded image: 8-bit RGBA, rows top-to-bottom.</summary>
    public sealed class Image
    {
        public int Width;
        public int Height;
        public byte[] Pixels; // RGBA8, row-major, top row first

        public Image(int w, int h) { Width = w; Height = h; Pixels = new byte[w * h * 4]; }

        /// <summary>Returns a copy with rows flipped (bottom row first) for OpenGL upload.</summary>
        public byte[] GetFlippedPixels()
        {
            var dst = new byte[Pixels.Length];
            int stride = Width * 4;
            for (int y = 0; y < Height; y++)
                Buffer.BlockCopy(Pixels, (Height - 1 - y) * stride, dst, y * stride, stride);
            return dst;
        }
    }

    /// <summary>
    /// Dependency-free PNG decoder (non-interlaced; bit depths 1/2/4/8/16; gray, RGB, palette,
    /// gray+alpha, RGBA; tRNS transparency). Uses the BCL zlib for inflate.
    /// </summary>
    public static class PngDecoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static Image Load(string path)
        {
            using var fs = File.OpenRead(path);
            return Decode(fs);
        }

        public static Image Decode(byte[] data) => Decode(new MemoryStream(data));

        public static Image Decode(Stream stream)
        {
            using var br = new BinaryReader(stream);
            var sig = br.ReadBytes(8);
            for (int i = 0; i < 8; i++)
                if (sig[i] != Signature[i]) throw new InvalidDataException("Not a PNG file");

            int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
            byte[] palette = null;
            byte[] trns = null;
            using var idat = new MemoryStream();

            while (true)
            {
                uint length = ReadBE(br);
                string type = new string(new[] { (char)br.ReadByte(), (char)br.ReadByte(), (char)br.ReadByte(), (char)br.ReadByte() });
                byte[] chunk = br.ReadBytes((int)length);
                br.ReadUInt32(); // crc

                switch (type)
                {
                    case "IHDR":
                        width = (int)BE(chunk, 0);
                        height = (int)BE(chunk, 4);
                        bitDepth = chunk[8];
                        colorType = chunk[9];
                        interlace = chunk[12];
                        break;
                    case "PLTE": palette = chunk; break;
                    case "tRNS": trns = chunk; break;
                    case "IDAT": idat.Write(chunk, 0, chunk.Length); break;
                    case "IEND": goto done;
                }
            }
            done:
            if (interlace != 0) throw new NotSupportedException("Interlaced PNGs are not supported");

            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Bad color type") };
            int bitsPerPixel = channels * bitDepth;
            int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
            int stride = (width * bitsPerPixel + 7) / 8;

            byte[] raw = new byte[(stride + 1) * height];
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            // Unfilter
            byte[] prev = new byte[stride];
            byte[] cur = new byte[stride];
            var img = new Image(width, height);
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                Unfilter(filter, cur, prev, stride, bytesPerPixel);
                ConvertRow(cur, img.Pixels, y * width * 4, width, bitDepth, colorType, palette, trns);
                var t = prev; prev = cur; cur = t;
            }
            return img;
        }

        private static void Unfilter(int filter, byte[] cur, byte[] prev, int stride, int bpp)
        {
            switch (filter)
            {
                case 0: break;
                case 1:
                    for (int i = bpp; i < stride; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]);
                    break;
                case 2:
                    for (int i = 0; i < stride; i++) cur[i] = (byte)(cur[i] + prev[i]);
                    break;
                case 3:
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= bpp ? cur[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + ((left + prev[i]) >> 1));
                    }
                    break;
                case 4:
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0;
                        int b = prev[i];
                        int c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        int pred = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                        cur[i] = (byte)(cur[i] + pred);
                    }
                    break;
                default: throw new InvalidDataException("Bad PNG filter " + filter);
            }
        }

        private static int Sample(byte[] row, int index, int bitDepth)
        {
            switch (bitDepth)
            {
                case 8: return row[index];
                case 16: return row[index * 2]; // take the high byte
                case 1: return (row[index >> 3] >> (7 - (index & 7))) & 1;
                case 2: return (row[index >> 2] >> ((3 - (index & 3)) * 2)) & 3;
                case 4: return (row[index >> 1] >> ((1 - (index & 1)) * 4)) & 15;
                default: throw new InvalidDataException("Bad bit depth");
            }
        }

        private static void ConvertRow(byte[] row, byte[] dst, int dstOffset, int width, int bitDepth, int colorType, byte[] palette, byte[] trns)
        {
            int maxVal = bitDepth >= 8 ? 255 : (1 << bitDepth) - 1;
            for (int x = 0; x < width; x++)
            {
                int o = dstOffset + x * 4;
                switch (colorType)
                {
                    case 0:
                    {
                        int g = Sample(row, x, bitDepth);
                        byte v = bitDepth >= 8 ? (byte)g : (byte)(g * 255 / maxVal);
                        byte a = 255;
                        if (trns != null && trns.Length >= 2)
                        {
                            int tg = (trns[0] << 8) | trns[1];
                            if (bitDepth == 16) tg >>= 8;
                            if (g == tg) a = 0;
                        }
                        dst[o] = v; dst[o + 1] = v; dst[o + 2] = v; dst[o + 3] = a;
                        break;
                    }
                    case 2:
                    {
                        int r = Sample(row, x * 3, bitDepth), g = Sample(row, x * 3 + 1, bitDepth), b = Sample(row, x * 3 + 2, bitDepth);
                        byte a = 255;
                        if (trns != null && trns.Length >= 6)
                        {
                            int tr = trns[bitDepth == 16 ? 0 : 1], tg = trns[bitDepth == 16 ? 2 : 3], tb = trns[bitDepth == 16 ? 4 : 5];
                            if (r == tr && g == tg && b == tb) a = 0;
                        }
                        dst[o] = (byte)r; dst[o + 1] = (byte)g; dst[o + 2] = (byte)b; dst[o + 3] = a;
                        break;
                    }
                    case 3:
                    {
                        int idx = Sample(row, x, bitDepth);
                        if (palette == null || idx * 3 + 2 >= palette.Length) { dst[o] = dst[o + 1] = dst[o + 2] = 0; dst[o + 3] = 255; break; }
                        dst[o] = palette[idx * 3]; dst[o + 1] = palette[idx * 3 + 1]; dst[o + 2] = palette[idx * 3 + 2];
                        dst[o + 3] = trns != null && idx < trns.Length ? trns[idx] : (byte)255;
                        break;
                    }
                    case 4:
                    {
                        int g = Sample(row, x * 2, bitDepth), a = Sample(row, x * 2 + 1, bitDepth);
                        dst[o] = (byte)g; dst[o + 1] = (byte)g; dst[o + 2] = (byte)g; dst[o + 3] = (byte)a;
                        break;
                    }
                    case 6:
                    {
                        dst[o] = (byte)Sample(row, x * 4, bitDepth);
                        dst[o + 1] = (byte)Sample(row, x * 4 + 1, bitDepth);
                        dst[o + 2] = (byte)Sample(row, x * 4 + 2, bitDepth);
                        dst[o + 3] = (byte)Sample(row, x * 4 + 3, bitDepth);
                        break;
                    }
                }
            }
        }

        private static uint ReadBE(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }

        private static uint BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    }

    /// <summary>Tiny PNG writer (RGBA8, no filtering) used for screenshots.</summary>
    public static class PngEncoder
    {
        public static void Save(string path, int width, int height, byte[] rgbaTopDown)
        {
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)width); WriteBE(ihdr, 4, (uint)height);
            ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            WriteChunk(bw, "IHDR", ihdr);

            var raw = new byte[(width * 4 + 1) * height];
            for (int y = 0; y < height; y++)
            {
                raw[y * (width * 4 + 1)] = 0;
                Buffer.BlockCopy(rgbaTopDown, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
            }
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            WriteChunk(bw, "IDAT", ms.ToArray());
            WriteChunk(bw, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
        {
            var len = new byte[4]; WriteBE(len, 0, (uint)data.Length); bw.Write(len);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            bw.Write(typeBytes);
            bw.Write(data);
            uint crc = Crc32(typeBytes, data);
            var c = new byte[4]; WriteBE(c, 0, crc); bw.Write(c);
        }

        private static void WriteBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        private static uint[] _crcTable;
        private static uint Crc32(byte[] a, byte[] b)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    _crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            foreach (var x in a) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
            foreach (var x in b) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
