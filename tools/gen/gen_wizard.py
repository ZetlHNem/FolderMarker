# -*- coding: utf-8 -*-
"""生成安装向导左侧品牌图（164x314）与右上小图（55x55）。

视觉语言与 app 图标一致：深色圆角底板质感 + 原生形状的彩色渐变文件夹。
区别只在于画布是竖版，因此底板铺满整幅（不画圆角），文件夹下方补标题与色点。
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from gen_app_icon import read_ico, folder_sprite, GRADIENTS  # noqa: E402

W, H = 164, 314
SS = 4                      # 超采样倍数
OUT_LARGE = os.path.join(ROOT, "packaging", "wizard_large.bmp")
OUT_SMALL = os.path.join(ROOT, "packaging", "wizard_small.bmp")

FONT_LIGHT = r"C:\Windows\Fonts\msyhl.ttc"
FONT_REG = r"C:\Windows\Fonts\msyh.ttc"

TOP = (0x1B, 0x1E, 0x26)
BOT = (0x0A, 0x0B, 0x0F)

DOTS = [
    ("red",    (0xE8, 0x4C, 0x5B)),
    ("orange", (0xF2, 0x8C, 0x3C)),
    ("yellow", (0xF4, 0xC5, 0x3D)),
    ("green",  (0x4C, 0xC0, 0x7A)),
    ("blue",   (0x3E, 0x8F, 0xE0)),
    ("purple", (0x95, 0x6B, 0xDE)),
    ("gray",   (0x8A, 0x90, 0x9C)),
]


def vgrad(w, h, top, bot):
    """竖向渐变底"""
    strip = Image.new("RGB", (1, h))
    px = strip.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = (int(top[0] + (bot[0] - top[0]) * t),
                    int(top[1] + (bot[1] - top[1]) * t),
                    int(top[2] + (bot[2] - top[2]) * t))
    return strip.resize((w, h), Image.BILINEAR)


def add_glow(img, cx, cy, radius, strength):
    """左上柔光：居中径向渐变，screen 叠加"""
    w, h = img.size
    radial = Image.radial_gradient("L").resize((radius * 2, radius * 2), Image.BILINEAR)
    mask = Image.new("L", (w, h), 0)
    mask.paste(Image.eval(radial, lambda v: 255 - v), (int(cx - radius), int(cy - radius)))
    light = Image.new("RGB", (w, h), (255, 255, 255))
    return Image.composite(Image.blend(img, light, strength), img, mask)


def folder_image(width):
    """原生文件夹形状 + 彩色渐变，按 bbox 裁好并缩放到指定宽度"""
    w, h, px, bbox = folder_sprite(read_ico(os.path.join(ROOT, "icons", "yellow.ico")),
                                   GRADIENTS["rainbow"])
    x0, y0, x1, y1 = bbox
    sprite = Image.frombytes("RGBA", (w, h), bytes(px)).crop((x0, y0, x1 + 1, y1 + 1))
    k = width / sprite.width
    return sprite.resize((max(1, int(width)), max(1, int(sprite.height * k))), Image.LANCZOS)


def build_large():
    w, h = W * SS, H * SS
    img = vgrad(w, h, TOP, BOT)
    img = add_glow(img, w * 0.5, h * 0.06, int(w * 0.95), 0.10)
    img = add_glow(img, w * 0.5, h * 0.50, int(w * 0.75), 0.055)

    # 文件夹：略偏上，给下方文字留位
    fw = int(w * 0.615)
    sprite = folder_image(fw)
    fx = (w - sprite.width) // 2
    fy = int(h * 0.420) - sprite.height // 2
    img.paste(sprite, (fx, fy), sprite)

    d = ImageDraw.Draw(img)

    # 主标题
    f1 = ImageFont.truetype(FONT_REG, int(14.5 * SS))
    t1 = "文件夹标记"
    bb = d.textbbox((0, 0), t1, font=f1)
    d.text(((w - (bb[2] - bb[0])) / 2 - bb[0], int(h * 0.630)), t1, font=f1, fill=(0xF5, 0xF7, 0xFB))

    # 副标题
    f2 = ImageFont.truetype(FONT_REG, int(8.6 * SS))
    t2 = "右键文件夹 · 7 种颜色"
    bb = d.textbbox((0, 0), t2, font=f2)
    d.text(((w - (bb[2] - bb[0])) / 2 - bb[0], int(h * 0.726)), t2, font=f2, fill=(0x9A, 0xA3, 0xB6))

    # 七色圆点
    r = int(2.8 * SS)
    gap = int(11.6 * SS)
    total = gap * len(DOTS) - (gap - 2 * r)
    x = (w - total) / 2 + r
    cy = int(h * 0.845)
    for _, color in DOTS:
        d.ellipse((x - r, cy - r, x + r, cy + r), fill=color)
        x += gap

    return img.resize((W, H), Image.LANCZOS)


def build_small():
    ico = Image.open(os.path.join(ROOT, "icons", "app.ico"))
    ico.size = (256, 256)
    ico = ico.convert("RGBA").resize((55, 55), Image.LANCZOS)
    bg = Image.new("RGB", (55, 55), (255, 255, 255))
    bg.paste(ico, (0, 0), ico)
    return bg


def main():
    large = build_large()
    large.save(OUT_LARGE, "BMP")
    small = build_small()
    small.save(OUT_SMALL, "BMP")
    print("生成 %s (%s)" % (OUT_LARGE, "x".join(map(str, large.size))))
    print("生成 %s (%s)" % (OUT_SMALL, "x".join(map(str, small.size))))


if __name__ == "__main__":
    main()
