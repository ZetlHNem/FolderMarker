# -*- coding: utf-8 -*-
"""生成右键菜单「标记符号」子菜单的 5 个图标（Fluent UI System Icons，MIT）。

    icons/badge_none.ico   无符号   subtract_circle（灰）
    icons/badge_star.ico   ★ 重要   star（琥珀）
    icons/badge_check.ico  ✓ 完成   checkmark（绿）
    icons/badge_alert.ico  ! 待办   alert（红）
    icons/badge_dot.ico    ● 进行中 circle（蓝）

和 pick/undo/clear 一样走 resvg 矢量渲染（16/20/24/32/40/48 六帧）。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import gen_app_icon as G
import resvg_py
from PIL import Image

SRC = os.path.join(ROOT, "build", "icon_src", "fluent")
OUT_DIR = os.path.join(ROOT, "icons")
SIZES = [48, 40, 32, 24, 20, 16]

SPECS = [
    ("badge_none",  "subtract_circle_24_filled.svg", "#6B7280"),   # 灰
    ("badge_star",  "star_24_filled.svg",            "#E8A400"),   # 琥珀
    ("badge_check", "checkmark_24_filled.svg",       "#2E9E44"),   # 绿
    ("badge_alert", "alert_24_filled.svg",           "#D64545"),   # 红
    ("badge_dot",   "circle_24_filled.svg",          "#0067C0"),   # 蓝
]


def render(name, tint, size):
    with open(os.path.join(SRC, name), "r", encoding="utf-8") as f:
        svg = f.read()
    svg = svg.replace("<svg ", '<svg fill="%s" ' % tint, 1)
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size))
    return Image.open(io.BytesIO(png)).convert("RGBA").tobytes()


def main():
    for key, svg, tint in SPECS:
        pngs = [G.to_png(s, render(svg, tint, s)) for s in SIZES]
        out = os.path.join(OUT_DIR, key + ".ico")
        with open(out, "wb") as f:
            f.write(G.to_ico(pngs))
        print("生成 %s (%d bytes, %d 帧)" % (out, os.path.getsize(out), len(pngs)))


if __name__ == "__main__":
    main()
