# -*- coding: utf-8 -*-
"""生成右键菜单分组图标 icons/menu.ico。

这里**刻意**不再单独设计一套视觉：菜单图标和 app.ico 用同一个 logo
（深墨色圆角底板 + 彩虹渐变文件夹），只是尺寸集更小。

之前那版是「透明底蓝色文件夹 + 红黄绿三个小圆点」，问题：
  * 和 app.ico 完全是两套观感，右键菜单里有种「路边捡来的图标」的感觉；
  * 三个圆点在 16px 下糊成一片彩色噪点，反而更难认；
  * 纯蓝的文件夹没有任何「这是色彩标记工具」的信息，靠三个点硬凑。

现在菜单里就是 app 图标缩小版，一眼能对上号。

用法：
    python gen_menu_icon.py                # 写 icons/menu.ico
    python gen_menu_icon.py black          # 换底板
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import gen_app_icon as G

OUT = os.path.join(ROOT, "icons", "menu.ico")
SIZES = [48, 40, 32, 24, 20, 16]


def build(style="rainbow", tile="ink", path=OUT):
    G.SIZES = SIZES
    G.SS = 8                     # 小尺寸 8x8 超采样，边缘更干净
    sprite = G.folder_sprite(G.read_ico(G.BASE), G.GRADIENTS[style])
    pngs = [G.to_png(s, G.render(s, sprite, G.TILES[tile])) for s in SIZES]
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "wb") as f:
        f.write(G.to_ico(pngs))
    print("生成 %s (%d bytes, %d 帧)" % (path, os.path.getsize(path), len(pngs)))


if __name__ == "__main__":
    style = sys.argv[1] if len(sys.argv) > 1 else "rainbow"
    tile = sys.argv[2] if len(sys.argv) > 2 else "ink"
    path = sys.argv[3] if len(sys.argv) > 3 else OUT
    build(style, tile, path)
