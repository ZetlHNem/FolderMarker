# -*- coding: utf-8 -*-
"""把生成的 ico 渲染成一张对比图，用于肉眼检查各尺寸下的效果。

第一列：128px 原大；后面三列：32 / 24 / 16px 分别放大 3/4/6 倍（最近邻），
放大是为了看清小尺寸下颜色标记是否还认得出。
"""
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ICONS = os.path.join(ROOT, "icons")
NATIVE = os.path.join(ROOT, "build", "native")
OUT = os.path.join(ROOT, "build", "preview_colors.png")

NAMES = ["(native)", "red", "orange", "yellow", "green", "blue", "purple", "gray"]
BIG = 128
SMALL = [(32, 3), (24, 4), (16, 6)]


def load_native(size):
    p = os.path.join(NATIVE, "%d.rgba" % size)
    if not os.path.exists(p):
        return None
    d = open(p, "rb").read()
    w, h = struct.unpack("<II", d[:8])
    return w, h, d[8:]


def read_ico_frame(path, want):
    data = open(path, "rb").read()
    n = struct.unpack_from("<H", data, 4)[0]
    for i in range(n):
        e = 6 + 16 * i
        w = data[e] or 256
        size, off = struct.unpack_from("<II", data, e + 8)
        if w == want:
            return w, data[off:off + size]
    return None


def decode_png(png):
    pos = 8
    idat = b""
    w = h = ct = 0
    while pos < len(png):
        ln = struct.unpack_from(">I", png, pos)[0]
        typ = png[pos + 4:pos + 8]
        chunk = png[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, _bd, ct = struct.unpack_from(">IIBB", chunk, 0)
        elif typ == b"IDAT":
            idat += chunk
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[ct]
    stride = w * ch
    prev = bytearray(stride)
    rows = []
    p = 0
    for _ in range(h):
        f = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if f == 1:
            for i in range(ch, stride):
                line[i] = (line[i] + line[i - ch]) & 255
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 255
        elif f == 3:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 255
        elif f == 4:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                b = prev[i]
                c = prev[i - ch] if i >= ch else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        prev = line
        rows.append(bytes(line))
    out = bytearray(w * h * 4)
    for y in range(h):
        row = rows[y]
        for x in range(w):
            i = (y * w + x) * 4
            if ch == 4:
                out[i:i + 4] = row[x * 4:x * 4 + 4]
            else:
                out[i] = row[x * 3]
                out[i + 1] = row[x * 3 + 1]
                out[i + 2] = row[x * 3 + 2]
                out[i + 3] = 255
    return w, h, bytes(out)


def png_bytes(w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, body):
        return (struct.pack(">I", len(body)) + tag + body
                + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


BG = (252, 252, 252)


def main():
    col1 = BIG + 20
    cols = [col1] + [s * z + 20 for s, z in SMALL]
    W = sum(cols) + 10
    rowh = BIG + 16
    H = rowh * len(NAMES) + 10

    canvas = bytearray()
    for _ in range(W * H):
        canvas += bytes(BG) + b"\xff"

    def blit(rgba, sw, sh, dx, dy, scale):
        for y in range(sh):
            for x in range(sw):
                i = (y * sw + x) * 4
                a = rgba[i + 3]
                if a == 0:
                    continue
                af = a / 255.0
                for yy in range(scale):
                    for xx in range(scale):
                        tx, ty = dx + x * scale + xx, dy + y * scale + yy
                        if not (0 <= tx < W and 0 <= ty < H):
                            continue
                        j = (ty * W + tx) * 4
                        for c in range(3):
                            canvas[j + c] = int(rgba[i + c] * af + BG[c] * (1 - af))

    for row, name in enumerate(NAMES):
        y0 = 8 + row * rowh
        if name == "(native)":
            get = lambda s: load_native(s)
        else:
            def get(s, nm=name):
                fr = read_ico_frame(os.path.join(ICONS, nm + ".ico"), s)
                if not fr:
                    return None
                return decode_png(fr[1])

        # 大图
        big = get(BIG)
        if big:
            blit(big[2], big[0], big[1], 10, y0 + (BIG - big[1]) // 2, 1)
        # 小图放大
        x = 10 + col1
        for s, z in SMALL:
            sm = get(s)
            if sm:
                blit(sm[2], sm[0], sm[1], x, y0 + (BIG - sm[1] * z) // 2, z)
            x += s * z + 20

    with open(OUT, "wb") as f:
        f.write(png_bytes(W, H, canvas))
    print("已生成", OUT, W, "x", H)


if __name__ == "__main__":
    main()
