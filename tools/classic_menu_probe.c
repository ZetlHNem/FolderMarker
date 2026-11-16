// 经典右键菜单探针：用 IContextMenu::QueryContextMenu 构建菜单并递归枚举，
// 不弹窗口、不截图，客观验证「文件夹标记」下到底有几项。
// 关键：QueryContextMenu 会实例化全机第三方 shell 扩展，某些扩展要 STA 消息泵，
// 所以放到工作线程跑，主线程泵消息，避免互等死锁。
// 用法：classic_menu_probe.exe "<文件夹绝对路径>" [timeout秒]
#define WIN32_LEAN_AND_MEAN
#include <initguid.h>
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <objbase.h>
#include <stdio.h>
#include <string.h>

#ifndef LPITEMID_LIST
typedef LPITEMIDLIST LPITEMID_LIST;
#endif

static void wide_to_utf8(const wchar_t *w, char *out, int cap)
{
    if (!w) { out[0] = 0; return; }
    WideCharToMultiByte(CP_UTF8, 0, w, -1, out, cap, NULL, NULL);
}

static void dump_menu(HMENU h, int depth)
{
    int n = GetMenuItemCount(h);
    for (int i = 0; i < n; i++)
    {
        wchar_t buf[512] = L"";
        MENUITEMINFOW mi;
        ZeroMemory(&mi, sizeof(mi));
        mi.cbSize = sizeof(mi);
        mi.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_FTYPE;
        mi.dwTypeData = buf;
        mi.cch = 511;
        if (!GetMenuItemInfoW(h, (UINT)i, TRUE, &mi)) { continue; }
        char t8[1024];
        wide_to_utf8(buf, t8, sizeof(t8));
        if (mi.fType & MFT_SEPARATOR) { printf("%*s---\n", depth * 3, ""); continue; }
        printf("%*s%s%s\n", depth * 3, "", t8, mi.hSubMenu ? "  >" : "");
        if (mi.hSubMenu && depth < 2) { dump_menu(mi.hSubMenu, depth + 1); }
    }
}

static void find_target(HMENU h, int depth, const char *target)
{
    int n = GetMenuItemCount(h);
    for (int i = 0; i < n; i++)
    {
        wchar_t buf[512] = L"";
        MENUITEMINFOW mi;
        ZeroMemory(&mi, sizeof(mi));
        mi.cbSize = sizeof(mi);
        mi.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_FTYPE;
        mi.dwTypeData = buf;
        mi.cch = 511;
        if (!GetMenuItemInfoW(h, (UINT)i, TRUE, &mi)) { continue; }
        char t8[1024];
        wide_to_utf8(buf, t8, sizeof(t8));
        if (_stricmp(t8, target) == 0)
        {
            printf("[找到] %s  子菜单=%s\n", t8, mi.hSubMenu ? "有" : "无(空箭头!)");
            if (mi.hSubMenu) { dump_menu(mi.hSubMenu, 1); }
            return;
        }
        if (mi.hSubMenu && depth < 1) { find_target(mi.hSubMenu, depth + 1, target); }
    }
}

typedef struct
{
    wchar_t path[MAX_PATH];
    volatile LONG done;
} ProbeCtx;

static DWORD WINAPI worker(LPVOID p)
{
    ProbeCtx *ctx = (ProbeCtx *)p;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    printf("[worker] STA ready\n");

    LPITEMID_LIST pidlAbs = ILCreateFromPathW(ctx->path);
    if (!pidlAbs) { printf("[worker] ILCreateFromPathW failed\n"); goto out; }

    IShellFolder *parent = NULL;
    LPCITEMIDLIST child = NULL;
    HRESULT hr = SHBindToParent(pidlAbs, &IID_IShellFolder, (void **)&parent, &child);
    if (FAILED(hr) || !parent) { printf("[worker] SHBindToParent 0x%08X\n", (unsigned)hr); goto out; }

    IContextMenu *cm = NULL;
    hr = parent->lpVtbl->GetUIObjectOf(parent, NULL, 1, &child, &IID_IContextMenu, NULL, (void **)&cm);
    if (FAILED(hr) || !cm) { printf("[worker] GetUIObjectOf 0x%08X\n", (unsigned)hr); goto out; }

    printf("[worker] QueryContextMenu...\n");
    HMENU h = CreatePopupMenu();
    hr = cm->lpVtbl->QueryContextMenu(cm, h, 0, 1, 0x7FFF, CMF_NORMAL);
    printf("[worker] QueryContextMenu hr=0x%08X 顶层项数=%d\n", (unsigned)hr, GetMenuItemCount(h));
    find_target(h, 0, "文件夹标记");

    DestroyMenu(h);
    cm->lpVtbl->Release(cm);
    parent->lpVtbl->Release(parent);
    CoTaskMemFree(pidlAbs);
out:
    CoUninitialize();
    InterlockedExchange(&ctx->done, 1);
    return 0;
}

int main(int argc, char **argv)
{
    setvbuf(stdout, NULL, _IONBF, 0);
    static ProbeCtx ctx;
    if (argc > 1) { MultiByteToWideChar(CP_UTF8, 0, argv[1], -1, ctx.path, MAX_PATH); }
    else { wcscpy(ctx.path, L"C:\\"); }
    for (wchar_t *q = ctx.path; *q; q++) { if (*q == L'/') { *q = L'\\'; } }
    int limit = (argc > 2) ? atoi(argv[2]) : 25;
    char p8[MAX_PATH];
    wide_to_utf8(ctx.path, p8, sizeof(p8));
    printf("[main] 目标: %s  超时 %ds\n", p8, limit);

    CreateThread(NULL, 0, worker, &ctx, 0, NULL);

    // STA 消息泵：让第三方扩展的跨线程 SendMessage 能得到响应
    DWORD start = GetTickCount();
    while (!ctx.done && (int)(GetTickCount() - start) < limit * 1000)
    {
        MSG msg;
        while (PeekMessageW(&msg, NULL, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        Sleep(10);
    }
    if (!ctx.done) { printf("[main] 超时：QueryContextMenu 仍卡在第三方扩展\n"); }
    else { printf("[main] 完成\n"); }
    return ctx.done ? 0 : 3;
}
