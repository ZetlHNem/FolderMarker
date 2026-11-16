# -*- coding: utf-8 -*-
"""生成应用 / 安装包图标：圆角矩形底板 + 原生形状的彩色渐变文件夹。

两部分各取所长：
  · 文件夹 —— 不重绘。取系统原生文件夹图标（icons/yellow.ico）的 alpha 当形状，
    把它的明度归一化成 shading，再乘彩色渐变。形状和资源管理器里完全一致。
  · 底板   —— 矢量绘制的圆角矩形，竖向渐变 + 左上柔光。
没有投影 / 阴影。

渲染：4x4 超采样后按面积平均，每个尺寸独立出图。

用法：
    python gen_app_icon.py                          # 默认 rainbow + indigo
    python gen_app_icon.py rainbow ink build/a.ico
"""
import struct, zlib, os, math, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(HERE, ".."))
from preview_app_icon import decode_png  # noqa: E402

BASE = os.path.join(ROOT, "icons", "yellow.ico")
OUT = os.path.join(ROOT, "icons", "app.ico")
SIZES = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]
SS = 4                      # 每个像素 4x4 超采样

# 文件夹渐变：t 沿左上 -> 右下
GRADIENTS = {
    "rainbow": [
        (0.00, (1.00, 0.36, 0.42)),
        (0.15, (1.00, 0.58, 0.22)),
        (0.32, (1.00, 0.85, 0.30)),
        (0.48, (0.78, 0.93, 0.35)),
        (0.64, (0.30, 0.88, 0.72)),
        (0.82, (0.20, 0.68, 0.95)),
        (1.00, (0.55, 0.38, 0.98)),
    ],
    "candy4": [
        (0.00, (0.16, 0.72, 1.00)),
        (0.33, (0.42, 0.42, 1.00)),
        (0.66, (0.69, 0.36, 1.00)),
        (1.00, (1.00, 0.37, 0.66)),
    ],
    "sunset": [
        (0.00, (1.00, 0.30, 0.42)),
        (0.50, (1.00, 0.55, 0.24)),
        (1.00, (1.00, 0.82, 0.30)),
    ],
    "aurora": [
        (0.00, (0.16, 0.90, 0.60)),
        (0.50, (0.13, 0.64, 0.96)),
        (1.00, (0.36, 0.30, 0.95)),
    ],
}

# 底板配色
TILES = {
    "indigo": {"top": (0.34, 0.44, 0.98), "bot": (0.11, 0.17, 0.66)},
    "ink":    {"top": (0.245, 0.255, 0.30), "bot": (0.085, 0.088, 0.105)},
    "graphite": {"top": (0.46, 0.47, 0.50), "bot": (0.25, 0.26, 0.29)},
    "slate":  {"top": (0.97, 0.97, 0.99), "bot": (0.84, 0.87, 0.93)},
}

SHADE_LO, SHADE_HI = 0.93, 1.08
SOFTEN = 0.03
FOLDER_W = 0.625            # 文件夹宽度占底板的比例
FOLDER_CY = 0.505           # 文件夹垂直中心


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else (hi if v > hi else v)


