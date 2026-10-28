using System;
using System.IO;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Dependency-free baseline JPEG decoder (Huffman, 8-bit, 1 or 3 components, any chroma
    /// subsampling; no progressive/arithmetic). Enough for the textures that ship with the game.
    /// </summary>
    public static class JpegDecoder
    {
        private sealed class Component
        {
            public int id, h, v, tq, td, ta;
            public int blocksW, blocksH;
            public byte[] pixels; // component plane at its own resolution
            public int stride;
            public int dcPred;
        }

        private sealed class Huffman
        {
            public readonly byte[] Bits = new byte[17];
            public byte[] Values;
            public readonly int[] MinCode = new int[17], MaxCode = new int[18], ValPtr = new int[17];

            public void Build()
            {
                int code = 0, k = 0;
                for (int l = 1; l <= 16; l++)
                {
                    ValPtr[l] = k;
                    MinCode[l] = code;
                    code += Bits[l];
                    k += Bits[l];
                    MaxCode[l] = Bits[l] > 0 ? code - 1 : -1;
                    code <<= 1;
                }
                MaxCode[17] = int.MaxValue;
            }
        }

        private static readonly int[] Zigzag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        public static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

        public static Image Decode(byte[] data)
        {
            var qt = new int[4][];
            var dcTables = new Huffman[4];
            var acTables = new Huffman[4];
            Component[] comps = null;
            int width = 0, height = 0, restartInterval = 0;
            int pos = 2;

            while (pos < data.Length)
            {
                if (data[pos] != 0xFF) { pos++; continue; }
                int marker = data[pos + 1];
                pos += 2;
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                if (marker == 0xD9) break;
                int len = (data[pos] << 8) | data[pos + 1];
                int segStart = pos + 2, segEnd = pos + len;

                switch (marker)
                {
                    case 0xDB: // DQT
                    {
                        int p = segStart;
                        while (p < segEnd)
                        {
                            int pq = data[p] >> 4, tq = data[p] & 15; p++;
                            var table = new int[64];
                            for (int i = 0; i < 64; i++)
                            {
                                if (pq == 0) table[Zigzag[i]] = data[p++];
                                else { table[Zigzag[i]] = (data[p] << 8) | data[p + 1]; p += 2; }
                            }
                            qt[tq] = table;
                        }
                        break;
                    }
                    case 0xC4: // DHT
                    {
                        int p = segStart;
                        while (p < segEnd)
                        {
                            int tc = data[p] >> 4, th = data[p] & 15; p++;
                            var h = new Huffman();
                            int total = 0;
                            for (int i = 1; i <= 16; i++) { h.Bits[i] = data[p++]; total += h.Bits[i]; }
                            h.Values = new byte[total];
                            Array.Copy(data, p, h.Values, 0, total); p += total;
                            h.Build();
                            if (tc == 0) dcTables[th] = h; else acTables[th] = h;
                        }
                        break;
                    }
                    case 0xC0: case 0xC1: // SOF0/1 baseline & extended sequential
                    {
                        int p = segStart;
                        if (data[p] != 8) throw new NotSupportedException("JPEG: only 8-bit precision");
                        height = (data[p + 1] << 8) | data[p + 2];
                        width = (data[p + 3] << 8) | data[p + 4];
                        int n = data[p + 5]; p += 6;
                        comps = new Component[n];
                        for (int i = 0; i < n; i++)
                        {
                            comps[i] = new Component { id = data[p], h = data[p + 1] >> 4, v = data[p + 1] & 15, tq = data[p + 2] };
                            p += 3;
                        }
                        break;
                    }
                    case 0xC2: throw new NotSupportedException("JPEG: progressive files are not supported");
                    case 0xDD: restartInterval = (data[segStart] << 8) | data[segStart + 1]; break;
                    case 0xDA: // SOS
                    {
                        if (comps == null) throw new InvalidDataException("JPEG: SOS before SOF");
                        int p = segStart;
                        int ns = data[p++];
                        for (int i = 0; i < ns; i++)
                        {
                            int cid = data[p], tables = data[p + 1]; p += 2;
                            foreach (var c in comps) if (c.id == cid) { c.td = tables >> 4; c.ta = tables & 15; }
                        }
                        pos = segEnd;
                        pos = DecodeScan(data, pos, comps, qt, dcTables, acTables, width, height, restartInterval);
                        continue;
                    }
                }
                pos = segEnd;
            }

            if (comps == null) throw new InvalidDataException("JPEG: no frame");
            return ToImage(comps, width, height);
        }

        private sealed class BitReader
        {
            private readonly byte[] _d; public int Pos; private int _bits, _count;
            public BitReader(byte[] d, int pos) { _d = d; Pos = pos; }

            public int Bit()
            {
                if (_count == 0)
                {
                    if (Pos >= _d.Length) return 0;
                    int b = _d[Pos++];
                    if (b == 0xFF)
                    {
                        int b2 = Pos < _d.Length ? _d[Pos] : 0;
                        if (b2 == 0) Pos++;            // stuffed byte
                        else if (b2 >= 0xD0 && b2 <= 0xD7) { /* marker: handled by restart logic */ }
                        else { Pos--; b = 0; }         // other marker: feed zeros
                    }
                    _bits = b; _count = 8;
                }
                _count--;
                return (_bits >> _count) & 1;
            }

            public int Bits(int n) { int v = 0; for (int i = 0; i < n; i++) v = (v << 1) | Bit(); return v; }
            public void Reset() { _count = 0; }

            public int Decode(Huffman h)
            {
                int code = 0;
                for (int l = 1; l <= 16; l++)
                {
                    code = (code << 1) | Bit();
                    if (h.MaxCode[l] >= 0 && code <= h.MaxCode[l] && code >= h.MinCode[l])
                        return h.Values[h.ValPtr[l] + code - h.MinCode[l]];
                }
                return 0;
            }

            public static int Extend(int v, int t) => t == 0 ? 0 : (v < (1 << (t - 1)) ? v - (1 << t) + 1 : v);
        }

        private static int DecodeScan(byte[] data, int pos, Component[] comps, int[][] qt, Huffman[] dc, Huffman[] ac, int width, int height, int restartInterval)
        {
            int hmax = 1, vmax = 1;
            foreach (var c in comps) { hmax = Math.Max(hmax, c.h); vmax = Math.Max(vmax, c.v); }
            int mcuW = 8 * hmax, mcuH = 8 * vmax;
            int mcusX = (width + mcuW - 1) / mcuW, mcusY = (height + mcuH - 1) / mcuH;
            foreach (var c in comps)
            {
                c.blocksW = mcusX * c.h; c.blocksH = mcusY * c.v;
                c.stride = c.blocksW * 8;
                c.pixels = new byte[c.stride * c.blocksH * 8];
                c.dcPred = 0;
            }

            var br = new BitReader(data, pos);
            var block = new int[64];
            int mcuCount = 0;
            for (int my = 0; my < mcusY; my++)
            {
                for (int mx = 0; mx < mcusX; mx++)
                {
                    if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0)
                    {
                        // expect RSTn marker
                        br.Reset();
                        while (br.Pos + 1 < data.Length && !(data[br.Pos] == 0xFF && data[br.Pos + 1] >= 0xD0 && data[br.Pos + 1] <= 0xD7)) br.Pos++;
                        if (br.Pos + 1 < data.Length) br.Pos += 2;
                        foreach (var c in comps) c.dcPred = 0;
                    }
                    mcuCount++;
                    foreach (var c in comps)
                    {
                        for (int by = 0; by < c.v; by++)
                            for (int bx = 0; bx < c.h; bx++)
                            {
                                Array.Clear(block, 0, 64);
                                int t = br.Decode(dc[c.td]);
                                int diff = t == 0 ? 0 : BitReader.Extend(br.Bits(t), t);
                                c.dcPred += diff;
                                block[0] = c.dcPred * qt[c.tq][0];
                                int k = 1;
                                while (k < 64)
                                {
                                    int rs = br.Decode(ac[c.ta]);
                                    int r = rs >> 4, s = rs & 15;
                                    if (s == 0)
                                    {
                                        if (r == 15) { k += 16; continue; }
                                        break;
                                    }
                                    k += r;
                                    if (k > 63) break;
                                    block[Zigzag[k]] = BitReader.Extend(br.Bits(s), s) * qt[c.tq][Zigzag[k]];
                                    k++;
                                }
                                Idct(block, c.pixels, ((my * c.v + by) * 8) * c.stride + (mx * c.h + bx) * 8, c.stride);
                            }
                    }
                }
            }
            // skip to the next marker
            int p2 = br.Pos;
            while (p2 + 1 < data.Length && !(data[p2] == 0xFF && data[p2 + 1] != 0 && !(data[p2 + 1] >= 0xD0 && data[p2 + 1] <= 0xD7))) p2++;
            return p2;
        }

        private static readonly float[] CosTable = BuildCos();
        private static float[] BuildCos()
        {
            var t = new float[64];
            for (int x = 0; x < 8; x++)
                for (int u = 0; u < 8; u++)
                    t[x * 8 + u] = (float)((u == 0 ? Math.Sqrt(0.5) : 1.0) * Math.Cos((2 * x + 1) * u * Math.PI / 16.0));
            return t;
        }

        private static void Idct(int[] block, byte[] dst, int offset, int stride)
        {
            // separable float IDCT (rows then columns)
            var tmp = new float[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    float s = 0f;
                    for (int u = 0; u < 8; u++) s += CosTable[x * 8 + u] * block[y * 8 + u];
                    tmp[y * 8 + x] = s / 2f;
                }
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                {
                    float s = 0f;
                    for (int v = 0; v < 8; v++) s += CosTable[y * 8 + v] * tmp[v * 8 + x];
                    int val = (int)Math.Round(s / 2f + 128f);
                    dst[offset + y * stride + x] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val);
                }
        }

        private static Image ToImage(Component[] comps, int width, int height)
        {
            var img = new Image(width, height);
            int hmax = 1, vmax = 1;
            foreach (var c in comps) { hmax = Math.Max(hmax, c.h); vmax = Math.Max(vmax, c.v); }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    if (comps.Length == 1)
                    {
                        var c = comps[0];
                        byte g = c.pixels[(y * c.v / vmax) * c.stride + x * c.h / hmax];
                        img.Pixels[o] = img.Pixels[o + 1] = img.Pixels[o + 2] = g; img.Pixels[o + 3] = 255;
                        continue;
                    }
                    float Y = Sample(comps[0], x, y, hmax, vmax);
                    float Cb = Sample(comps[1], x, y, hmax, vmax) - 128f;
                    float Cr = Sample(comps[2], x, y, hmax, vmax) - 128f;
                    img.Pixels[o] = Clamp(Y + 1.402f * Cr);
                    img.Pixels[o + 1] = Clamp(Y - 0.344136f * Cb - 0.714136f * Cr);
                    img.Pixels[o + 2] = Clamp(Y + 1.772f * Cb);
                    img.Pixels[o + 3] = 255;
                }
            return img;
        }

        private static float Sample(Component c, int x, int y, int hmax, int vmax) => c.pixels[(y * c.v / vmax) * c.stride + x * c.h / hmax];
        private static byte Clamp(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f));
    }

    /// <summary>Loads PNG or JPEG by sniffing the file signature (several assets are JPEGs named .png).</summary>
    public static class ImageLoader
    {
        public static Image Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (JpegDecoder.IsJpeg(data)) return JpegDecoder.Decode(data);
            return PngDecoder.Decode(data);
        }
    }
}
