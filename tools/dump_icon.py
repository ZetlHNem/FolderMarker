import ctypes, ctypes.wintypes as wt, sys
from PIL import Image

user32 = ctypes.WinDLL('user32', use_last_error=True)
gdi32 = ctypes.WinDLL('gdi32', use_last_error=True)

user32.SendMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.SendMessageW.restype = wt.LPARAM
user32.GetIconInfo.argtypes = [wt.HICON, ctypes.c_void_p]
user32.GetIconInfo.restype = wt.BOOL
user32.GetDC.argtypes = [wt.HWND]
user32.GetDC.restype = wt.HDC
user32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
gdi32.GetObjectW.argtypes = [wt.HANDLE, ctypes.c_int, ctypes.c_void_p]
gdi32.GetObjectW.restype = ctypes.c_int
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.c_void_p, wt.UINT]
gdi32.GetDIBits.restype = ctypes.c_int
gdi32.DeleteObject.argtypes = [wt.HANDLE]

class ICONINFO(ctypes.Structure):
    _fields_ = [('fIcon', wt.BOOL), ('xHotspot', wt.DWORD), ('yHotspot', wt.DWORD),
                ('hbmMask', wt.HBITMAP), ('hbmColor', wt.HBITMAP)]

class BITMAP(ctypes.Structure):
    _fields_ = [('bmType', wt.LONG), ('bmWidth', wt.LONG), ('bmHeight', wt.LONG),
                ('bmWidthBytes', wt.LONG), ('bmPlanes', wt.WORD), ('bmBitsPixel', wt.WORD),
                ('bmBits', ctypes.c_void_p)]

def dump(hwnd, which, out):
    hicon = user32.SendMessageW(hwnd, 0x007F, which, 0)  # WM_GETICON
    print('hwnd', hex(hwnd), 'WM_GETICON', which, '->', hex(hicon) if hicon else 0)
    if not hicon:
        return False
    ii = ICONINFO()
    if not user32.GetIconInfo(hicon, ctypes.byref(ii)):
        print('GetIconInfo failed'); return False
    bm = BITMAP()
    gdi32.GetObjectW(ii.hbmColor, ctypes.sizeof(BITMAP), ctypes.byref(bm))
    w, h = bm.bmWidth, bm.bmHeight
    print('size', w, h, 'bpp', bm.bmBitsPixel)

    bmi = ctypes.create_string_buffer(40)
    ctypes.memset(bmi, 0, 40)
    ctypes.cast(bmi, ctypes.POINTER(wt.DWORD))[0] = 40
    ctypes.cast(bmi, ctypes.POINTER(wt.LONG))[1] = w
    ctypes.cast(bmi, ctypes.POINTER(wt.LONG))[2] = -h
    ctypes.cast(bmi, ctypes.POINTER(wt.WORD))[6] = 1
    ctypes.cast(bmi, ctypes.POINTER(wt.WORD))[7] = 32

    buf = ctypes.create_string_buffer(w * h * 4)
    hdc = user32.GetDC(0)
    ok = gdi32.GetDIBits(hdc, ii.hbmColor, 0, h, buf, bmi, 0)
    user32.ReleaseDC(0, hdc)
    print('GetDIBits lines', ok)
    if ok <= 0:
        return False

    img = Image.frombuffer('RGBA', (w, h), buf.raw, 'raw', 'BGRA', 0, 1)
    # 若 alpha 全 0/255 相同，用 mask 补 alpha
    alpha = img.split()[3]
    if alpha.getextrema() == (0, 0):
        print('alpha 全 0，用 mask 合成')
        mbuf = ctypes.create_string_buffer(w * h // 8)
        ctypes.cast(bmi, ctypes.POINTER(wt.LONG))[2] = -h
        hdc = user32.GetDC(0)
        gdi32.GetDIBits(hdc, ii.hbmMask, 0, h, mbuf, bmi, 0)
        user32.ReleaseDC(0, hdc)
        mask = Image.frombuffer('1', (w, h), mbuf.raw, 'raw', '1', 0, 1)
        img.putalpha(mask.convert('L').point(lambda v: 0 if v else 255))
    print('alpha extrema', img.split()[3].getextrema())
    img.save(out)
    img.resize((w * 8, h * 8), Image.NEAREST).save(out.replace('.png', '_zoom.png'))
    gdi32.DeleteObject(ii.hbmColor); gdi32.DeleteObject(ii.hbmMask)
    return True

if __name__ == '__main__':
    hwnd = int(sys.argv[1])
    which = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    dump(hwnd, which, sys.argv[3] if len(sys.argv) > 3 else 'build/win_icon.png')
