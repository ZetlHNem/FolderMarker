// 发 SHCNE_ASSOCCHANGED | SHCNF_FLUSH，强制 Explorer 重读文件关联与静态 verb 表。
// 光重启 explorer.exe 不足以清掉缓存的静态级联菜单结构。
// 安装器 FolderMarker.iss 里的 NotifyShellAssocChanged 做的是同一件事。
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#ifndef SHCNE_ASSOCCHANGED
#define SHCNE_ASSOCCHANGED 0x08000000L
#endif
#ifndef SHCNF_FLUSH
#define SHCNF_FLUSH 0x1000
#endif

int main(void)
{
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, NULL, NULL);
    printf("SHCNE_ASSOCCHANGED + SHCNF_FLUSH sent\n");
    return 0;
}
