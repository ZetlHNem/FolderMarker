// 原生 COM 服务器：实现 IExplorerCommand + IEnumExplorerCommand。
// 打包进 MSIX sparse package 后，「文件夹标记」才会出现在 Windows 11 的新版右键菜单里。
// 纯 C 手写 vtable（不依赖 ATL / WRL），用 zig cc 编译。
#define WIN32_LEAN_AND_MEAN
#include <initguid.h>
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <stdio.h>
#include <stdarg.h>
#include <wchar.h>

#ifndef SIGDN_FILESYSPATH
#define SIGDN_FILESYSPATH 0x80058000
#endif

static const GUID CLSID_MarkerCommand =
    {0x6F1D3A24, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x41}};
/* 同包 4 个 Verb：2 个分组（各有子菜单）+ 2 个动作。
   Explorer 自动生成一个「文件夹标记」应用 flyout 作为第二层，分组再展开成第三层。
   历史 pkg-badge 挂过 ...B42，但当时 DllGetClassObject 只认 ...B41，
   第二个 Verb 永远激活失败 —— 被误记成「Windows 会静默丢弃第二个 Verb」。 */
#define VERB_COUNT 3
static const GUID g_clsids[VERB_COUNT] =
{
    {0x6F1D3A24, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x41}},
    {0x6F1D3A26, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x43}},
    {0x6F1D3A27, 0x8C7E, 0x4B1A, {0x9E, 0x25, 0x3D, 0x5C, 0x8A, 0x0F, 0x7B, 0x44}}
};
static const GUID IID_IExplorerCommandLocal =
    {0xA08CE4D0, 0xFA25, 0x44AB, {0xB5, 0x7C, 0xC7, 0xB1, 0xC3, 0x23, 0xE0, 0xB9}};
static const GUID IID_IEnumExplorerCommandLocal =
    {0xA88826F8, 0x186F, 0x4987, {0xAA, 0xDE, 0xEA, 0x0C, 0xEF, 0x8F, 0xBF, 0xE8}};

#define ECS_ENABLED_ 0
#define ECS_HIDDEN_ 2
#define ECF_HASSUBCOMMANDS_ 1

/* 一份命令树，两套菜单：
     新版菜单 -> Explorer 自动加「文件夹标记」应用 flyout，4 个 Verb 全部显示；
     经典菜单 -> 由注册表「文件夹标记 >」提供同一个单入口和同一套 4 项。
   日志实测：经典菜单两条构建路径的**第一个调用都是 GetFlags**，
   新版菜单一定是 GetState 或 GetTitle 打头。所以在 GetFlags 处按"首个方法"
   拦截（返回 E_NOTIMPL）是唯一覆盖全部经典路径的最早点；
   Canonical 的 E_NOTIMPL 与 GetState 的 ECS_HIDDEN 作为后续兜底。 */

typedef struct Cmd Cmd;
typedef struct EnumCmd EnumCmd;

typedef struct CmdVtbl
{
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(Cmd *, REFIID, void **);
    ULONG   (STDMETHODCALLTYPE *AddRef)(Cmd *);
    ULONG   (STDMETHODCALLTYPE *Release)(Cmd *);
    HRESULT (STDMETHODCALLTYPE *GetTitle)(Cmd *, IShellItemArray *, LPWSTR *);
    HRESULT (STDMETHODCALLTYPE *GetIcon)(Cmd *, IShellItemArray *, LPWSTR *);
    HRESULT (STDMETHODCALLTYPE *GetToolTip)(Cmd *, IShellItemArray *, LPWSTR *);
    HRESULT (STDMETHODCALLTYPE *GetCanonicalName)(Cmd *, GUID *);
    HRESULT (STDMETHODCALLTYPE *GetState)(Cmd *, IShellItemArray *, BOOL, int *);
    HRESULT (STDMETHODCALLTYPE *Invoke)(Cmd *, IShellItemArray *, IBindCtx *);
    HRESULT (STDMETHODCALLTYPE *GetFlags)(Cmd *, int *);
    HRESULT (STDMETHODCALLTYPE *EnumSubCommands)(Cmd *, void **);
} CmdVtbl;

