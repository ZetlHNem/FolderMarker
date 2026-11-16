import ctypes, ctypes.wintypes as wt, sys

user32 = ctypes.WinDLL('user32', use_last_error=True)
EnumWindows = user32.EnumWindows
EnumWindowsProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

pid_want = int(sys.argv[1])
rows = []

def cb(hwnd, lparam):
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    if pid.value == pid_want:
        buf = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd, buf, 256)
        cls = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls, 256)
        r = wt.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        style = user32.GetWindowLongW(hwnd, -16)
        ex = user32.GetWindowLongW(hwnd, -20)
        rows.append(dict(hwnd=hex(hwnd), visible=bool(user32.IsWindowVisible(hwnd)),
                         title=buf.value, cls=cls.value,
                         rect=(r.left, r.top, r.right, r.bottom),
                         style=hex(style & 0xFFFFFFFF), exstyle=hex(ex & 0xFFFFFFFF),
                         iconic=bool(user32.IsIconic(hwnd))))
    return True

EnumWindows(EnumWindowsProc(cb), 0)
print('pid', pid_want, 'top-level windows:', len(rows))
for r in rows:
    print(r)
