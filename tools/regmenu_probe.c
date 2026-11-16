// 注册表静态菜单探针：模拟 Explorer 对一个文件夹构建经典右键菜单，
// 枚举打印所有条目（含禁用态与子菜单层级）。
// 要点：
//   1. QueryContextMenu 会加载全部 shell 扩展，其中有些要 STA 消息泵——
//      整个过程放工作线程，主线程泵消息。
//   2. 静态级联（SubCommands）在子菜单真正被打开时才填充；
//      探针用 TrackPopupMenu（屏幕外）+ 键盘导航打开「文件夹标记」
//      及其第一个级联子项，填充后再枚举，最后自动 ESC 关闭。
// 用法：regmenu_probe.exe <目录路径> [CMF flags 十六进制]
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <stdio.h>

static LPITEMIDLIST g_pidl;
static HWND g_owner;
static HANDLE g_shown, g_closed;
static HMENU g_menu;
static IContextMenu3 *g_pcm3;
static UINT g_flags;

static const IID g_iidSF =
    { 0x000214E6, 0x0000, 0x0000, { 0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 } };
static const IID g_iidCM =
    { 0x000214E4, 0x0000, 0x0000, { 0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 } };
static const IID g_iidCM3 =
    { 0xBCFCE0A0, 0xEC17, 0x11D0, { 0x8D,0x21,0x00,0xC0,0x4F,0xD9,0xDB,0x89 } };

static char g_out[131072];
static void emitf(const char *fmt, ...)
{
    size_t n = strlen(g_out);
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf(g_out + n, sizeof(g_out) - n - 1, fmt, ap);
    va_end(ap);
}

/* 隐藏菜单拥有者窗口：把菜单消息转发给 IContextMenu3 */
static LRESULT CALLBACK owner_proc(HWND w, UINT m, WPARAM wp, LPARAM lp)
{
    if (g_pcm3 && m == WM_INITMENUPOPUP)
        g_pcm3->lpVtbl->HandleMenuMsg(g_pcm3, m, wp, lp);
    return DefWindowProcW(w, m, wp, lp);
}

static DWORD WINAPI worker(LPVOID arg)
{
    (void)arg;
    IShellFolder *psf = NULL;
    LPCITEMIDLIST child = NULL;
    HRESULT hr = SHBindToParent((LPCITEMIDLIST)g_pidl, &g_iidSF, (void **)&psf, &child);
    if (FAILED(hr)) { emitf("SHBindToParent 失败 0x%08lX\r\n", hr); SetEvent(g_closed); return 0; }
    IContextMenu *pcm = NULL;
    hr = psf->lpVtbl->GetUIObjectOf(psf, NULL, 1, &child, &g_iidCM, NULL, (void **)&pcm);
    psf->lpVtbl->Release(psf);
    if (FAILED(hr)) { emitf("GetUIObjectOf 失败 0x%08lX\r\n", hr); SetEvent(g_closed); return 0; }
    pcm->lpVtbl->QueryInterface(pcm, &g_iidCM3, (void **)&g_pcm3);

    WNDCLASSW wc = {0};
    wc.lpfnWndProc   = owner_proc;
    wc.lpszClassName = L"FmProbeOwner";
    RegisterClassW(&wc);
    g_owner = CreateWindowExW(0, wc.lpszClassName, L"", WS_POPUP,
                              -3000, -3000, 10, 10, NULL, NULL, NULL, NULL);

    g_menu = CreatePopupMenu();
    HRESULT qhr = pcm->lpVtbl->QueryContextMenu(pcm, g_menu, 0, 1, 0x7FFF, g_flags);
    emitf("QueryContextMenu hr=0x%08lX 顶层=%d 项\r\n", qhr, GetMenuItemCount(g_menu));
    SetEvent(g_shown);

    if (GetMenuItemCount(g_menu) > 0)
        TrackPopupMenu(g_menu, TPM_RETURNCMD | TPM_LEFTBUTTON,
                       -2900, -2900, 0, g_owner, NULL);

    if (g_pcm3) { g_pcm3->lpVtbl->Release(g_pcm3); g_pcm3 = NULL; }
    pcm->lpVtbl->Release(pcm);
    DestroyWindow(g_owner);
    SetEvent(g_closed);
    return 0;
}