typedef struct EnumVtbl
{
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(EnumCmd *, REFIID, void **);
    ULONG   (STDMETHODCALLTYPE *AddRef)(EnumCmd *);
    ULONG   (STDMETHODCALLTYPE *Release)(EnumCmd *);
    HRESULT (STDMETHODCALLTYPE *Next)(EnumCmd *, ULONG, void **, ULONG *);
    HRESULT (STDMETHODCALLTYPE *Skip)(EnumCmd *, ULONG);
    HRESULT (STDMETHODCALLTYPE *Reset)(EnumCmd *);
    HRESULT (STDMETHODCALLTYPE *Clone)(EnumCmd *, void **);
} EnumVtbl;

struct Cmd
{
    const CmdVtbl *vtbl;
    LONG ref;
    int verb;            // 0..VERB_COUNT-1 = 某个 Verb 的根命令；-1 = 子命令
    int touched;         // 已有方法被调用过（用来识别"首个调用"）
    int classic;         // 经典菜单构建（GetFlags 拦截或 GetCanonicalName）
    int parent;          // 1 = 分组根（挂子菜单），0 = 可点击动作
    int index;           // 子命令序号，用来生成稳定的 CanonicalName
    int group;           // 0 = 颜色子项，1 = 标记符号子项
    const wchar_t *title;
    const wchar_t *icon;
    const wchar_t *color; // "none" 表示清除
};

struct EnumCmd
{
    const EnumVtbl *vtbl;
    LONG ref;
    ULONG index;
    int group;
};

/* ---------------- 取证日志 ---------------- */
/* 写 %TEMP%\fm_shell.log，UTF-8 编码。
   注意：窄字符 vfprintf 的 %ls 在 C locale 下打不出中文（会输出空串），
   所以宽字符串一律先经 U8() 转 UTF-8 再用 %s 输出，避免把「没拿到标题」
   误判成 Explorer 的行为。 */
static char g_u8buf[4][512];
static int g_u8slot;
static const char *U8(const wchar_t *w)
{
    char *b = g_u8buf[(g_u8slot++) & 3];
    if (!w) { b[0] = 0; return b; }
    WideCharToMultiByte(CP_UTF8, 0, w, -1, b, 512, NULL, NULL);
    b[511] = 0;
    return b;
}

static void logf(const char *fmt, ...)
{
    wchar_t path[MAX_PATH];
    DWORD n = GetTempPathW(MAX_PATH, path);
    if (!n) { return; }
    if (wcslen(path) + 16 >= MAX_PATH) { return; }
    wcscat_s(path, MAX_PATH, L"fm_shell.log");

    FILE *f = NULL;
    if (_wfopen_s(&f, path, L"ab") != 0 || !f) { return; }
    va_list ap;
    va_start(ap, fmt);
    vfprintf(f, fmt, ap);
    va_end(ap);
    fputc('\n', f);
    fclose(f);
}

/* ---------------- 工具 ---------------- */

static HINSTANCE g_module;

static void module_dir(wchar_t *out, DWORD cch)
{
    GetModuleFileNameW(g_module, out, cch);
    wchar_t *slash = wcsrchr(out, L'\\');
    if (slash) { *slash = 0; }
}

static void file_in_dir(wchar_t *out, DWORD cch, const wchar_t *rel)
{
    wchar_t dir[MAX_PATH];
    module_dir(dir, MAX_PATH);
    _snwprintf(out, cch, L"%s\\%s", dir, rel);
    out[cch - 1] = 0;
}

static HRESULT dup_string(const wchar_t *src, LPWSTR *out)
{
    if (!src) { *out = NULL; return S_OK; }
    size_t len = wcslen(src) + 1;
    LPWSTR buf = (LPWSTR)CoTaskMemAlloc(len * sizeof(wchar_t));
    if (!buf) { *out = NULL; return E_OUTOFMEMORY; }
    memcpy(buf, src, len * sizeof(wchar_t));
    *out = buf;
    return S_OK;
}

