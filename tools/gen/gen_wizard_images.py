# -*- coding: utf-8 -*-
"""生成 Inno Setup 向导用的现代风格侧栏图（BMP，纯标准库）"""
import os, struct, math

BUILD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "build")
S = os.path.dirname(os.path.abspath(__file__))

# macOS 7 色
PALETTE = [
    (255, 59, 48), (255, 149, 0), (255, 204, 0), (52, 199, 89),
    (0, 122, 255), (175, 82, 222), (142, 142, 147),
]


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def blend(dst, src, alpha):
    return tuple(int(dst[i] * (1 - alpha) + src[i] * alpha) for i in range(3))


def write_bmp(path, w, h, pixels):
    row_size = (w * 3 + 3) & ~3
    pad = row_size - w * 3
    data = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b = pixels[y][x]
            data += bytes((b, g, r))
        data += b"\x00" * pad
    file_size = 54 + len(data)
    header = b"BM" + struct.pack("<IHHI", file_size, 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, w, h, 1, 24, 0, len(data), 2835, 2835, 0, 0)
    with open(path, "wb") as f:
        f.write(header + info + bytes(data))


def make_canvas(w, h, top, bottom):
    px = []
    for y in range(h):
        t = y / max(1, h - 1)
        base = lerp(top, bottom, t)
        px.append([base for _ in range(w)])
    return px


def draw_disc(px, w, h, cx, cy, r, color, alpha=1.0, feather=1.4):
    """抗锯齿实心圆"""
    x0, x1 = int(cx - r - 2), int(cx + r + 2)
    y0, y1 = int(cy - r - 2), int(cy + r + 2)
    for y in range(max(0, y0), min(h, y1 + 1)):
        for x in range(max(0, x0), min(w, x1 + 1)):
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            if d <= r - feather:
                cov = 1.0
            elif d >= r + feather:
                continue
            else:
                cov = (r + feather - d) / (2 * feather)
            a = alpha * cov
            px[y][x] = blend(px[y][x], color, a)


def draw_ring(px, w, h, cx, cy, radius, dot_r, colors, start_deg=-90):
    n = len(colors)
    for i, c in enumerate(colors):
        ang = math.radians(start_deg + 360.0 * i / n)
        x = cx + radius * math.cos(ang)
        y = cy + radius * math.sin(ang)
        # 白色描边
        draw_disc(px, w, h, x, y, dot_r + max(1.6, dot_r * 0.26), (255, 255, 255), 0.95)
        draw_disc(px, w, h, x, y, dot_r, c)
        # 高光
        draw_disc(px, w, h, x - dot_r * 0.3, y - dot_r * 0.3, dot_r * 0.32, (255, 255, 255), 0.5)


def build_large():
    w, h = 164, 314
    px = make_canvas(w, h, (255, 255, 255), (238, 244, 255))
    cx, cy = w / 2.0, h / 2.0 - 6
    # 柔和光晕
    draw_disc(px, w, h, cx, cy, 74, (226, 236, 252), 0.75, feather=18)
    draw_ring(px, w, h, cx, cy, 38, 13.5, PALETTE)
    # 中心小徽标
    draw_disc(px, w, h, cx, cy, 13, (255, 255, 255), 0.9)
    draw_disc(px, w, h, cx, cy, 11, (247, 214, 122))
    write_bmp(os.path.join(BUILD, "wizard_large.bmp"), w, h, px)
    print("wizard_large.bmp 164x314")


def build_small():
    w, h = 55, 55
    px = make_canvas(w, h, (255, 255, 255), (255, 255, 255))
    draw_ring(px, w, h, w / 2.0, h / 2.0, 13.5, 5.4, PALETTE)
    write_bmp(os.path.join(BUILD, "wizard_small.bmp"), w, h, px)
    print("wizard_small.bmp 55x55")


if __name__ == "__main__":
    build_large()
    build_small()
