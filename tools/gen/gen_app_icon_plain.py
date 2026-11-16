# -*- coding: utf-8 -*-
"""生成应用 / 安装包图标（无底板版）：原生文件夹形状 + 彩色渐变。

注意：正式图标用的是带圆角底板的 gen_app_icon.py，这个脚本留作备选。
默认输出到 build/plain_app.ico，不会覆盖 icons/app.ico。

做法不是重绘，而是「换色」：
  1. 取系统原生文件夹图标（icons/yellow.ico，各尺寸都是 shell 渲染出来的位图）
  2. 用它的 alpha 当形状蒙版 —— 所以标签页、圆角、边缘和资源管理器里一模一样
  3. 把原图的明度归一化成一层 shading（顶面高光、标签页暗一档、底边压暗都保留）
  4. 用对角彩色渐变乘上 shading，得到新颜色
结果没有底板、没有投影，就是一个渐变色的文件夹。

用法：
    python gen_app_icon.py                    # 默认配色输出 icons/app.ico
    python gen_app_icon.py rainbow build/a.ico
"""
import struct, zlib, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(HERE, ".."))
from preview_app_icon import decode_png  # noqa: E402

BASE = os.path.join(ROOT, "icons", "yellow.ico")
OUT = os.path.join(ROOT, "build", "plain_app.ico")

# 渐变沿左上 -> 右下，stops 里的 t 是归一化位置
GRADIENTS = {
    # 彩虹：玫红 → 橙 → 琥珀 → 绿 → 青 → 紫
    "rainbow": [
        (0.00, (1.00, 0.36, 0.42)),
        (0.15, (1.00, 0.58, 0.22)),
        (0.32, (1.00, 0.85, 0.30)),
        (0.48, (0.78, 0.93, 0.35)),
        (0.64, (0.30, 0.88, 0.72)),
        (0.82, (0.20, 0.68, 0.95)),
        (1.00, (0.55, 0.38, 0.98)),
    ],
    # 糖果（四色）：天蓝 → 蓝紫 → 紫 → 粉，跨度够彩色但同属冷调，过渡不发浊
    "candy4": [
        (0.00, (0.16, 0.72, 1.00)),
        (0.33, (0.42, 0.42, 1.00)),
        (0.66, (0.69, 0.36, 1.00)),
        (1.00, (1.00, 0.37, 0.66)),
    ],
    # 糖果：蓝 → 紫 → 粉
    "candy": [
        (0.00, (0.31, 0.67, 1.00)),
        (0.50, (0.56, 0.42, 1.00)),
        (1.00, (1.00, 0.44, 0.85)),
    ],
    # 日落：珊瑚红 → 橙 → 金
    "sunset": [
        (0.00, (1.00, 0.30, 0.42)),
        (0.50, (1.00, 0.55, 0.24)),
        (1.00, (1.00, 0.82, 0.30)),
    ],
    # 极光：青绿 → 蓝 → 靛
    "aurora": [
        (0.00, (0.16, 0.90, 0.60)),
        (0.50, (0.13, 0.64, 0.96)),
        (1.00, (0.36, 0.30, 0.95)),
    ],
}

SHADE_LO, SHADE_HI = 0.93, 1.08   # 明度归一化后映射成的亮度乘子区间
SOFTEN = 0.03                      # 往白里混一点，去掉荧光感
SHADE_GAMMA = 1.0


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else (hi if v > hi else v)


def mix(a, b, t):
    t = clamp(t)
    return (a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def read_ico(path):
    data = open(path, "rb").read()
    n = struct.unpack("<HHH", data[:6])[2]
    frames = {}
    for i in range(n):
        off = 6 + 16 * i
        w, h, _, _, _, _, size, offset = struct.unpack("<BBBBHHII", data[off:off + 16])
        png = data[offset:offset + size]
        if png[:8] != b"\x89PNG\r\n\x1a\n":
            continue
        iw, ih, rgba = decode_png(png)
        frames[iw] = (iw, ih, rgba)
    return frames


def gradient_at(stops, t):
    t = clamp(t)
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        if t <= t1:
            k = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
            return mix(c0, c1, k)
    return stops[-1][1]


def colorize(w, h, rgba, stops):
    """把一张原生帧换成渐变配色，形状 / alpha / 明暗层次全部沿用原图"""
    n = w * h
    lum = [0.0] * n
    solid = []
    for i in range(n):
        if rgba[i * 4 + 3] == 0:
            continue
        r, g, b = rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]
        lum[i] = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
        solid.append(i)

    if not solid:
        return bytes(rgba)

    # 按本帧自己的明度分布归一化，各尺寸的明暗层次都能拉满
    vals = sorted(lum[i] for i in solid)
    lo = vals[int(len(vals) * 0.02)]
    hi = vals[int(len(vals) * 0.98)]
    if hi - lo < 1e-6:
        hi = lo + 1e-6

    out = bytearray(len(rgba))
    for i in solid:
        x = (i % w) / max(1, w - 1)
        y = (i // w) / max(1, h - 1)
        t = 0.56 * x + 0.44 * y          # 对角渐变，顺着光从左上来
        base = gradient_at(stops, t)
        k = clamp((lum[i] - lo) / (hi - lo)) ** SHADE_GAMMA
        shade = SHADE_LO + (SHADE_HI - SHADE_LO) * k
        o = i * 4
        c = (base[0] * shade, base[1] * shade, base[2] * shade)
        c = mix(c, (1.0, 1.0, 1.0), SOFTEN)
        out[o] = max(0, min(255, int(c[0] * 255 + 0.5)))
        out[o + 1] = max(0, min(255, int(c[1] * 255 + 0.5)))
        out[o + 2] = max(0, min(255, int(c[2] * 255 + 0.5)))
        out[o + 3] = rgba[o + 3]
    return bytes(out)


def png_chunk(typ, data):
    return (struct.pack(">I", len(data)) + typ + data +
            struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff))


def to_png(w, h, rgba):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgba[y * w * 4:(y + 1) * w * 4]
    png = b"\x89PNG\r\n\x1a\n"
    png += png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += png_chunk(b"IEND", b"")
    return png


def to_ico(pngs):
    n = len(pngs)
    header = struct.pack("<HHH", 0, 1, n)
    entries, images = b"", b""
    offset = 6 + 16 * n
    for png in pngs:
        w, h = struct.unpack(">II", png[16:24])
        entries += struct.pack("<BBBBHHII", w if w < 256 else 0, h if h < 256 else 0,
                               0, 0, 1, 32, len(png), offset)
        images += png
        offset += len(png)
    return header + entries + images


def build(style, path):
    stops = GRADIENTS[style]
    frames = read_ico(BASE)
    sizes = sorted(frames, reverse=True)
    pngs = []
    for s in sizes:
        w, h, rgba = frames[s]
        pngs.append(to_png(w, h, colorize(w, h, rgba, stops)))
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "wb") as f:
        f.write(to_ico(pngs))
    print("生成 %s (%d bytes, %d 帧, 配色 %s)"
          % (path, os.path.getsize(path), len(pngs), style))


if __name__ == "__main__":
    style = sys.argv[1] if len(sys.argv) > 1 else "rainbow"
    path = sys.argv[2] if len(sys.argv) > 2 else OUT
    build(style, path)