def mix(a, b, t):
    t = clamp(t)
    return (a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def sd_round_rect(px, py, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hw, hh = (x1 - x0) / 2.0 - r, (y1 - y0) / 2.0 - r
    dx = abs(px - cx) - hw
    dy = abs(py - cy) - hh
    ax, ay = max(dx, 0.0), max(dy, 0.0)
    return math.hypot(ax, ay) + min(max(dx, dy), 0.0) - r


def over(dst, src, a):
    if a <= 0.0:
        return dst
    if a >= 1.0:
        return src
    return (src[0] * a + dst[0] * (1 - a),
            src[1] * a + dst[1] * (1 - a),
            src[2] * a + dst[2] * (1 - a))


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
    """原生帧换色：形状 / alpha / 明暗层次全部沿用原图"""
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
    vals = sorted(lum[i] for i in solid)
    lo = vals[int(len(vals) * 0.02)]
    hi = vals[int(len(vals) * 0.98)]
    if hi - lo < 1e-6:
        hi = lo + 1e-6
    out = bytearray(len(rgba))
    for i in solid:
        x = (i % w) / max(1, w - 1)
        y = (i // w) / max(1, h - 1)
        base = gradient_at(stops, 0.56 * x + 0.44 * y)
        k = clamp((lum[i] - lo) / (hi - lo))
        shade = SHADE_LO + (SHADE_HI - SHADE_LO) * k
        c = mix((base[0] * shade, base[1] * shade, base[2] * shade), (1.0, 1.0, 1.0), SOFTEN)
        o = i * 4
        out[o] = max(0, min(255, int(c[0] * 255 + 0.5)))
        out[o + 1] = max(0, min(255, int(c[1] * 255 + 0.5)))
        out[o + 2] = max(0, min(255, int(c[2] * 255 + 0.5)))
        out[o + 3] = rgba[o + 3]
    return bytes(out)


def folder_sprite(frames, stops):
    """取最大那帧上色，返回 (w, h, rgba, bbox)"""
    src = frames[max(frames)]
    w, h, rgba = src
    px = colorize(w, h, rgba, stops)
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[(y * w + x) * 4 + 3] > 4:
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y
    return w, h, px, (minx, miny, maxx, maxy)


def sample_sprite(sprite, u, v):
    """u,v in 0..1 取文件夹颜色，双线性"""
    w, h, px, (x0, y0, x1, y1) = sprite
    fx = x0 + u * (x1 - x0)
    fy = y0 + v * (y1 - y0)
    fx = min(max(fx, 0.0), w - 1.001)
    fy = min(max(fy, 0.0), h - 1.001)
    ix, iy = int(fx), int(fy)
    tx, ty = fx - ix, fy - iy
    out = [0.0, 0.0, 0.0, 0.0]
    for dy in (0, 1):
        for dx in (0, 1):
            o = ((iy + dy) * w + (ix + dx)) * 4
            wgt = (tx if dx else 1 - tx) * (ty if dy else 1 - ty)
            a = px[o + 3] / 255.0
            for k in range(3):
                out[k] += px[o + k] / 255.0 * a * wgt
            out[3] += a * wgt
    if out[3] <= 0.0005:
        return (0.0, 0.0, 0.0), 0.0
    # 预乘还原成 straight alpha
    return (out[0] / out[3], out[1] / out[3], out[2] / out[3]), min(1.0, out[3])


def scene(x, y, size, sprite, tile):
    # 底板几何：大尺寸铺满画布、22.5% 圆角；小尺寸（≤32px）留一圈透明边距
    # 并放大相对圆角 —— 否则 16/20/24px 在任务栏、右键菜单里就是一个方块，
    # 完全看不出圆角（16px 上 22.5% 半径只有 3.6px，视觉上等于直边）。
    if size <= 32:
        m = 0.06                       # 透明边距（相对画布）
        rad = 0.30 * (1.0 - 2.0 * m)   # 圆角占底板边长的 30%
    else:
        m = 0.0
        rad = 0.225

    if sd_round_rect(x, y, m, m, 1.0 - m, 1.0 - m, rad) > 0.0:
        return (0.0, 0.0, 0.0, 0.0)

    w = 1.0 - 2.0 * m                  # 底板占画布的比例
    tx = (x - m) / w                   # 底板内归一化坐标
    ty = (y - m) / w

    rgb = mix(tile["top"], tile["bot"], ty ** 0.92)
    glow = clamp(1.0 - math.hypot(tx - 0.26, ty - 0.14) / 0.82) ** 2
    rgb = mix(rgb, (1.0, 1.0, 1.0), 0.13 * glow)

    fw = FOLDER_W * (1.06 if size < 24 else 1.0) * w
    sw_, sh_, _, (sx0, sy0, sx1, sy1) = sprite
    fh = fw * (sy1 - sy0) / (sx1 - sx0)
    fx0 = 0.5 - fw / 2
    fy0 = m + w * FOLDER_CY - fh / 2
    u = (x - fx0) / fw
    v = (y - fy0) / fh
    if 0.0 <= u <= 1.0 and 0.0 <= v <= 1.0:
        c, a = sample_sprite(sprite, u, v)
        if a > 0.0:
            rgb = over(rgb, c, a)
    return (rgb[0], rgb[1], rgb[2], 1.0)


def render(size, sprite, tile):
    px = bytearray(size * size * 4)
    step = 1.0 / SS
    off = [step * (k + 0.5) for k in range(SS)]
    cnt = SS * SS
    for iy in range(size):
        for ix in range(size):
            ar = ag = ab = aa = 0.0
            for sy in off:
                for sx in off:
                    x = (ix + sx) / size
                    y = (iy + sy) / size
                    r, g, b, a = scene(x, y, size, sprite, tile)
                    ar += r
                    ag += g
                    ab += b
                    aa += a
            if aa <= 0.0:
                continue
            o = (iy * size + ix) * 4
            px[o] = max(0, min(255, int(ar / aa * 255 + 0.5)))
            px[o + 1] = max(0, min(255, int(ag / aa * 255 + 0.5)))
            px[o + 2] = max(0, min(255, int(ab / aa * 255 + 0.5)))
            px[o + 3] = max(0, min(255, int(aa / cnt * 255 + 0.5)))
    return bytes(px)


def png_chunk(typ, data):
    return (struct.pack(">I", len(data)) + typ + data +
            struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff))


def to_png(size, rgba):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        raw += rgba[y * size * 4:(y + 1) * size * 4]
    png = b"\x89PNG\r\n\x1a\n"
    png += png_chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
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


def build(style, tile_name, path):
    sprite = folder_sprite(read_ico(BASE), GRADIENTS[style])
    tile = TILES[tile_name]
    pngs = [to_png(s, render(s, sprite, tile)) for s in SIZES]
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "wb") as f:
        f.write(to_ico(pngs))
    print("生成 %s (%d bytes, %d 帧, 配色 %s / 底板 %s)"
          % (path, os.path.getsize(path), len(pngs), style, tile_name))


if __name__ == "__main__":
    style = sys.argv[1] if len(sys.argv) > 1 else "rainbow"
    tile_name = sys.argv[2] if len(sys.argv) > 2 else "indigo"
    path = sys.argv[3] if len(sys.argv) > 3 else OUT
    build(style, tile_name, path)