/* 跨线程枚举 HMENU（弹出期间从主线程调用） */
static void walk(HMENU h, int depth)
{
    int n = GetMenuItemCount(h);
    for (int i = 0; i < n; i++)
    {
        wchar_t buf[256] = {0};
        MENUITEMINFOW mi;
        ZeroMemory(&mi, sizeof(mi));
        mi.cbSize     = sizeof(mi);
        mi.fMask      = MIIM_STRING | MIIM_SUBMENU | MIIM_STATE | MIIM_ID;
        mi.dwTypeData = buf;
        mi.cch        = 255;
        if (!GetMenuItemInfoW(h, i, TRUE, &mi))
            continue;
        char u8[512];
        WideCharToMultiByte(CP_UTF8, 0, buf, -1, u8, sizeof(u8), NULL, NULL);
        emitf("%*s[%2d] id=0x%04x %s%s%s\r\n",
              depth * 2, "", i, (unsigned)mi.wID, u8,
              (mi.fState & MFS_DISABLED) ? "  <禁用>" : "",
              mi.hSubMenu ? "  >" : "");
        if (mi.hSubMenu && depth < 4)
            walk(mi.hSubMenu, depth + 1);
    }
}

static void send_key(WORD vk)
{
    INPUT in[2] = {0};
    in[0].type = INPUT_KEYBOARD;
    in[0].ki.wVk = vk;
    in[1].type = INPUT_KEYBOARD;
    in[1].ki.wVk = vk;
    in[1].ki.dwFlags = KEYEVENTF_KEYUP;
    SendInput(2, in, sizeof(INPUT));
}

int main(int argc, char **argv)
{
    if (argc < 2) { printf("usage: regmenu_probe.exe <dir> [flags_hex] [esc_ms]\n"); return 2; }
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    g_flags = (argc > 2) ? (UINT)strtoul(argv[2], NULL, 16)
                         : (CMF_NORMAL | CMF_EXTENDEDVERBS | CMF_ITEMMENU);

    wchar_t wpath[MAX_PATH];
    for (char *p = argv[1]; *p; p++) if (*p == '/') *p = '\\';
    MultiByteToWideChar(CP_UTF8, 0, argv[1], -1, wpath, MAX_PATH);

    IShellFolder *pdesk = NULL;
    HRESULT hr = SHGetDesktopFolder(&pdesk);
    if (SUCCEEDED(hr))
        hr = pdesk->lpVtbl->ParseDisplayName(pdesk, NULL, NULL, wpath, NULL, &g_pidl, NULL);
    if (pdesk) pdesk->lpVtbl->Release(pdesk);
    if (FAILED(hr)) { printf("ParseDisplayName 失败 0x%08lX\n", hr); return 1; }

    g_shown = CreateEventW(NULL, TRUE, FALSE, NULL);
    g_closed = CreateEventW(NULL, TRUE, FALSE, NULL);
    HANDLE t = CreateThread(NULL, 0, worker, NULL, 0, NULL);
    if (!t) { printf("CreateThread 失败\n"); return 1; }

    /* 等菜单弹出 */
    if (WaitForSingleObject(g_shown, 10000) != WAIT_OBJECT_0)
    { printf("*** 菜单构建超时 ***\n"); TerminateThread(t, 0); return 1; }

    /* 菜单处于跟踪状态：把每个子菜单的 WM_INITMENUPOPUP 投递给拥有者窗口，
       模态循环会泵到它并在正确线程上转发给 IContextMenu3，触发静态级联填充 */
    Sleep(1200);
    for (int pass = 0; pass < 2; pass++)
    {
        HMENU stack[64]; int sp = 0;
        stack[sp++] = g_menu;
        while (sp)
        {
            HMENU h = stack[--sp];
            int n = GetMenuItemCount(h);
            for (int i2 = 0; i2 < n; i2++)
            {
                MENUITEMINFOW mi = {0};
                wchar_t buf[128] = {0};
                mi.cbSize = sizeof(mi);
                mi.fMask = MIIM_STRING | MIIM_SUBMENU;
                mi.dwTypeData = buf; mi.cch = 127;
                if (!GetMenuItemInfoW(h, i2, TRUE, &mi) || !mi.hSubMenu)
                    continue;
                PostMessageW(g_owner, WM_INITMENUPOPUP,
                             (WPARAM)mi.hSubMenu, MAKELPARAM(i2, 0));
                if (sp < 63 && pass == 0)
                    stack[sp++] = mi.hSubMenu;
            }
        }
        Sleep(1200);
    }
    send_key(VK_ESCAPE);
    PostMessageW(g_owner, WM_CANCELMODE, 0, 0);
    WaitForSingleObject(g_closed, 10000);

    emitf("================ 菜单内容（关闭后枚举）================\r\n");
    walk(g_menu, 0);
    emitf("======================================================\r\n");
    printf("%s", g_out);
    fflush(stdout);
    CloseHandle(t); CloseHandle(g_shown); CloseHandle(g_closed);
    ILFree(g_pidl);
    CoUninitialize();
    return 0;
}
