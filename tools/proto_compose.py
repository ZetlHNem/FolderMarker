# -*- coding: utf-8 -*-
"""原型：验证「文件夹内容缩略图 = imageres 5(底) + 内容图(中) + imageres 6(顶)」的合成结构。

做两版：
  1. 不染色（黄壳）→ 和 Windows 真实缩略图逐像素对比，验证几何是否正确
  2. 染成目标色 → 看彩色外壳 + 内容预览的成品效果
"""
import colorsys
import os
import sys

from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
LAYERS = os.path.join(ROOT, "build", "layers")

# 真实 Windows 缩略图量出来的内容区域（256px 时）
CONTENT_BOX = (17, 66, 231, 153)   # x0, y0, x1, y1

# 与主程序 TintFolder 一致的外壳判定参数
HUE_LOW, HUE_HIGH = 20.0 / 360.0, 82.0 / 360.0
MIN_SAT, MIN_VAL = 0.10, 0.06

TARGETS = {
    "red":    (357.0, 0.95),
    "green":  (145.0, 0.82),
    "blue":   (215.0, 0.90),
    "gray":   (0.0, 0.0),
}


def tint(im, target_hue_deg, sat_target):
    """把黄色外壳像素换成目标色（带 sMax 归一化 + 0.45 次方，和主程序一致）。"""
    px = im.load()
    w, h = im.size
    src = im.copy()
    spx = src.load()

    s_max = 0.0
    for y in range(h):
        for x in range(w):
            r, g, b, a = spx[x, y]
            if a < 16:
                continue
            hh, ss, vv = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if HUE_LOW <= hh <= HUE_HIGH and ss >= 0.25 and vv >= 0.35 and ss > s_max:
                s_max = ss
    if s_max < 0.30:
        s_max = 0.78

    hue = target_hue_deg / 360.0
    for y in range(h):
        for x in range(w):
            r, g, b, a = spx[x, y]
            if a < 16:
                continue
            hh, ss, vv = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if not (HUE_LOW <= hh <= HUE_HIGH and ss >= MIN_SAT and vv >= MIN_VAL):
                continue
            if sat_target <= 0.001:
                luma = (0.299 * r + 0.587 * g + 0.114 * b) * 0.72
                lv = int(min(255, luma + 0.5))
                px[x, y] = (lv, lv, lv, a)
                continue
            rel = min(1.0, ss / s_max)
            ns = min(1.0, sat_target * (rel ** 0.45))
            nr, ng, nb = colorsys.hsv_to_rgb(hue, ns, vv)
            px[x, y] = (int(nr * 255 + 0.5), int(ng * 255 + 0.5), int(nb * 255 + 0.5), a)
    return im


def compose(back, front, content, box=CONTENT_BOX):
    """back(底) + content(中，按 box 裁剪填充) + front(顶)。"""
    W, H = back.size
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0 + 1, y1 - y0 + 1

    # 内容按 cover 裁剪成 box 大小
    scale = max(bw / content.width, bh / content.height)
    cw, ch = int(content.width * scale + 0.5), int(content.height * scale + 0.5)
    c = content.resize((cw, ch), Image.LANCZOS)
    left, top = (cw - bw) // 2, (ch - bh) // 2
    c = c.crop((left, top, left + bw, top + bh))
    if c.mode != "RGBA":
        c = c.convert("RGBA")

    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(back)

    # 内容层，alpha 与背板相乘（裁成文件夹形状）
    cl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cl.paste(c, (x0, y0))
    cl.putalpha(ImageChops.multiply(cl.split()[3], back.split()[3]))
    canvas.alpha_composite(cl)

    canvas.alpha_composite(front)
    return canvas


def main():
    back = Image.open(os.path.join(LAYERS, "imageres_5.png")).convert("RGBA")
    front = Image.open(os.path.join(LAYERS, "imageres_6.png")).convert("RGBA")
    content = Image.open(os.path.join(LAYERS, "f15_first_file.png")).convert("RGBA")
    real = Image.open(r"C:\Users\EDY\AppData\Local\FolderMarker\custom\d967da52-base.png").convert("RGBA")

    # 1. 不染色版，和真实缩略图对比几何
    plain = compose(back, front, content)
    plain.save(os.path.join(LAYERS, "proto_plain.png"))

    # 逐像素差（只统计 shell+内容都算）
    diff = ImageChops.difference(plain.convert("RGB"), real.convert("RGB"))
    hist = diff.histogram()
    total = sum(hist)
    small = sum(hist[:16])   # 每通道差 <16 的像素
    print("与真实缩略图对比: 差值<16 的像素占 %.1f%%" % (small * 100.0 / total))

    # 2. 染色版（红/绿/蓝/灰）
    sheet = Image.new("RGBA", (256 * 4, 256), (255, 255, 255, 255))
    for i, name in enumerate(["red", "green", "blue", "gray"]):
        hue, sat = TARGETS[name]
        tb = tint(back.copy(), hue, sat)
        tf = tint(front.copy(), hue, sat)
        out = compose(tb, tf, content)
        out.save(os.path.join(LAYERS, "proto_%s.png" % name))
        sheet.alpha_composite(out, (256 * i, 0))
    sheet.save(os.path.join(LAYERS, "proto_colors.png"))
    print("已生成 proto_colors.png（红/绿/蓝/灰）")

    # 3. 对比图：真实 Windows 原版 vs 不染色合成 vs 红色合成
    cmp_img = Image.new("RGBA", (256 * 3, 256), (255, 255, 255, 255))
    cmp_img.alpha_composite(real, (0, 0))
    cmp_img.alpha_composite(plain, (256, 0))
    red = Image.open(os.path.join(LAYERS, "proto_red.png"))
    cmp_img.alpha_composite(red, (512, 0))
    cmp_img = cmp_img.resize((768, 256), Image.NEAREST)
    cmp_img.save(os.path.join(LAYERS, "proto_cmp.png"))
    print("已生成 proto_cmp.png（真实 / 合成黄 / 合成红）")


if __name__ == "__main__":
    main()
