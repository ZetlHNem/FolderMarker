import ctypes, ctypes.wintypes as wt, sys
from PIL import Image

shell32 = ctypes.WinDLL('shell32', use_last_error=True)
user32 = ctypes.WinDLL('user32', use_last_error=True)
gdi32 = ctypes.WinDLL('gdi32', use_last_error=True)

shell32.ExtractIconExW.argtypes = [wt.LPCWSTR, ctypes.c_int, ctypes.POINTER(wt.HICON), ctypes.POINTER(wt.HICON), wt.UINT]
shell32.ExtractIconExW.restype = wt.UINT

class ICONINFO(ctypes.Structure):
    _fields_ = [('fIcon', wt.BOOL), ('xHotspot', wt.DWORD), ('yHotspot', wt.DWORD),
                ('hbmMask', wt.HBITMAP), ('hbmColor', wt.HBITMAP)]
class BITMAP(ctypes.Structure):
    _fields_ = [('bmType', wt.LONG), ('bmWidth', wt.LONG), ('bmHeight', wt.LONG),
                ('bmWidthBytes', wt.LONG), ('bmPlanes', wt.WORD), ('bmBitsPixel', wt.WORD),
                ('bmBits', ctypes.c_void_p)]

user32.GetIconInfo.argtypes = [wt.HICON, ctypes.c_void_p]
user32.GetDC.argtypes = [wt.HWND]; user32.GetDC.restype = wt.HDC
gdi32.GetObjectW.argtypes = [wt.HANDLE, ctypes.c_int, ctypes.c_void_p]
gdi32.GetObjectW.restype = ctypes.c_int
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.c_void_p, wt.UINT]

def extract(path, which, out):
    big = wt.HICON(); small = wt.HICON()
    n = shell32.ExtractIconExW(path, 0, ctypes.byref(big), ctypes.byref(small), 1)
    h = (big if which == 1 else small).value
    if not h:
        print('no icon', path); return
    ii = ICONINFO(); user32.GetIconInfo(h, ctypes.byref(ii))
    bm = BITMAP(); gdi32.GetObjectW(ii.hbmColor, ctypes.sizeof(BITMAP), ctypes.byref(bm))
    w, hh = bm.bmWidth, bm.bmHeight
    bmi = ctypes.create_string_buffer(40)
    ctypes.cast(bmi, ctypes.POINTER(wt.DWORD))[0] = 40
    ctypes.cast(bmi, ctypes.POINTER(wt.LONG))[1] = w
    ctypes.cast(bmi, ctypes.POINTER(wt.LONG))[2] = -hh
    ctypes.cast(bmi, ctypes.POINTER(wt.WORD))[6] = 1
    ctypes.cast(bmi, ctypes.POINTER(wt.WORD))[7] = 32
    buf = ctypes.create_string_buffer(w * hh * 4)
    gdi32.GetDIBits(user32.GetDC(0), ii.hbmColor, 0, hh, buf, bmi, 0)
    img = Image.frombuffer('RGBA', (w, hh), buf.raw, 'raw', 'BGRA', 0, 1)
    a = img.split()[3]
    print(path, which, w, 'corner', a.getpixel((0, 0)), 'top row', [a.getpixel((x, 0)) for x in range(min(w, 6))])
    img.resize((w * 8, hh * 8), Image.NEAREST).save(out)

extract(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 0, sys.argv[3] if len(sys.argv) > 3 else 'build/exeico.png')
