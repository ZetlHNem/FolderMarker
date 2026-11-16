# -*- coding: utf-8 -*-
"""把原生文件夹图标按 macO 标记色上色，输出多尺寸 .ico。

思路：不重绘任何形状，只把像素色相 (H) 换成目标色、饱和度按比例缩放，
明度 (V) 完全保留 —— 于是原生图标的渐变、阴影、高光全部还在，
但整只文件夹变成标记色，16px 下也一眼能认出来。
"""
import colorsys
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
NATIVE = os.path.join(ROOT, "build", "native")
ICONS = os.path.join(ROOT, "icons")

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

# 颜色名 -> (色相°, 目标饱和度)
# 明度一律沿用原图像素，所以这组数字只需要管"颜色够不够正"。
COLORS = {
    "red":    (357.0, 0.95),
    "orange": (30.0,  1.00),
    "yellow": (45.0,  0.94),
    "green":  (145.0, 0.82),
    "blue":   (215.0, 0.90),
    "purple": (272.0, 0.75),
    "gray":   (0.0,   0.00),   # 特殊处理：中性灰
}

# 灰色不能靠降饱和 —— 那样只会变成白色。改成按亮度转中性灰再压暗一点。
GRAY_FACTOR = 0.72


def load_rgba(size):
    p = os.path.join(NATIVE, "%d.rgba" % size)
    if not os.path.exists(p):
        return None
    d = open(p, "rb").read()
    w, h = struct.unpack("<II", d[:8])
    return w, h, bytearray(d[8:])


def tint(rgba, hue_deg, sat_target):
    """只改 H/S，保留 V 和 alpha；灰色走单独的亮度映射。"""
    h = hue_deg / 360.0
    # 原生图标本体最饱和处约 0.78，据此做等比缩放，保持原有层次
    src_max = 0.78
    k = sat_target / src_max if src_max else 0.0
    gray = sat_target <= 0.001
    out = bytearray(len(rgba))
    for i in range(0, len(rgba), 4):
        a = rgba[i + 3]
        if a == 0:
            continue
        r, g, b = rgba[i], rgba[i + 1], rgba[i + 2]
        if a != 255:
            # 半透明边缘：还原到真色再算
            r = min(255, r * 255 // a)
            g = min(255, g * 255 // a)
            b = min(255, b * 255 // a)
        if gray:
            luma = int((0.299 * r + 0.587 * g + 0.114 * b) * GRAY_FACTOR + 0.5)
            out[i] = out[i + 1] = out[i + 2] = min(255, luma)
            out[i + 3] = a
            continue
        _, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
        if s < 0.08 or v < 0.04:
            # 灰影 / 纯黑边 / 白色高光：不动
            out[i], out[i + 1], out[i + 2], out[i + 3] = r, g, b, a
            continue
        ns = min(1.0, s * k)
        nr, ng, nb = colorsys.hsv_to_rgb(h, ns, v)
        out[i] = int(nr * 255 + 0.5)
        out[i + 1] = int(ng * 255 + 0.5)
        out[i + 2] = int(nb * 255 + 0.5)
        out[i + 3] = a
    return out


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


def write_ico(path, frames):
    """frames: [(size, png_bytes)]"""
    n = len(frames)
    header = struct.pack("<HHH", 0, 1, n)
    entries = b""
    offset = 6 + 16 * n
    body = b""
    for size, data in frames:
        w = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        body += data
    with open(path, "wb") as f:
        f.write(header + entries + body)


def contact_sheet(alpha_pairs, out_path, cell=128):
    """拼一张对比图，左半 128px，右半 32px（放大 4 倍）。"""
    cols = len(alpha_pairs)
    pad = 12
    W = cols * (cell + pad) + pad
    H = cell + pad * 2 + cell // 2 + 20
    canvas = bytearray(b"\x00\x00\x00\x00" * (W * H))

    def blit(src, sw, sh, dx, dy, scale):
        for y in range(sh):
            for x in range(sw):
                i = (y * sw + x) * 4
                a = src[i + 3]
                if a == 0:
                    continue
                for yy in range(scale):
                    for xx in range(scale):
                        tx, ty = dx + x * scale + xx, dy + y * scale + yy
                        if 0 <= tx < W and 0 <= ty < H:
                            j = (ty * W + tx) * 4
                            canvas[j], canvas[j + 1], canvas[j + 2], canvas[j + 3] = (
                                src[i], src[i + 1], src[i + 2], a)

    for idx, (size, rgba) in enumerate(alpha_pairs):
        x = pad + idx * (cell + pad)
        blit(rgba, size, size, x, pad, cell // size)
        small = alpha_pairs_small[size]
        blit(small, 32, 32, x, pad + cell + pad, 2)
    with open(out_path, "wb") as f:
        f.write(png_bytes(W, H, canvas))


alpha_pairs_small = {}


def main():
    os.makedirs(ICONS, exist_ok=True)

    # 先确认原生图标在手
    for s in SIZES:
        if not os.path.exists(os.path.join(NATIVE, "%d.rgba" % s)):
            print("缺少原生图标 %dpx，请先运行 tools/extract_native.py" % s)
            return 1

    for name, (hue, sat) in COLORS.items():
        frames = []
        for s in SIZES:
            w, h, rgba = load_rgba(s)
            out = tint(rgba, hue, sat)
            frames.append((w, png_bytes(w, h, out)))
        write_ico(os.path.join(ICONS, name + ".ico"), frames)
        print("生成 %-7s %d 个尺寸" % (name + ".ico", len(frames)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
