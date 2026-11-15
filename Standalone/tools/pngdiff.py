#!/usr/bin/env python3
"""Pixel gate: percentage of pixels that differ between two screenshots (PNG, 8-bit RGB / RGBA, as the game writes).

    python3 tools/pngdiff.py a.png b.png [threshold]

A pixel differs when any channel differs by more than `threshold` (default 0). Standard library only.
"""
import sys
import struct
import zlib


def read_png(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path}: not a PNG")
    pos, idat, w = 8, b"", None
    while pos < len(data):
        n, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if kind == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            if depth != 8 or ctype not in (2, 6):
                raise SystemExit(f"{path}: only 8-bit RGB / RGBA is supported")
            bpp = 3 if ctype == 2 else 4
        elif kind == b"IDAT":
            idat += body
        pos += 12 + n
    raw, stride = zlib.decompress(idat), w * bpp
    rows, prev = [], bytearray(stride)
    for y in range(h):
        f, line = raw[y * (stride + 1)], bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return w, h, bpp, rows


def main():
    a, b = read_png(sys.argv[1]), read_png(sys.argv[2])
    t = int(sys.argv[3]) if len(sys.argv) > 3 else 0
    if a[:2] != b[:2]:
        raise SystemExit(f"size differs: {a[:2]} vs {b[:2]}")
    w, h = a[0], a[1]
    differ = 0
    for ra, rb in zip(a[3], b[3]):
        for x in range(w):
            if any(abs(ra[x * a[2] + c] - rb[x * b[2] + c]) > t for c in range(3)):
                differ += 1
    print(f"{100.0 * differ / (w * h):.3f}% of pixels differ ({differ} of {w * h})")


if __name__ == "__main__":
    main()
