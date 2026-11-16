# -*- coding: utf-8 -*-
"""生成右键菜单三个动作的专属图标（简约纯图形版）：

    icons/pick.ico   自定义颜色...  —— 彩虹色轮
    icons/undo.ico   恢复上次颜色  —— 蓝色回退箭头 ↺
    icons/clear.ico  清除标记      —— 红色 ×

透明底、单个符号铺满画布，和资源管理器原生菜单图标的简约风格一致。
4x4 超采样，48/40/32/24/20/16 六帧。

用法：
    python gen_action_icons.py            # 生成三个 ico
    python gen_action_icons.py preview    # 额外输出 build/action_preview.png
"""
import os, sys, math

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import gen_app_icon as G

SIZES = [48, 40, 32, 24, 20, 16]
SS = 4                      # 每像素 4x4 超采样
OUT_DIR = os.path.join(ROOT, "icons")

BLUE = (0.16, 0.44, 0.90)
RED = (0.86, 0.22, 0.20)


def hsv(h, s, v):
    h = h % 1.0
    i = int(h * 6.0)
    f = h * 6.0 - i
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    i %= 6
    return [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]


def mix(a, b, t):
    return tuple(a[k] + (b[k] - a[k]) * t for k in range(3))


# ---------- 符号几何（画布归一化坐标 u,v ∈ [-1,1]，y 向下） ----------

def glyph_pick(u, v, size=48):
    """色轮：实心圆，色相随角度旋转，中心一点白增加通透感"""
    d = math.hypot(u, v)
    if d > 0.88:
        return None
    ang = math.atan2(v, u) / (2.0 * math.pi)
    c = hsv(ang, 0.78, 0.95)
    if d < 0.16:                      # 中心小白点，避免糊成一坨
        c = mix(c, (1.0, 1.0, 1.0), 0.85)
    return c


def _in_arc(u, v, r, hw, a0, a1):
    d = math.hypot(u, v)
    if abs(d - r) > hw:
        return False
    ang = math.degrees(math.atan2(v, u))
    x = (ang - a0) % 360.0
    span = (a1 - a0) % 360.0
    return x <= span


def _in_tri(px, py, tri):
    (x0, y0), (x1, y1), (x2, y2) = tri
    d1 = (px - x1) * (y0 - y1) - (x0 - x1) * (py - y1)
    d2 = (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2)
    d3 = (px - x0) * (y2 - y0) - (x2 - x0) * (py - y0)
    neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    return not (neg and pos)


def _dist_seg(px, py, ax, ay, bx, by):
    """点到线段距离"""
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    t = (wx * vx + wy * vy) / (vx * vx + vy * vy)
    t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
    dx, dy = wx - vx * t, wy - vy * t
    return math.hypot(dx, dy)


def glyph_undo(u, v, size=48):
    """标准撤销字形 ↶：左向箭头 + 水平杆 + 圆角 90° 拐弯 + 竖杆上翘。
    拐弯半径紧凑（0.32）、主体由直线段构成，16px 下不糊。"""
    hw = 0.12 if size <= 20 else 0.15          # 笔画半宽
    y0 = 0.28                                  # 杆的纵坐标
    # 水平杆：从箭头基部 -0.50 到拐弯点 0.30
    d = _dist_seg(u, v, -0.50, y0, 0.30, y0)
    # 圆角拐弯：圆心 (0.30, y0-0.32) 半径 0.32，从 (0.30,y0) 弯到 (0.62,y0-0.32)
    du, dv = u - 0.30, v - (y0 - 0.32)
    if du >= 0.0 and dv >= 0.0:
        d = min(d, abs(math.hypot(du, dv) - 0.32))
    # 竖杆：x=0.62，从拐弯终点向上到 -0.50
    d = min(d, _dist_seg(u, v, 0.62, y0 - 0.32, 0.62, -0.50))
    if d <= hw:
        return BLUE
    # 箭头头部：指左三角，尖端 (-0.86,y0)，基部 (-0.50,y0±0.32)
    if _in_tri(u, v, ((-0.86, y0), (-0.50, y0 - 0.32), (-0.50, y0 + 0.32))):
        return BLUE
    return None


def glyph_clear(u, v, size=48):
    """红色 ×：两条对角粗线"""
    if max(abs(u), abs(v)) > 0.68:
        return None
    d1 = abs(u - v) / math.sqrt(2.0)
    d2 = abs(u + v) / math.sqrt(2.0)
    if d1 < 0.26 or d2 < 0.26:
        return RED
    return None


GLYPHS = {"pick": glyph_pick, "undo": glyph_undo, "clear": glyph_clear}


def render(size, kind):
    glyph = GLYPHS[kind]
    px = bytearray(size * size * 4)
    off = [(k + 0.5) / SS for k in range(SS)]
    cnt = SS * SS
    for iy in range(size):
        for ix in range(size):
            ar = ag = ab = aa = 0.0
            for sy in off:
                for sx in off:
                    u = (ix + sx) / size * 2.0 - 1.0
                    v = (iy + sy) / size * 2.0 - 1.0
                    c = glyph(u, v, size)
                    if c is None:
                        continue
                    ar += c[0]
                    ag += c[1]
                    ab += c[2]
                    aa += 1.0
            if aa <= 0.0:
                continue
            o = (iy * size + ix) * 4
            px[o] = max(0, min(255, int(ar / aa * 255 + 0.5)))
            px[o + 1] = max(0, min(255, int(ag / aa * 255 + 0.5)))
            px[o + 2] = max(0, min(255, int(ab / aa * 255 + 0.5)))
            px[o + 3] = max(0, min(255, int(aa / cnt * 255 + 0.5)))
    return bytes(px)


def build_one(kind, path):
    pngs = [G.to_png(s, render(s, kind)) for s in SIZES]
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "wb") as f:
        f.write(G.to_ico(pngs))
    print("生成 %s (%d bytes, %d 帧)" % (path, os.path.getsize(path), len(pngs)))


def write_preview():
    """64px 大图 + 16px 原大，棋盘格底，横向拼接"""
    big, small = 64, 16
    rows_big = [render(big, k) for k in ("pick", "undo", "clear")]
    rows_small = [render(small, k) for k in ("pick", "undo", "clear")]
    W = max(big * 3, small * 3)
    H = big + small + 6
    canvas = bytearray(W * H * 4)
    for i, data in enumerate(rows_big):
        for y in range(big):
            for x in range(big):
                do = (y * W + i * big + x) * 4
                canvas[do:do + 4] = data[(y * big + x) * 4:(y * big + x) * 4 + 4]
    for i, data in enumerate(rows_small):
        for y in range(small):
            for x in range(small):
                do = ((big + 6 + y) * W + i * small + x) * 4
                canvas[do:do + 4] = data[(y * small + x) * 4:(y * small + x) * 4 + 4]
    for y in range(H):
        for x in range(W):
            o = (y * W + x) * 4
            a = canvas[o + 3] / 255.0
            bg = 1.0 if ((x // 4 + y // 4) % 2 == 0) else 0.82
            for k in range(3):
                canvas[o + k] = int((canvas[o + k] / 255.0 * a + bg * (1 - a)) * 255 + 0.5)
            canvas[o + 3] = 255
    out = os.path.join(ROOT, "build", "action_preview.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "wb") as f:
        f.write(G.to_png(W, bytes(canvas)))
    print("预览 %s" % out)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "preview":
        write_preview()
    else:
        build_one("pick", os.path.join(OUT_DIR, "pick.ico"))
        build_one("undo", os.path.join(OUT_DIR, "undo.ico"))
        build_one("clear", os.path.join(OUT_DIR, "clear.ico"))
