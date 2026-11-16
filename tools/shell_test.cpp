// 直接调用 FolderMarker.Shell.dll 的 COM 对象，检查 GetFlags / EnumSubCommands 的真实返回值。
// 用于区分「DLL 逻辑有问题」还是「资源管理器没调它」。
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shobjidl.h>
#include <stdio.h>
#include <wchar.h>
#include <stdlib.h>

typedef HRESULT (__stdcall *PFN_DllGetClassObject)(REFCLSID, REFIID, void **);

int main()
{
    int argc = 0;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    const wchar_t *path = argc > 1 ? argv[1] : L"FolderMarker.Shell.dll";

    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) { wprintf(L"CoInitialize 失败 0x%08X\n", hr); return 1; }

    HMODULE dll = LoadLibraryW(path);
    if (!dll) { wprintf(L"LoadLibrary 失败 %lu\n", GetLastError()); return 1; }
    wprintf(L"DLL 已加载\n");

    PFN_DllGetClassObject gco = (PFN_DllGetClassObject)GetProcAddress(dll, "DllGetClassObject");
    if (!gco) { wprintf(L"找不到 DllGetClassObject\n"); return 1; }

    CLSID clsid = {0x6F1D3A24, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x41}};

    IClassFactory *fac = NULL;
    hr = gco(clsid, IID_IClassFactory, (void **)&fac);
    wprintf(L"DllGetClassObject => 0x%08X  fac=%p\n", hr, (void *)fac);
    if (FAILED(hr) || !fac) { return 1; }

    IUnknown *unk = NULL;
    hr = fac->CreateInstance(NULL, IID_IUnknown, (void **)&unk);
    wprintf(L"CreateInstance => 0x%08X  obj=%p\n", hr, (void *)unk);

    IExplorerCommand *cmd = NULL;
    if (unk) { hr = unk->QueryInterface(IID_PPV_ARGS(&cmd)); }
    wprintf(L"QueryInterface(IExplorerCommand) => 0x%08X  cmd=%p\n", hr, (void *)cmd);
    if (!cmd) { return 1; }

    LPWSTR title = NULL;
    hr = cmd->GetTitle(NULL, &title);
    wprintf(L"GetTitle => 0x%08X  \"%s\"\n", hr, title ? title : L"(null)");
    if (title) { CoTaskMemFree(title); title = NULL; }

    EXPCMDFLAGS flags = 0;
    hr = cmd->GetFlags(&flags);
    wprintf(L"GetFlags => 0x%08X  flags=0x%X  (HASSUBCOMMANDS=%d)\n", hr, flags, (flags & ECF_HASSUBCOMMANDS) ? 1 : 0);

    IEnumExplorerCommand *e = NULL;
    hr = cmd->EnumSubCommands(&e);
    wprintf(L"EnumSubCommands => 0x%08X  enum=%p\n", hr, (void *)e);
    if (e)
    {
        IExplorerCommand *child = NULL;
        ULONG fetched = 0;
        int n = 0;
        while (e->Next(1, &child, &fetched) == S_OK && fetched == 1 && child)
        {
            LPWSTR t = NULL, tip = NULL;
            cmd->GetTitle(NULL, &title);
            child->GetTitle(NULL, &t);
            child->GetToolTip(NULL, &tip);
            GUID canon = {0};
            child->GetCanonicalName(&canon);
            wprintf(L"  [%d] \"%s\"  canon=%08X-%04X-...工具提示=%s\n", n, t ? t : L"(null)",
                    canon.Data1, canon.Data2, tip ? tip : L"");
            if (t) { CoTaskMemFree(t); }
            if (tip) { CoTaskMemFree(tip); }
            child->Release();
            child = NULL;
            n++;
            if (n > 12) { break; }
        }
        wprintf(L"子项总数: %d\n", n);
        // 若给了目标文件夹 + 序号，真正执行一次 Invoke，验证端到端
        if (argc > 3)
        {
            int want = _wtoi(argv[3]);
            IShellItem *si = NULL;
            HRESULT h2 = SHCreateItemFromParsingName(argv[2], NULL, IID_PPV_ARGS(&si));
            IShellItemArray *ia = NULL;
            if (SUCCEEDED(h2) && si) { h2 = SHCreateShellItemArrayFromShellItem(si, IID_PPV_ARGS(&ia)); }
            wprintf(L"构造 ShellItemArray => 0x%08X\n", h2);
            if (ia)
            {
                e->Reset();
                IExplorerCommand *pick = NULL;
                ULONG got = 0;
                for (int i = 0; i <= want; i++)
                {
                    if (e->Next(1, &pick, &got) != S_OK || got != 1) { pick = NULL; break; }
                    if (i < want) { pick->Release(); pick = NULL; }
                }
                if (pick)
                {
                    EXPCMDFLAGS f2 = 0; pick->GetFlags(&f2);
                    HRESULT h3 = pick->Invoke(ia, NULL);
                    wprintf(L"Invoke(第 %d 项) => 0x%08X  flags=0x%X\n", want, h3, f2);
                    pick->Release();
                }
                else { wprintf(L"取第 %d 项失败\n", want); }
                ia->Release();
            }
            if (si) { si->Release(); }
        }
        e->Release();
    }
    if (title) { CoTaskMemFree(title); }

    cmd->Release();
    fac->Release();
    CoUninitialize();
    return 0;
}
