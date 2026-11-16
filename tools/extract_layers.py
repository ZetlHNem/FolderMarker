# -*- coding: utf-8 -*-
"""抓 imageres.dll 指定索引的图标（保留 alpha 通道），验证「文件夹缩略图 = 5 底层 + 内容 + 6 顶层」的组合结构。"""
import ctypes
import os
import struct
from ctypes import wintypes, byref, POINTER

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32

# 64 位句柄必须显式声明类型，否则 ctypes 会当 int 溢出
gdi32.GetObjectW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p]
gdi32.GetObjectW.restype = ctypes.c_int
gdi32.GetDIBits.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint,
                            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
gdi32.GetDIBits.restype = ctypes.c_int
gdi32.DeleteObject.argtypes = [ctypes.c_void_p]
gdi32.DeleteObject.restype = ctypes.c_int
user32.GetIconInfo.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.GetIconInfo.restype = ctypes.c_int
user32.PrivateExtractIconsW.argtypes = [ctypes.c_wchar_p, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint]
user32.PrivateExtractIconsW.restype = ctypes.c_uint
user32.DestroyIcon.argtypes = [ctypes.c_void_p]
user32.DestroyIcon.restype = ctypes.c_int
user32.GetDC.argtypes = [ctypes.c_void_p]
user32.GetDC.restype = ctypes.c_void_p
user32.ReleaseDC.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.ReleaseDC.restype = ctypes.c_int


class ICONINFO(ctypes.Structure):
    _fields_ = [
        ("fIcon", ctypes.c_int),
        ("xHotspot", ctypes.c_uint),
        ("yHotspot", ctypes.c_uint),
        ("hbmMask", ctypes.c_void_p),
        ("hbmColor", ctypes.c_void_p),
    ]


class BITMAP(ctypes.Structure):
    _fields_ = [
        ("bmType", ctypes.c_long), ("bmWidth", ctypes.c_long), ("bmHeight", ctypes.c_long),
        ("bmWidthBytes", ctypes.c_long), ("bmPlanes", ctypes.c_ushort), ("bmBitsPixel", ctypes.c_ushort),
        ("bmBits", ctypes.c_void_p),
    ]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", ctypes.c_ulong), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
        ("biPlanes", ctypes.c_ushort), ("biBitCount", ctypes.c_ushort), ("biCompression", ctypes.c_ulong),
        ("biSizeImage", ctypes.c_ulong), ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", ctypes.c_ulong), ("biClrImportant", ctypes.c_ulong),
    ]


def extract_icon_rgba(dll, index, size):
    icons = (ctypes.c_void_p * 1)()
    got = user32.PrivateExtractIconsW(ctypes.c_wchar_p(dll), index, size, size,
                                      icons, None, 1, 0)
    if got == 0 or not icons[0]:
        return None
    hicon = icons[0]
    try:
        ii = ICONINFO()
        if not user32.GetIconInfo(hicon, byref(ii)):
            return None
        try:
            bm = BITMAP()
            gdi32.GetObjectW(ii.hbmColor, ctypes.sizeof(BITMAP), byref(bm))
            w, h = bm.bmWidth, bm.bmHeight

            bi = BITMAPINFOHEADER()
            bi.biSize = 40
            bi.biWidth = w
            bi.biHeight = -h
            bi.biPlanes = 1
            bi.biBitCount = 32
            bi.biCompression = 0

            bits = (ctypes.c_ubyte * (w * h * 4))()
            hdc = user32.GetDC(None)
            gdi32.GetDIBits(hdc, ii.hbmColor, 0, h, bits, byref(bi), 0)

            # AND 掩码（32bpp 图标一般 alpha 有效，但有的资源 alpha 全 0，要用掩码补）
            has_alpha = any(bits[i] != 0 for i in range(3, w * h * 4, 4))
            if not has_alpha and ii.hbmMask:
                bmm = BITMAP()
                gdi32.GetObjectW(ii.hbmMask, ctypes.sizeof(BITMAP), byref(bmm))
                mbi = BITMAPINFOHEADER()
                mbi.biSize = 40
                mbi.biWidth = w
                mbi.biHeight = -h
                mbi.biPlanes = 1
                mbi.biBitCount = 1
                mbi.biCompression = 0
                mbits = (ctypes.c_ubyte * (((w + 31) // 32) * 4 * h))()
                gdi32.GetDIBits(hdc, ii.hbmMask, 0, h, mbits, byref(mbi), 0)
                stride = ((w + 31) // 32) * 4
                for y in range(h):
                    for x in range(w):
                        bit = (mbits[y * stride + x // 8] >> (7 - x % 8)) & 1
                        bits[(y * w + x) * 4 + 3] = 0 if bit else 255
            user32.ReleaseDC(None, hdc)
            return w, h, bytes(bits)
        finally:
            gdi32.DeleteObject(ii.hbmMask)
            gdi32.DeleteObject(ii.hbmColor)
    finally:
        user32.DestroyIcon(hicon)


def save_png(w, h, bgra, out):
    from PIL import Image
    im = Image.frombytes("RGBA", (w, h), bgra, "raw", "BGRA")
    im.save(out)
    return im


if __name__ == "__main__":
    import sys
    from PIL import Image
    dll = r"C:\Windows\System32\imageres.dll"
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "build", "layers")
    os.makedirs(out_dir, exist_ok=True)

    for idx in [int(x) for x in sys.argv[1:]] or [5, 6]:
        r = extract_icon_rgba(dll, idx, 256)
        if not r:
            print(idx, "抓取失败")
            continue
        w, h, buf = r
        im = save_png(w, h, buf, os.path.join(out_dir, "imageres_%d.png" % idx))
        # 叠到棋盘格上看形状
        board = Image.new("RGBA", (w, h), (255, 255, 255, 255))
        px = board.load()
        for y in range(h):
            for x in range(w):
                if (x // 16 + y // 16) % 2 == 0:
                    px[x, y] = (230, 230, 230, 255)
        board.alpha_composite(im)
        board.save(os.path.join(out_dir, "imageres_%d_board.png" % idx))
        print(idx, "OK", im.size)
