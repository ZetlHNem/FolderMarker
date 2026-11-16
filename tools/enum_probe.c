// 菜单树探针：直接加载 FolderMarker.Shell.dll，实例化两个根 CLSID，
// 把 Explorer 会看到的东西（标题 / Flags / 子项）原样打印出来。
// 用来在不动鼠标的情况下验证「Win11 新菜单里到底有几层、每层几项」。
#define WIN32_LEAN_AND_MEAN
#include <initguid.h>
#include <windows.h>
#include <shobjidl.h>
#include <stdio.h>

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
    "Verb1 颜色", "Verb2 清除标记", "Verb3 管理"
};

static void wide_to_utf8(const wchar_t *w, char *out, int cap)
{
    if (!w) { out[0] = 0; return; }
    WideCharToMultiByte(CP_UTF8, 0, w, -1, out, cap, NULL, NULL);
}

static int walk(PFN_GetClassObject get, const CLSID *cid, const char *label, int depth)
{
    IClassFactory *fac = NULL;
    IExplorerCommand *cmd = NULL;

    HRESULT hr = get(cid, &IID_IClassFactory, (void **)&fac);
    if (FAILED(hr)) { printf("  [%s] DllGetClassObject failed 0x%08X\n", label, hr); return 0; }

    hr = fac->lpVtbl->CreateInstance(fac, NULL, &IID_IExplorerCommand, (void **)&cmd);
    fac->lpVtbl->Release(fac);
    if (FAILED(hr)) { printf("  [%s] CreateInstance failed 0x%08X\n", label, hr); return 0; }

    LPWSTR title = NULL;
    cmd->lpVtbl->GetTitle(cmd, NULL, &title);
    EXPCMDFLAGS flags = ECF_DEFAULT;
    cmd->lpVtbl->GetFlags(cmd, &flags);

    char t[512];
    wide_to_utf8(title, t, sizeof(t));
    printf("%*s[%s] \"%s\"  flags=0x%X%s\n", depth * 2, "", label, t,
           (unsigned)flags, (flags & ECF_HASSUBCOMMANDS) ? "  (有子菜单)" : "");
    if (title) { CoTaskMemFree(title); }

    int total = 0;
    if (flags & ECF_HASSUBCOMMANDS)
    {
        IEnumExplorerCommand *en = NULL;
        if (SUCCEEDED(cmd->lpVtbl->EnumSubCommands(cmd, &en)) && en)
        {
            IExplorerCommand *child = NULL;
            while (en->lpVtbl->Next(en, 1, &child, NULL) == S_OK && child)
            {
                LPWSTR ct = NULL, ci = NULL;
                child->lpVtbl->GetTitle(child, NULL, &ct);
                child->lpVtbl->GetIcon(child, NULL, &ci);
                EXPCMDSTATE st = ECS_ENABLED;
                child->lpVtbl->GetState(child, NULL, FALSE, &st);

                char ct8[512], ci8[512], base[128];
                wide_to_utf8(ct, ct8, sizeof(ct8));
                wide_to_utf8(ci, ci8, sizeof(ci8));

                base[0] = 0;
                if (ci8[0])
                {
                    char *p = strrchr(ci8, '\\');
                    strcpy_s(base, sizeof(base), p ? p + 1 : ci8);
                }

                EXPCMDFLAGS cf = ECF_DEFAULT;
                child->lpVtbl->GetFlags(child, &cf);
                printf("%*s  - \"%s\"  icon=%s  state=%d%s\n",
                       depth * 2, "", ct8, base[0] ? base : "(无)", (int)st,
                       (cf & ECF_HASSUBCOMMANDS) ? "  (还有子菜单)" : "");

                // 第三级：把子项自己的子菜单也打出来
                if (cf & ECF_HASSUBCOMMANDS)
                {
                    IEnumExplorerCommand *en2 = NULL;
                    if (SUCCEEDED(child->lpVtbl->EnumSubCommands(child, &en2)) && en2)
                    {
                        IExplorerCommand *g = NULL;
                        while (en2->lpVtbl->Next(en2, 1, &g, NULL) == S_OK && g)
                        {
                            LPWSTR gt = NULL, gi = NULL;
                            g->lpVtbl->GetTitle(g, NULL, &gt);
                            g->lpVtbl->GetIcon(g, NULL, &gi);
                            char gt8[512], gi8[512], gb[128];
                            wide_to_utf8(gt, gt8, sizeof(gt8));
                            wide_to_utf8(gi, gi8, sizeof(gi8));
                            gb[0] = 0;
                            if (gi8[0]) { char *q = strrchr(gi8, '\\'); strcpy_s(gb, sizeof(gb), q ? q + 1 : gi8); }
                            printf("%*s      · \"%s\"  icon=%s\n", depth * 2, "", gt8, gb[0] ? gb : "(无)");
                            if (gt) { CoTaskMemFree(gt); }
                            if (gi) { CoTaskMemFree(gi); }
                            g->lpVtbl->Release(g);
                            g = NULL;
                        }
                        en2->lpVtbl->Release(en2);
                    }
                }

                if (ct) { CoTaskMemFree(ct); }
                if (ci) { CoTaskMemFree(ci); }
                child->lpVtbl->Release(child);
                child = NULL;
                total++;
            }
            en->lpVtbl->Release(en);
        }
    }

    cmd->lpVtbl->Release(cmd);
    return total;
}

int main(int argc, char **argv)
{
    const char *dllPath = (argc > 1) ? argv[1] : "build\\FolderMarker.Shell.dll";

    HMODULE mod = LoadLibraryA(dllPath);
    if (!mod) { printf("LoadLibrary failed %lu: %s\n", GetLastError(), dllPath); return 1; }

    PFN_GetClassObject get = (PFN_GetClassObject)GetProcAddress(mod, "DllGetClassObject");
    if (!get) { printf("DllGetClassObject not found\n"); return 1; }

    printf("== 菜单树（%d 个 Verb）==\n", VERB_COUNT);
    int total = 0, ok = 0;
    for (int i = 0; i < VERB_COUNT; i++)
    {
        int n = walk(get, &g_clsids[i], g_labels[i], 0);
        printf("   -> %d 项\n", n);
        total += n;
        ok++;
    }
    printf("\n合计 %d 项，%d 个 Verb 全部激活\n", total, ok);
    return 0;
}
