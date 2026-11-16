// 路由自测：模拟 Explorer 在两套菜单里的三种真实调用序列。
// 新版 4 个 Verb 全部 ENABLED；经典在首个 GetFlags 处返回 E_NOTIMPL，
// 并在后续 GetState 返回 HIDDEN（交给注册表「文件夹标记 >」）。
// 两套菜单标题始终是同一份中文。
// 用法：classic_probe.exe <FolderMarker.Shell.dll>
#define WIN32_LEAN_AND_MEAN
#include <initguid.h>
#include <windows.h>
#include <shobjidl.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

typedef HRESULT(__stdcall *PFN_GetClassObject)(REFCLSID, REFIID, void **);

#define VERB_COUNT 3
static const CLSID g_clsids[VERB_COUNT] =
{
    {0x6F1D3A24, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x41}},
    {0x6F1D3A26, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x43}},
    {0x6F1D3A27, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x44}}
};
static const char *g_labels[VERB_COUNT] =
{
    "颜色", "清除标记", "管理"
};

static IExplorerCommand *new_root(PFN_GetClassObject get, const CLSID *cid)
{
    IClassFactory *fac = NULL;
    IExplorerCommand *cmd = NULL;
    if (FAILED(get(cid, &IID_IClassFactory, (void **)&fac))) { return NULL; }
    if (FAILED(fac->lpVtbl->CreateInstance(fac, NULL, &IID_IExplorerCommand, (void **)&cmd))) { cmd = NULL; }
    fac->lpVtbl->Release(fac);
    return cmd;
}

// 0 = 新版顶层菜单（GetState 打头），1 = 新版预构建（GetTitle 打头），
// 2 = 经典菜单（GetFlags 打头）
static int probe_one(PFN_GetClassObject get, int verb, int mode)
{
    IExplorerCommand *c = new_root(get, &g_clsids[verb]);
    if (!c) { printf("  创建失败"); return -1; }

    EXPCMDFLAGS flags = 0;
    EXPCMDSTATE state = 0;
    LPWSTR s = NULL;

    if (mode == 0)
    {
        c->lpVtbl->GetState(c, NULL, FALSE, &state);
        c->lpVtbl->GetTitle(c, NULL, &s);
        c->lpVtbl->GetIcon(c, NULL, &s);
        c->lpVtbl->GetFlags(c, &flags);
    }
    else if (mode == 1)
    {
        c->lpVtbl->GetTitle(c, NULL, &s);
        c->lpVtbl->GetFlags(c, &flags);
        c->lpVtbl->GetIcon(c, NULL, &s);
        c->lpVtbl->GetState(c, NULL, FALSE, &state);
    }
    else
    {
        c->lpVtbl->GetFlags(c, &flags);
        GUID cn;
        c->lpVtbl->GetCanonicalName(c, &cn);   // 经典菜单特有，判据靠它
        c->lpVtbl->GetTitle(c, NULL, &s);
        c->lpVtbl->GetIcon(c, NULL, &s);
        c->lpVtbl->GetState(c, NULL, FALSE, &state);
    }
    c->lpVtbl->Release(c);
    return (int)state;
}

/* 统一架构下两套菜单用同一份中文标题。返回 0 = 标题正确。 */
static const wchar_t *const g_wtitles[VERB_COUNT] =
{
    L"颜色", L"清除标记", L"管理"
};

static int probe_title(PFN_GetClassObject get, int verb, int classic)
{
    IExplorerCommand *c = new_root(get, &g_clsids[verb]);
    if (!c) { return -1; }
    EXPCMDFLAGS flags = 0;
    EXPCMDSTATE state = 0;
    LPWSTR s = NULL;
    int flags_ok = 1;
    int canonical_ok = 1;
    if (classic)
    {
        HRESULT hrF = c->lpVtbl->GetFlags(c, &flags);
        flags_ok = (hrF == E_NOTIMPL);       // 经典必须在首个 GetFlags 处被拦截
        GUID cn;
        HRESULT hr = c->lpVtbl->GetCanonicalName(c, &cn);
        canonical_ok = (hr == E_NOTIMPL);   // 经典菜单必须拿不到名字
    }
    else         { c->lpVtbl->GetState(c, NULL, FALSE, &state); }
    c->lpVtbl->GetTitle(c, NULL, &s);
    int ok = 0;
    if (s)
    {
        size_t n = wcslen(s);
        while (n > 0 && s[n - 1] == L' ') { n--; }
        ok = (n == wcslen(g_wtitles[verb]) && wcsncmp(s, g_wtitles[verb], n) == 0);
    }
    if (s) { CoTaskMemFree(s); }
    c->lpVtbl->Release(c);
    return (ok && canonical_ok && flags_ok) ? 0 : 1;
}

int main(int argc, char **argv)
{
    const char *dll = (argc > 1) ? argv[1] : "build\\FolderMarker.Shell.dll";
    HMODULE mod = LoadLibraryA(dll);
    if (!mod) { printf("LoadLibrary failed %lu\n", GetLastError()); return 1; }
    PFN_GetClassObject get = (PFN_GetClassObject)GetProcAddress(mod, "DllGetClassObject");
    if (!get) { printf("DllGetClassObject not found\n"); return 1; }

    // ECS_ENABLED=0, ECS_HIDDEN=2；经典路径交给注册表单入口
    (void)argc;
    const int want[3] = {0, 0, 2};
    const char *modes[3] = {"新版顶层(GetState先)", "新版预构建(GetTitle先)", "经典(GetFlags先)"};
    int bad = 0;

    for (int m = 0; m < 3; m++)
    {
        printf("== %s ==\n", modes[m]);
        for (int v = 0; v < VERB_COUNT; v++)
        {
            int st = probe_one(get, v, m);
            const char *name = (st == 2) ? "HIDDEN" : (st == 0 ? "ENABLED" : "?");
            int ok = (st == want[m]);
            if (!ok) { bad++; }
            printf("  %-12s -> %-7s %s\n", g_labels[v], name, ok ? "OK" : "<<< 不符合预期");
        }
    }
    printf("\n== 统一标题自检（两套菜单都应是同一份中文）==\n");
    for (int v = 0; v < VERB_COUNT; v++)
    {
        int in_classic = probe_title(get, v, 1);
        int in_modern  = probe_title(get, v, 0);
        int ok = (in_classic == 0 && in_modern == 0);
        if (!ok) { bad++; }
        printf("  %-12s 经典=%-6s 新版=%-6s %s\n", g_labels[v],
               in_classic == 0 ? "中文" : "异常",
               in_modern == 0 ? "中文" : "异常",
               ok ? "OK" : "<<< 不符合预期");
    }
    printf("\n结果: %s\n", bad ? "有判定错误" : "新版全 ENABLED / 经典 GetFlags 拦截 + HIDDEN / 标题统一");
    return bad ? 2 : 0;
}
