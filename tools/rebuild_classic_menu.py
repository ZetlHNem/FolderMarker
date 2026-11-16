"""重建经典菜单注册表子树（4 个二级项 + 两个三级分组）。

经典菜单的「文件夹标记 >」单入口由这份注册表 cascade 提供；
新版菜单由 sparse 包的 4 个 COM Verb 提供（DLL 让经典菜单跳过它们）。
- 先把整棵子树导出成文本备份（build/classic_menu_backup.txt）
- 删除并重新按规范写入（默认值保持空、SubCommands 保持空字符串）
- 不触碰 sparse 包与新版菜单

用法：python tools/rebuild_classic_menu.py [--backup-only]
"""
import os
import sys
import winreg

ROOT = r"Software\Classes\Directory\shell\FolderMarker"
APP = os.path.join(os.environ["LOCALAPPDATA"], "FolderMarker")
EXE = os.path.join(APP, "FolderMarker.exe")
ICONS = os.path.join(APP, "icons")

# (键名, MUIVerb, Icon, command 或 None(表示级联), 子项列表)
# 第二层正好 4 项 —— 这个数量在实测中能稳定全部渲染。
# 「自定义颜色」「恢复上次颜色」收进颜色三级；「清除标记」「管理」留在外面。
# 所有标题都不带省略号。
ITEMS = [
    # ===== 预算规律（本机 Explorer 实测）：根级联自动枚举的整棵子树
    # 最多渲染 14 个条目（14 可达、15 被丢弃），安全线 ≤13。
    # 当前分配：颜色9 + 清除1 + 管理1 = 11。标记符号已整体移除。
    # 「清理副本标记」已从右键菜单移除（自动清理每分钟跑，手动入口保留在托盘菜单）。
    ("1_colors", "颜色", "palette.ico", None,
     [("1_red", "红色", "red.ico", "set red"),
      ("2_orange", "橙色", "orange.ico", "set orange"),
      ("3_yellow", "黄色", "yellow.ico", "set yellow"),
      ("4_green", "绿色", "green.ico", "set green"),
      ("5_blue", "蓝色", "blue.ico", "set blue"),
      ("6_purple", "紫色", "purple.ico", "set purple"),
      ("7_gray", "灰色", "gray.ico", "set gray"),
      ("8_pick", "自定义颜色", "pick.ico", "pick none"),
      ("9_undo", "恢复上次颜色", "undo.ico", "undo none")]),
    ("2_clear", "清除标记", "clear.ico", "clear none", []),
    ("3_manage", "管理", "manage.ico", "ui", []),
]


def dump_tree(key, path, out):
    i = 0
    while True:
        try:
            name = winreg.EnumKey(key, i)
        except OSError:
            break
        i += 1
        sub = winreg.OpenKey(key, name)
        vals = []
        j = 0
        while True:
            try:
                n, v, t = winreg.EnumValue(sub, j)
            except OSError:
                break
            vals.append((n, v, t))
            j += 1
        out.append(f"[{path}\\{name}]")
        for n, v, t in vals:
            out.append(f"  {n!r} = {v!r} (type {t})")
        dump_tree(sub, path + "\\" + name, out)
        winreg.CloseKey(sub)


def backup():
    out = []
    try:
        root = winreg.OpenKey(winreg.HKEY_CURRENT_USER, ROOT)
    except FileNotFoundError:
        return None
    vals = []
    j = 0
    while True:
        try:
            n, v, t = winreg.EnumValue(root, j)
        except OSError:
            break
        vals.append((n, v, t))
        j += 1
    out.append(f"[{ROOT}]")
    for n, v, t in vals:
        out.append(f"  {n!r} = {v!r} (type {t})")
    dump_tree(root, ROOT, out)
    winreg.CloseKey(root)
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = os.path.join(here, "build", "classic_menu_backup.txt")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    return p


def setv(key, name, value):
    winreg.SetValueEx(key, name, 0, winreg.REG_SZ, value)


def delete_tree(path):
    """winreg.DeleteKeyEx 不递归，必须自底向上删。"""
    try:
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, path, 0, winreg.KEY_ALL_ACCESS)
    except FileNotFoundError:
        return
    names = []
    i = 0
    while True:
        try:
            names.append(winreg.EnumKey(k, i))
        except OSError:
            break
        i += 1
    winreg.CloseKey(k)
    for n in names:
        delete_tree(path + "\\" + n)
    winreg.DeleteKeyEx(winreg.HKEY_CURRENT_USER, path, winreg.KEY_WOW64_64KEY, 0)


def build():
    delete_tree(ROOT)

    root = winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, ROOT, 0, winreg.KEY_ALL_ACCESS)
    winreg.SetValueEx(root, "", 0, winreg.REG_SZ, "")
    setv(root, "MUIVerb", "文件夹标记")
    setv(root, "Icon", os.path.join(ICONS, "menu.ico"))
    setv(root, "SubCommands", "")   # 空值 = 自动枚举 shell\ 子键

    shell = winreg.CreateKey(root, "shell")
    for name, muiverb, icon, command, children in ITEMS:
        k = winreg.CreateKey(shell, name)
        winreg.SetValueEx(k, "", 0, winreg.REG_SZ, "")
        setv(k, "MUIVerb", muiverb)
        setv(k, "Icon", os.path.join(ICONS, icon))
        if command is None:
            setv(k, "SubCommands", "")
            sub = winreg.CreateKey(k, "shell")
            for cname, cverb, cicon, args in children:
                ck = winreg.CreateKey(sub, cname)
                winreg.SetValueEx(ck, "", 0, winreg.REG_SZ, "")
                setv(ck, "MUIVerb", cverb)
                setv(ck, "Icon", os.path.join(ICONS, cicon))
                ckcmd = winreg.CreateKey(ck, "command")
                winreg.SetValueEx(ckcmd, "", 0, winreg.REG_SZ,
                                  f'"{EXE}" {args} "%1"' if args != "ui" else f'"{EXE}" ui')
                winreg.CloseKey(ckcmd)
                winreg.CloseKey(ck)
            winreg.CloseKey(sub)
        else:
            # 三级叶子项：与 colors/badge 现有子项同构（(默认) 空 + MUIVerb）。
            # 注意：三级里这种叶子能渲染（颜色子菜单 9 项为证），
            # 但根级联的自动枚举会在最后一个级联项后截断，叶子放根级联末尾无效。
            cmd = winreg.CreateKey(k, "command")
            winreg.SetValueEx(cmd, "", 0, winreg.REG_SZ,
                              f'"{EXE}" {command} "%1"' if command != "ui" else f'"{EXE}" ui')
            winreg.CloseKey(cmd)
        winreg.CloseKey(k)


    winreg.CloseKey(shell)
    winreg.CloseKey(root)


if __name__ == "__main__":
    p = backup()
    print("备份:", p)
    if "--backup-only" not in sys.argv:
        build()
        print("已重建 4 个二级项 / 2 个三级分组")
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, ROOT + r"\shell")
        n = 0
        while True:
            try:
                name = winreg.EnumKey(k, n)
            except OSError:
                break
            print("  ", name)
            n += 1
        print("共", n, "项")
