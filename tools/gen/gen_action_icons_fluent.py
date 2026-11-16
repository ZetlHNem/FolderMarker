# -*- coding: utf-8 -*-
"""生成右键菜单三个动作的专属图标（Fluent UI System Icons，MIT 协议）。

    icons/pick.ico   自定义颜色...  —— paint_brush_color（Fluent 官方多色版）
    icons/undo.ico   恢复上次颜色  —— arrow_undo（蓝色 #0067C0）
    icons/clear.ico  清除标记      —— eraser（中性灰 #555B66）

来源 https://www.npmjs.com/package/@fluentui/svg-icons （MIT License），
SVG 原文件缓存在 build/icon_src/fluent/。mono 图标在根节点注入 fill 染色。
SVG 是矢量，每个目标尺寸（48/40/32/24/20/16）直接由对应 viewBox 的 SVG
用 resvg 独立渲染：小尺寸用 16 viewBox（笔画比例专门为小尺寸设计），
大尺寸用 24 viewBox，40 由 24 渲染。

用法：
    python gen_action_icons_fluent.py            # 生成三个 ico
    python gen_action_icons_fluent.py preview    # 额外输出预览图
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import gen_app_icon as G
import resvg_py
from PIL import Image

SRC = os.path.join(ROOT, "build", "icon_src", "fluent")
OUT_DIR = os.path.join(ROOT, "icons")
SIZES = [48, 40, 32, 24, 20, 16]

BLUE = "#0067C0"      # Win11 强调蓝
GRAY = "#555B66"      # 中性灰

# 每个动作：{尺寸段: (svg 文件, 染色, None)}
PLAN = {
    "pick":  {16: ("paint_brush_16_color.svg", None),
              20: ("paint_brush_16_color.svg", None),
              24: ("paint_brush_24_color.svg", None),
              32: ("paint_brush_24_color.svg", None),
              40: ("paint_brush_24_color.svg", None),
              48: ("paint_brush_24_color.svg", None)},
    "undo":  {16: ("arrow_undo_16_filled.svg", BLUE),
              20: ("arrow_undo_16_filled.svg", BLUE),
              24: ("arrow_undo_24_filled.svg", BLUE),
              32: ("arrow_undo_24_filled.svg", BLUE),
              40: ("arrow_undo_24_filled.svg", BLUE),
              48: ("arrow_undo_24_filled.svg", BLUE)},
    "clear": {s: ("eraser_24_filled.svg", GRAY) for s in SIZES},
}


def load_svg(name, tint):
    with open(os.path.join(SRC, name), "r", encoding="utf-8") as f:
        svg = f.read()
    if tint:
        svg = svg.replace("<svg ", '<svg fill="%s" ' % tint, 1)
    return svg


def render(size, name, tint):
    svg = load_svg(name, tint)
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size))
    im = Image.open(__import__("io").BytesIO(png)).convert("RGBA")
    return im.tobytes()


def build_one(kind, path):
    plan = PLAN[kind]
    pngs = []
    for s in SIZES:
        name, tint = plan[s]
        pngs.append(G.to_png(s, render(s, name, tint)))
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "wb") as f:
        f.write(G.to_ico(pngs))
    print("生成 %s (%d bytes, %d 帧)" % (path, os.path.getsize(path), len(pngs)))


def write_preview():
    """64px(48帧放大) + 24px + 16px 原大，棋盘底"""
    names = ["pick", "undo", "clear"]
    zoom = 2
    cell = 48 * zoom
    rows = [(48, zoom), (24, 1), (16, 1)]
    W = cell * 3
    H = sum(48 * z if s == 48 else s for s, z in rows) + 8 * (len(rows) - 1)
    canvas = Image.new("RGBA", (W, H))
    px = canvas.load()
    for y in range(H):
        for x in range(W):
            px[x, y] = (255, 255, 255, 255) if ((x // 8) + (y // 8)) % 2 == 0 else (235, 235, 235, 255)
    y0 = 0
    for size, z in rows:
        h = size * z
        for i, kind in enumerate(names):
            name, tint = PLAN[kind][size]
            im = Image.frombytes("RGBA", (size, size), render(size, name, tint))
            if z != 1:
                im = im.resize((size * z, size * z), Image.NEAREST)
            canvas.alpha_composite(im, (i * cell + (cell - h) // 2, y0))
        y0 += h + 8
    out = os.path.join(ROOT, "build", "action_preview.png")
    canvas.convert("RGB").save(out)
    print("预览 %s" % out)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "preview":
        write_preview()
    else:
        build_one("pick", os.path.join(OUT_DIR, "pick.ico"))
        build_one("undo", os.path.join(OUT_DIR, "undo.ico"))
        build_one("clear", os.path.join(OUT_DIR, "clear.ico"))
