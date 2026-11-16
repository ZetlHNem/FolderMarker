# -*- coding: utf-8 -*-
"""把 ico 里的每一帧导出成 png，并拼一张多尺寸预览图，方便肉眼核对"""
import os, struct, zlib, sys

HERE = os.path.dirname(os.path.abspath(__file__))


def png_chunk(typ, data):
    return (struct.pack(">I", len(data)) + typ + data +
            struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff))


def write_png(path, w, h, rgba):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgba[y * w * 4:(y + 1) * w * 4]
    png = b"\x89PNG\r\n\x1a\n"
    png += png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += png_chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def read_ico(path):
    data = open(path, "rb").read()
    _, _, n = struct.unpack("<HHH", data[:6])
    frames = []
    for i in range(n):
        off = 6 + 16 * i
        w, h, _, _, _, _, size, offset = struct.unpack("<BBBBHHII", data[off:off + 16])
        png = data[offset:offset + size]
        if png[:8] != b"\x89PNG\r\n\x1a\n":
            continue
        iw, ih = struct.unpack(">II", png[16:24])
        frames.append((iw, ih, png))
    return frames


def decode_png(png):
    w, h = struct.unpack(">II", png[16:24])
    pos, idat = 8, b""
    while pos < len(png):
        ln = struct.unpack(">I", png[pos:pos + 4])[0]
        typ = png[pos + 4:pos + 8]
        if typ == b"IDAT":
            idat += png[pos + 8:pos + 8 + ln]
        pos += 12 + ln
    raw = zlib.decompress(idat)
    stride = w * 4
    out = bytearray(stride * h)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        ft = raw[p]; p += 1
        line = bytearray(raw[p:p + stride]); p += stride
        if ft == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, bytes(out)


def sample(w, h, rgba, sx, sy):
    """双线性取样，用于放大显示"""
    x = min(max(sx, 0), w - 1)
    y = min(max(sy, 0), h - 1)
    i = (y * w + x) * 4
    return rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]


def scale(w, h, rgba, tw, th):
    out = bytearray(tw * th * 4)
    for y in range(th):
        for x in range(tw):
            fx = (x + 0.5) * w / tw - 0.5
            fy = (y + 0.5) * h / th - 0.5
            x0, y0 = int(fx // 1), int(fy // 1)
            tx, ty = fx - x0, fy - y0
            c00 = sample(w, h, rgba, x0, y0)
            c10 = sample(w, h, rgba, x0 + 1, y0)
            c01 = sample(w, h, rgba, x0, y0 + 1)
            c11 = sample(w, h, rgba, x0 + 1, y0 + 1)
            acc = []
            for k in range(4):
                top = c00[k] * (1 - tx) + c10[k] * tx
                bot = c01[k] * (1 - tx) + c11[k] * tx
                acc.append(int(top * (1 - ty) + bot * ty + 0.5))
            i = (y * tw + x) * 4
            out[i:i + 4] = bytes(acc)
    return bytes(out)


def montage(ico_path, out_path, sizes=(256, 48, 32, 16), bg=(245, 246, 248)):
    frames = {}
    for (_, _, png) in read_ico(ico_path):
        fw, fh, fragba = decode_png(png)
        frames[fw] = (fw, fh, fragba)
    pad, gap = 16, 24
    label_h = 0
    tiles = []
    for s in sizes:
        if s not in frames:
            continue
        w, h, rgba = frames[s]
        show = max(64, s)
        tiles.append((s, show, scale(w, h, rgba, show, show)))
    width = pad * 2 + sum(t[1] for t in tiles) + gap * (len(tiles) - 1)
    height = pad * 2 + max(t[1] for t in tiles) + label_h
    canvas = bytearray(width * height * 4)
    for i in range(width * height):
        canvas[i * 4:i * 4 + 4] = bytes(bg[0:3]) + b"\xff"
    x = pad
    for s, show, rgba in tiles:
        y = pad
        for yy in range(show):
            for xx in range(show):
                si = (yy * show + xx) * 4
                a = rgba[si + 3] / 255.0
                if a <= 0:
                    continue
                di = ((y + yy) * width + (x + xx)) * 4
                for k in range(3):
                    canvas[di + k] = int(rgba[si + k] * a + canvas[di + k] * (1 - a))
        x += show + gap
    write_png(out_path, width, height, bytes(canvas))
    print("wrote", out_path, width, "x", height)


if __name__ == "__main__":
    ico = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(HERE), "icons", "app.ico")
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(HERE), "build", "preview_app.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    montage(ico, out)