static void run_for_path(const wchar_t *folder, const wchar_t *color, int quiet)
{
    if (!folder || !*folder) { return; }

    wchar_t exe[MAX_PATH];
    file_in_dir(exe, MAX_PATH, L"FolderMarker.exe");
    if (GetFileAttributesW(exe) == INVALID_FILE_ATTRIBUTES) { return; }

    // quiet = 多选批量。个别文件夹没权限时不弹一串模态框，
    // 由 FolderMarker.exe 里的 FM_QUIET 分支消化掉。
    const wchar_t *tail = quiet ? L" quiet" : L"";

    wchar_t args[2048];
    if (wcscmp(color, L"none") == 0)
    {
        _snwprintf(args, 2048, L"clear none \"%s\"%s", folder, tail);
    }
    else if (wcscmp(color, L"pick") == 0 || wcscmp(color, L"undo") == 0)
    {
        // 这两个动作自带参数形态：<动作> none <文件夹>
        _snwprintf(args, 2048, L"%s none \"%s\"%s", color, folder, tail);
    }
    else
    {
        _snwprintf(args, 2048, L"set %s \"%s\"%s", color, folder, tail);
    }
    args[2047] = 0;

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    wchar_t cmd[4096];
    _snwprintf(cmd, 4096, L"\"%s\" %s", exe, args);
    cmd[4095] = 0;

    if (CreateProcessW(exe, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
    {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

/* 管理台是全局窗口，不需要选中的文件夹路径 */
static void run_ui(void)
{
    wchar_t exe[MAX_PATH];
    file_in_dir(exe, MAX_PATH, L"FolderMarker.exe");
    if (GetFileAttributesW(exe) == INVALID_FILE_ATTRIBUTES) { return; }

    wchar_t cmd[4096];
    _snwprintf(cmd, 4096, L"\"%s\" ui", exe);
    cmd[4095] = 0;

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    if (CreateProcessW(exe, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
    {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

/* ---------------- 子命令对象 ---------------- */

/* 颜色分组的 9 个子项：7 个纯色 + 自定义颜色 + 恢复上次颜色。
   新版菜单单层子菜单上限 16 项，9 项安全。所有标题都不带省略号。 */
static const wchar_t *g_titles[] = {L"红色", L"橙色", L"黄色", L"绿色", L"蓝色", L"紫色", L"灰色",
                                    L"自定义颜色", L"恢复上次颜色"};
static const wchar_t *g_colors[] = {L"red", L"orange", L"yellow", L"green", L"blue", L"purple", L"gray",
                                    L"pick", L"undo"};
static const wchar_t *g_icons[]  = {L"red.ico", L"orange.ico", L"yellow.ico", L"green.ico",
                                    L"blue.ico", L"purple.ico", L"gray.ico",
                                    L"pick.ico", L"undo.ico"};
#define COLOR_COUNT 9

static ULONG color_total(void) { return (ULONG)COLOR_COUNT; }

/* 3 个 Verb：1 个分组（颜色子菜单）+ 2 个动作。
   标记符号功能已按需求整体移除。color 为空表示分组根；
   非空表示动作（值传给 run_for_path）。 */
typedef struct
{
    const wchar_t *title;
    const wchar_t *icon;
    const wchar_t *color;
    int group;           // 0 = 颜色子项
    int has_children;
} VerbDef;

static const VerbDef g_verbs[VERB_COUNT] =
{
    {L"颜色",       L"palette.ico",    L"",      0, 1},
    {L"清除标记",   L"clear.ico",      L"none",  0, 0},
    {L"管理",       L"manage.ico",     L"manage",0, 0}
};

static ULONG STDMETHODCALLTYPE cmd_addref(Cmd *self) { return (ULONG)InterlockedIncrement(&self->ref); }

static ULONG STDMETHODCALLTYPE cmd_release(Cmd *self)
{
    LONG n = InterlockedDecrement(&self->ref);
    if (n == 0) { CoTaskMemFree(self); }
    return (ULONG)n;
}

static HRESULT STDMETHODCALLTYPE cmd_query(Cmd *self, REFIID riid, void **ppv)
{
    if (!ppv) { return E_POINTER; }
    *ppv = NULL;
    logf("QueryInterface verb=%d iid=%08X", self->verb, riid ? ((const GUID *)riid)->Data1 : 0);
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IExplorerCommandLocal))
    {
        *ppv = self;
        cmd_addref(self);
        return S_OK;
    }
    return E_NOINTERFACE;
}

static HRESULT STDMETHODCALLTYPE cmd_title(Cmd *self, IShellItemArray *items, LPWSTR *out)
{
    (void)items;
    self->touched = 1;
    logf("GetTitle  parent=%d group=%d idx=%d title=%s", self->parent, self->group, self->index, U8(self->title));
    return dup_string(self->title, out);
}

static HRESULT STDMETHODCALLTYPE cmd_icon(Cmd *self, IShellItemArray *items, LPWSTR *out)
{
    (void)items;
    logf("GetIcon   parent=%d group=%d idx=%d icon=%s", self->parent, self->group, self->index, U8(self->icon));
    if (!self->icon) { *out = NULL; return S_OK; }
    wchar_t path[MAX_PATH];
    wchar_t rel[MAX_PATH];
    _snwprintf(rel, MAX_PATH, L"icons\\%s", self->icon);
    rel[MAX_PATH - 1] = 0;
    file_in_dir(path, MAX_PATH, rel);
    return dup_string(path, out);
}

static HRESULT STDMETHODCALLTYPE cmd_tooltip(Cmd *self, IShellItemArray *items, LPWSTR *out)
{
    (void)items;
    if (self->parent) { return dup_string(L"给文件夹加一点颜色，图标立即变色", out); }
    *out = NULL;
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE cmd_canonical(Cmd *self, GUID *guid)
{
    (void)guid;
    // 新版菜单从不调用本方法；经典菜单会调用它。
    // 置标记交给 GetState 隐藏，同时返回 E_NOTIMPL 禁掉缓存/简略路径。
    self->classic = 1;
    logf("Canonical verb=%d -> E_NOTIMPL (经典菜单交给注册表单入口)", self->verb);
    return E_NOTIMPL;
}

static HRESULT STDMETHODCALLTYPE cmd_state(Cmd *self, IShellItemArray *items, BOOL slow, int *state)
{
    (void)items; (void)slow;
    self->touched = 1;
    if (self->classic && self->verb >= 0)
    {
        *state = ECS_HIDDEN_;
        logf("GetState  verb=%d -> ECS_HIDDEN (经典菜单交给注册表单入口)", self->verb);
        return S_OK;
    }
    *state = ECS_ENABLED_;
    logf("GetState  parent=%d group=%d idx=%d -> ECS_ENABLED",
         self->parent, self->group, self->index);
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE cmd_invoke(Cmd *self, IShellItemArray *items, IBindCtx *bc)
{
    (void)bc;
    logf("Invoke    parent=%d group=%d idx=%d color=%s",
         self->parent, self->group, self->index, U8(self->color));

    if (!self->parent && self->color && wcscmp(self->color, L"manage") == 0)
    {
        run_ui();
        return S_OK;
    }
    if (!self->parent && items)
    {
        DWORD count = 0;
        if (SUCCEEDED(items->lpVtbl->GetCount(items, &count)))
        {
            int quiet = (count > 1);

            for (DWORD i = 0; i < count; i++)
            {
                IShellItem *item = NULL;
                if (FAILED(items->lpVtbl->GetItemAt(items, i, &item)) || !item) { continue; }
                LPWSTR path = NULL;
                if (self->color && *self->color &&
                    SUCCEEDED(item->lpVtbl->GetDisplayName(item, SIGDN_FILESYSPATH, &path)) && path)
                {
                    run_for_path(path, self->color, quiet);
                    CoTaskMemFree(path);
                }
                item->lpVtbl->Release(item);
            }
        }
    }
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE cmd_flags(Cmd *self, int *flags)
{
    // 经典菜单两条路径都以 GetFlags 作为根 Verb 的第一个调用；
    // 新版菜单首个必定是 GetState 或 GetTitle。此处返回 E_NOTIMPL，
    // 让 Explorer 在插入菜单之前就放弃这个 verb。
    if (!self->touched && self->verb >= 0)
    {
        self->touched = 1;
        self->classic = 1;
        *flags = 0;
        logf("GetFlags  verb=%d -> E_NOTIMPL (经典菜单首个调用，交给注册表单入口)", self->verb);
        return E_NOTIMPL;
    }
    self->touched = 1;
    *flags = self->parent ? ECF_HASSUBCOMMANDS_ : 0;
    logf("GetFlags  parent=%d group=%d idx=%d -> 0x%X (%s)",
         self->parent, self->group, self->index, *flags,
         *flags ? "ECF_HASSUBCOMMANDS" : "ECF_DEFAULT");
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE cmd_enum(Cmd *self, void **out);

static const CmdVtbl g_cmdVtbl =
{
    cmd_query, cmd_addref, cmd_release,
    cmd_title, cmd_icon, cmd_tooltip, cmd_canonical,
    cmd_state, cmd_invoke, cmd_flags, cmd_enum
};

/* ---------------- 枚举器 ---------------- */

static ULONG STDMETHODCALLTYPE enum_addref(EnumCmd *self) { return (ULONG)InterlockedIncrement(&self->ref); }

static ULONG STDMETHODCALLTYPE enum_release(EnumCmd *self)
{
    LONG n = InterlockedDecrement(&self->ref);
    if (n == 0) { CoTaskMemFree(self); }
    return (ULONG)n;
}

static HRESULT STDMETHODCALLTYPE enum_query(EnumCmd *self, REFIID riid, void **ppv)
{
    if (!ppv) { return E_POINTER; }
    *ppv = NULL;
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IEnumExplorerCommandLocal))
    {
        *ppv = self;
        enum_addref(self);
        return S_OK;
    }
    return E_NOINTERFACE;
}

static ULONG group_total(int group)
{
    (void)group;
    return color_total();
}

static Cmd *make_child(int index, int group)
{
    Cmd *cmd = (Cmd *)CoTaskMemAlloc(sizeof(Cmd));
    if (!cmd) { return NULL; }
    ZeroMemory(cmd, sizeof(Cmd));
    cmd->vtbl = &g_cmdVtbl;
    cmd->ref = 1;
    cmd->verb = -1;
    cmd->index = index;
    cmd->group = group;

    cmd->title = g_titles[index];
    cmd->icon = g_icons[index];
    cmd->color = g_colors[index];

    if (!cmd->title) { cmd->title = L""; }
    if (!cmd->icon) { cmd->icon = L"menu.ico"; }
    if (!cmd->color) { cmd->color = L""; }
    return cmd;
}

static HRESULT STDMETHODCALLTYPE enum_next(EnumCmd *self, ULONG celt, void **rgelt, ULONG *fetched)
{
    ULONG n = 0;
    while (n < celt && self->index < group_total(self->group))
    {
        Cmd *child = make_child((int)self->index, self->group);
        if (!child) { break; }
        rgelt[n] = child;              // 已持有 1 个引用，交给调用方
        self->index++;
        n++;
    }
    if (fetched) { *fetched = n; }
    logf("  Next group=%d -> 返回 %lu 项", self->group, n);
    return (n == celt) ? S_OK : S_FALSE;
}

static HRESULT STDMETHODCALLTYPE enum_skip(EnumCmd *self, ULONG celt)
{
    self->index += celt;
    if (self->index > group_total(self->group)) { self->index = group_total(self->group); }
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE enum_reset(EnumCmd *self)
{
    self->index = 0;
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE enum_clone(EnumCmd *self, void **out)
{
    EnumCmd *copy = (EnumCmd *)CoTaskMemAlloc(sizeof(EnumCmd));
    if (!copy) { return E_OUTOFMEMORY; }
    copy->vtbl = self->vtbl;
    copy->ref = 1;
    copy->index = self->index;
    copy->group = self->group;
    *out = copy;
    return S_OK;
}

static const EnumVtbl g_enumVtbl = {enum_query, enum_addref, enum_release, enum_next, enum_skip, enum_reset, enum_clone};

static HRESULT STDMETHODCALLTYPE cmd_enum(Cmd *self, void **out)
{
    // 只有父命令有子菜单；子命令返回一个立即结束的空枚举（比 E_NOTIMPL 更稳妥）。
    EnumCmd *e = (EnumCmd *)CoTaskMemAlloc(sizeof(EnumCmd));
    if (!e) { *out = NULL; return E_OUTOFMEMORY; }
    e->vtbl = &g_enumVtbl;
    e->ref = 1;
    e->group = self->group;
    e->index = self->parent ? 0 : group_total(self->group);

    logf("EnumSubCommands parent=%d index=%d group=%d -> 从 0 枚举 %lu 项",
         self->parent, self->index, self->group, group_total(e->group));

    *out = e;
    return S_OK;
}

/* ---------------- 类工厂 ---------------- */

typedef struct
{
    const void *vtbl;
    LONG ref;
    int kind;            // 0 = 颜色根命令(B41)，1 = 符号根命令(B42)
} Factory;

static HRESULT STDMETHODCALLTYPE fac_query(Factory *self, REFIID riid, void **ppv);
static ULONG STDMETHODCALLTYPE fac_addref(Factory *self) { return (ULONG)InterlockedIncrement(&self->ref); }
static ULONG STDMETHODCALLTYPE fac_release(Factory *self)
{
    LONG n = InterlockedDecrement(&self->ref);
    if (n == 0) { CoTaskMemFree(self); }
    return (ULONG)n;
}

static HRESULT STDMETHODCALLTYPE fac_create(Factory *self, IUnknown *outer, REFIID riid, void **ppv)
{
    (void)self; (void)outer;
    if (!ppv) { return E_POINTER; }
    *ppv = NULL;

    Cmd *cmd = (Cmd *)CoTaskMemAlloc(sizeof(Cmd));
    if (!cmd) { return E_OUTOFMEMORY; }
    ZeroMemory(cmd, sizeof(Cmd));
    cmd->vtbl = &g_cmdVtbl;
    cmd->ref = 1;
    cmd->verb = self->kind;

    const VerbDef *v = &g_verbs[self->kind];
    cmd->parent = v->has_children;   // 分组根 = 1（有子菜单），动作 = 0
    cmd->group = v->group;
    cmd->title = v->title;
    cmd->icon = v->icon;
    cmd->color = v->color;

    HRESULT hr = cmd_query(cmd, riid, ppv);
    cmd_release(cmd);
    return hr;
}

static HRESULT STDMETHODCALLTYPE fac_lock(Factory *self, BOOL lock)
{
    (void)self; (void)lock;
    return S_OK;
}

static const struct
{
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(Factory *, REFIID, void **);
    ULONG   (STDMETHODCALLTYPE *AddRef)(Factory *);
    ULONG   (STDMETHODCALLTYPE *Release)(Factory *);
    HRESULT (STDMETHODCALLTYPE *CreateInstance)(Factory *, IUnknown *, REFIID, void **);
    HRESULT (STDMETHODCALLTYPE *LockServer)(Factory *, BOOL);
} g_factoryVtbl = {fac_query, fac_addref, fac_release, fac_create, fac_lock};

static HRESULT STDMETHODCALLTYPE fac_query(Factory *self, REFIID riid, void **ppv)
{
    if (!ppv) { return E_POINTER; }
    *ppv = NULL;
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IClassFactory))
    {
        *ppv = self;
        fac_addref(self);
        return S_OK;
    }
    return E_NOINTERFACE;
}

/* ---------------- 导出 ---------------- */

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = inst;
        DisableThreadLibraryCalls(inst);
    }
    return TRUE;
}

HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID riid, void **ppv)
{
    if (!ppv) { return E_POINTER; }
    *ppv = NULL;

    int kind = -1;
    for (int i = 0; i < VERB_COUNT; i++)
    {
        if (IsEqualCLSID(clsid, &g_clsids[i])) { kind = i; break; }
    }
    if (kind < 0) { return CLASS_E_CLASSNOTAVAILABLE; }

    Factory *f = (Factory *)CoTaskMemAlloc(sizeof(Factory));
    if (!f) { return E_OUTOFMEMORY; }
    f->vtbl = &g_factoryVtbl;
    f->ref = 1;
    f->kind = kind;

    logf("DllGetClassObject kind=%d", kind);

    HRESULT hr = fac_query(f, riid, ppv);
    fac_release(f);
    return hr;
}

HRESULT __stdcall DllCanUnloadNow(void)
{
    return S_FALSE;
}
