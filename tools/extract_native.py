# -*- coding: utf-8 -*-
"""从系统抓取「原生文件夹图标」在多个尺寸下的 RGBA 位图。

用 IShellItemImageFactory::GetImage 直接拿资源管理器渲染出来的那张图，
所以形状、阴影、抗锯齿与系统完全一致，不做任何重绘。

输出: build/native/<size>.rgba   (裸 RGBA, 便于后续上色)
      build/native/<size>.png    (用于肉眼确认)
"""
import ctypes
import os
import struct
import sys
import zlib
from ctypes import POINTER, byref, c_int, c_uint, c_void_p, wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "build", "native")

ole32 = ctypes.windll.ole32
shell32 = ctypes.windll.shell32
gdi32 = ctypes.windll.gdi32
user32 = ctypes.windll.user32

SIIGBF_BIGGERSIZEOK = 0x00000001
SIIGBF_ICONONLY = 0x00000004


class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", ctypes.c_ulong),
        ("Data2", ctypes.c_ushort),
        ("Data3", ctypes.c_ushort),
        ("Data4", ctypes.c_ubyte * 8),
    ]

    def __init__(self, s):
        super().__init__()
        parts = s.strip("{}").split("-")
        self.Data1 = int(parts[0], 16)
        self.Data2 = int(parts[1], 16)
        self.Data3 = int(parts[2], 16)
        tail = parts[3] + parts[4]
        for i in range(8):
            self.Data4[i] = int(tail[i * 2:i * 2 + 2], 16)


IID_IShellItemImageFactory = GUID("bcc18b79-ba16-442f-80c4-8a59c30c463b")


class SIZE(ctypes.Structure):
    _fields_ = [("cx", ctypes.c_long), ("cy", ctypes.c_long)]


class BITMAP(ctypes.Structure):
    _fields_ = [
        ("bmType", ctypes.c_long),
        ("bmWidth", ctypes.c_long),
        ("bmHeight", ctypes.c_long),
        ("bmWidthBytes", ctypes.c_long),
        ("bmPlanes", ctypes.c_ushort),
        ("bmBitsPixel", ctypes.c_ushort),
        ("bmBits", c_void_p),
    ]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", ctypes.c_ulong),
        ("biWidth", ctypes.c_long),
        ("biHeight", ctypes.c_long),
        ("biPlanes", ctypes.c_ushort),
        ("biBitCount", ctypes.c_ushort),
        ("biCompression", ctypes.c_ulong),
        ("biSizeImage", ctypes.c_ulong),
        ("biXPelsPerMeter", ctypes.c_long),
        ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", ctypes.c_ulong),
        ("biClrImportant", ctypes.c_ulong),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", ctypes.c_ulong * 3)]


def com_method(ptr, index, restype, *argtypes):
    vtable = ctypes.cast(ptr, POINTER(POINTER(c_void_p))).contents
    fn = ctypes.WINFUNCTYPE(restype, c_void_p, *argtypes)(vtable[index])
    return fn


def hbitmap_to_rgba(hbm, w, h):
    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h  # 负数 = 自上而下
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0  # BI_RGB

    buf = (ctypes.c_ubyte * (w * h * 4))()
    hdc = gdi32.CreateCompatibleDC(None)
    ok = gdi32.GetDIBits(hdc, hbm, 0, h, buf, byref(bmi), 0)
    gdi32.DeleteDC(hdc)
    if not ok:
        return None
    data = bytearray(buf)

    # GetDIBits 返回 BGRA，且未预乘 alpha；部分图标 alpha 全 0 需按亮度兜底
    has_alpha = any(data[i] for i in range(3, len(data), 4))
    for i in range(0, len(data), 4):
        b, g, r, a = data[i], data[i + 1], data[i + 2], data[i + 3]
        data[i], data[i + 1], data[i + 2] = r, g, b
        if has_alpha and a:
            # 反预乘
            data[i] = min(255, data[i] * 255 // a)
            data[i + 1] = min(255, data[i + 1] * 255 // a)
            data[i + 2] = min(255, data[i + 2] * 255 // a)
        elif not has_alpha:
            data[i + 3] = 255
    return bytes(data)


def grab(path, size):
    factory = c_void_p()
    hr = shell32.SHCreateItemFromParsingName(
        ctypes.c_wchar_p(path), None, byref(IID_IShellItemImageFactory), byref(factory)
    )
    if hr != 0 or not factory:
        return None, hr

    hbm = c_void_p()
    get_image = com_method(factory, 3, ctypes.HRESULT, SIZE, c_int, POINTER(c_void_p))
    try:
        get_image(factory, SIZE(size, size), SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, byref(hbm))
    except OSError:
        pass

    if not hbm:
        com_method(factory, 2, ctypes.c_ulong)(factory)  # Release
        return None, -1

    bm = BITMAP()
    gdi32.GetObjectW(hbm, ctypes.sizeof(BITMAP), byref(bm))
    w, h = bm.bmWidth, bm.bmHeight
    rgba = hbitmap_to_rgba(hbm, w, h)
    gdi32.DeleteObject(hbm)
    com_method(factory, 2, ctypes.c_ulong)(factory)
    return (w, h, rgba), 0


def write_png(path, w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, body):
        return (
            struct.pack(">I", len(body))
            + tag
            + body
            + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)
        )

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def main():
    os.makedirs(OUT, exist_ok=True)
    sample = os.path.join(ROOT, "build", "native_sample")
    os.makedirs(sample, exist_ok=True)
    if not os.listdir(sample):
        open(os.path.join(sample, "x.txt"), "w").close()

    ole32.CoInitialize(None)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    for s in sizes:
        res, hr = grab(sample, s)
        if not res:
            print("FAIL size=%d hr=%s" % (s, hr))
            continue
        w, h, rgba = res
        with open(os.path.join(OUT, "%d.rgba" % s), "wb") as f:
            f.write(struct.pack("<II", w, h) + rgba)
        write_png(os.path.join(OUT, "%d.png" % s), w, h, rgba)
        print("ok %3d -> %dx%d" % (s, w, h))
    ole32.CoUninitialize()


if __name__ == "__main__":
    main()
