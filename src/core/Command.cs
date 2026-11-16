using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

// 单文件夹命令（set / clear / undo / pick）的实现。
// Main 只做分发；路径校验、图标选择、ini 写入、属性/时间戳还原都在这里。
// 从 FolderMarker.cs 拆出来 —— 这是整个程序最常改的一段。

internal static partial class Program
{
    /// <summary>
    /// 针对单个文件夹的命令（set / clear / undo / pick）。
    /// 路径校验、\?\ 长路径、返回码都收在这里，Main 只负责分发。
    /// </summary>
    private static int RunFolderCommand(string action, string color, string[] args)
    {
        string target = args[2];

        try
        {
            // 超过 MAX_PATH 的路径，.NET 的 Directory.Exists 会直接返回 false，
            // 只有加上 \\?\ 前缀才认。两种写法都试一次。
            if (!Directory.Exists(target) && !Directory.Exists(Io(target)))
            {
                // 以前这里静默 return 2，用户点完右键菜单毫无反应，根本不知道发生了什么
                ShowFailure("找不到这个文件夹：\r\n\r\n" + target + "\r\n\r\n它可能已经被删除、改名，或者所在磁盘/共享盘当前不可访问。");
                return 2;
            }

            string folder = Path.GetFullPath(target);
            if (folder.Length > 3)
            {
                folder = folder.TrimEnd('\\');
            }

            string ioFolder = Io(folder);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(ioFolder, "desktop.ini");

                        if (action == "set")
            {
                int ci;
                double cHue, cSat;
                if (!ResolveColor(color, out ci, out cHue, out cSat))
                {
                    ShowFailure("不支持的颜色：" + color + "\r\n\r\n预设 7 色，或 #RRGGBB 自定义色。");
                    return 3;
                }
                color = color.ToLowerInvariant();
                bool isCustom = ci < 0;

                // 自定义色没有预生成的内置 ico：把系统纯壳染色现做一颗，
                // 落在本机缓存（<hash>-c<hex>.ico），纯色和三明治共用此名。
                string iconPath;
                if (isCustom)
                {
                    iconPath = Path.Combine(CacheIconDir(), CacheIconName(folder, color));
                    if (!File.Exists(Io(iconPath)))
                    {
                        BuildPlainCustomIco(iconPath, cHue, cSat);
                    }
                }
                else
                {
                    iconPath = Path.Combine(Path.Combine(baseDir, "icons"), color + ".ico");
                    if (!File.Exists(Io(iconPath)))
                    {
                        ShowFailure("找不到内置图标文件：\r\n\r\n" + iconPath
                            + "\r\n\r\n安装目录里的 icons 被清理掉了，重新安装一次即可。");
                        return 4;
                    }
                }

                int hr;
                bool applied;
                bool usedCacheIcon = false;
                string cacheIcoPath = null;
                DateTime nudgeBase = DateTime.MinValue;
                using (PathLock(iniPath))
                {
                    FolderState before = FolderState.Capture(ioFolder);
                    FolderState stored = LoadState(iniPath);
                    // 记下当前颜色给「恢复上次颜色」用（换色/清除前都要）
                    string curToken, curIconName;
                    bool curIsCache;
                    if (TryReadMarkedIcon(iniPath, out curIconName, out curToken, out curIsCache)
                        && !string.IsNullOrEmpty(curToken))
                    {
                        // curToken 为空时绝不能写：会留下 0 字节的 .prev，
                        // 「恢复上次颜色」读到空值就永远失效，而且文件越积越多。
                        try
                        {
                            Directory.CreateDirectory(Io(Path.GetDirectoryName(PrevColorPath(folder))));
                            File.WriteAllText(Io(PrevColorPath(folder)), curToken, new UTF8Encoding(false));
                        }
                        catch { }
                    }

                    string prevIcon = ExtractIconResource(iniPath);   // 必须在 shell 覆盖之前读

                    // 换色时 ini 里已经是我们的图标，真正的原图标存在 [FolderMarkerState] 里
                    if (string.IsNullOrEmpty(prevIcon) && stored.Valid)
                    {
                        prevIcon = stored.PrevIcon;
                    }

                    // 含图片/视频的文件夹：按 Windows 的三层公式（imageres 5 背板 + 内容缩略图 + 6 前盖）
                    // 合成「彩色外壳 + 内容预览」。ico 只写进本机缓存，绝不落到共享盘文件夹里。
                    // 普通文件夹仍然直接用安装包里现成的纯色 ico。
                    string cacheIconName = null;
                    if (FindFirstMediaFile(folder) != null)
                    {
                        string cname = CacheIconName(folder, color);
                        string cpath = Path.Combine(CacheIconDir(), cname);
                        string built = TryBuildThumbnailIcon(folder, color, cpath);
                        if (built != null)
                        {
                            iconPath = built;
                            cacheIconName = cname;
                            cacheIcoPath = cpath;
                            usedCacheIcon = true;
                        }
                        else
                        {
                            DebugLog("thumb skip: 合成失败，退回纯色图标 " + folder);
                            if (isCustom)
                            {
                                // 自定义色：纯色 ico 也必须落在 cache 里
                                double ph = cHue;
                                double ps = cSat;
                                try { BuildPlainCustomIco(iconPath, ph, ps); } catch { }
                                cacheIconName = CacheIconName(folder, color);
                                cacheIcoPath = iconPath;
                                usedCacheIcon = true;
                            }
                        }
                    }

                    string icoName = Path.GetFileName(iconPath);
                    System.Diagnostics.Stopwatch swStep = System.Diagnostics.Stopwatch.StartNew();
                    hr = ApplyIcon(folder, iconPath);
                    long tApi = swStep.ElapsedMilliseconds;

                    // shell 也可能"返回成功但其实没写进去"（例如目录 ACL 拒绝写入，
                    // 它内部把错误吞掉了）。所以不看返回码，直接看 ini 里到底有没有这条图标。
                    if (hr != 0 || !MarkApplied(iniPath, icoName))
                    {
                        DebugLog("api set hr=0x" + hr.ToString("X8") + " no-icon-in-ini -> legacy for " + folder);
                        WriteMark(iniPath, iconPath, folder);
                    }

                    // SHGetSetFolderCustomSettings 写的是本机绝对路径（含用户名/盘符），
                    // 换台电脑就找不到文件。改写成 %LOCALAPPDATA% 环境变量形式：
                    // 两台电脑各自解析到自己的缓存/安装目录，共享盘上同一份 desktop.ini 都能用。
                    string envRel;
                    if (cacheIconName != null || isCustom)
                    {
                        if (cacheIconName == null)
                        {
                            // 自定义纯色：ico 也在 cache，让缓存+签名机制接管
                            cacheIconName = CacheIconName(folder, color);
                            cacheIcoPath = iconPath;
                            usedCacheIcon = true;
                        }
                        envRel = "cache" + Path.DirectorySeparatorChar + cacheIconName;
                    }
                    else
                    {
                        envRel = "icons" + Path.DirectorySeparatorChar + color + ".ico";
                    }

                    RewriteIconAsEnvPath(iniPath, icoName, envRel);

                    applied = MarkApplied(iniPath, icoName);

                    // 把「标记前」的属性、时间戳、以及用户原有的自定义图标记进 ini，
                    // 清除时才有据可依。只在第一次标记时写，换色不会覆盖原始记录。
                    SaveState(iniPath, before, prevIcon);

                    // 挪时间的基准：如果当前时间和「最初标记前」的记录只差几秒，
                    // 说明差的那点就是我们自己挪出来的，基准回到最初值，避免越换色越偏。
                    nudgeBase = before.WriteUtc;
                    if (stored.Valid)
                    {
                        double drift = (before.WriteUtc - stored.WriteUtc).TotalSeconds;
                        if (drift >= 0 && drift < 10)
                        {
                            nudgeBase = stored.WriteUtc;
                        }
                    }

                    // shell 会把文件夹标成「只读」并把时间戳刷成现在。
                    // 用户看属性面板会以为文件夹被动过，这里全部还原。
                    swStep.Restart();
                    KeepInvisible(ioFolder, before);



                    if (tApi > 80 || swStep.ElapsedMilliseconds > 80)
                    {
                        DebugLog("slow set api=" + tApi + "ms restore=" + swStep.ElapsedMilliseconds + "ms " + folder);
                    }
                }

                // 缩略图 / 图标缓存都以路径为键，统一通知一次让资源管理器重读。
                // 以前只在 hr != 0 时通知、其余靠「同名覆盖 ico 会自动换图」，
                // 实测「文件夹本来就有缩略图」时并不可靠。
                NotifyFolder(folder);

                // 通知之后再挪时间：顺序很关键。资源管理器的「文件夹缩略图缓存」
                // 键是「路径 + 修改时间（2 秒粒度）」，标记本身不会让它失效，
                // 旧的那张「黄壳 + 内容」会继续命中，用户以为图标没换。
                // 先通知（窗口重绘一次）再挪 2~4 秒，缓存立刻作废，下一次绘制
                // 就只能用我们写的图标。时间只显示到分钟，看不出来。
                if (nudgeBase != DateTime.MinValue)
                {
                    NudgeWriteTime(folder, nudgeBase, 1 + ValidColorIndex(color));
                }

                if (!applied)
                {
                    ShowFailure("无法为这个文件夹设置图标。\r\n\r\n可能是没有写入权限，或者这个位置（只读磁盘、网络位置）不支持自定义文件夹图标。");
                    return 6;
                }

                if (usedCacheIcon && cacheIcoPath != null)
                {
                    // 属性和时间戳在上面已还原（再往后挪了几毫秒），此时再写签名，
                    // 守护进程第一次巡检才不会因为 mtime 对不上白重画一次。
                    WriteCacheSig(cacheIcoPath, ContentSignature(folder));
                }

                // 记一笔，卸载时才能把这些文件夹还原（否则图标文件删了，
                // desktop.ini 还指着不存在的路径，文件夹会变成空白图标）。
                AddMarked(folder);

                return 0;
            }

            if (action == "clear")
            {
                bool cleared = ClearOne(folder);

                if (cleared)
                {
                    RemoveMarked(folder);
                    return 0;
                }

                ShowFailure("无法清除这个文件夹的标记。\r\n\r\n可能是没有写入权限，或者这个位置不支持修改文件夹图标。");
                return 6;
            }

            // 恢复上次颜色：把 set 前记下的颜色 token 原样再来一次 set
            if (action == "undo")
            {
                string prev = ReadPrevColor(folder);
                if (string.IsNullOrEmpty(prev))
                {
                    ShowFailure("没有可恢复的上次颜色。\r\n\r\n这个文件夹还没有换过色。");
                    return 1;
                }

                return Main(new string[] { "set", prev, folder, "quiet" });
            }


            // 自定义颜色：弹自绘的 WinUI 风格取色器（HSV 面板 + 预览 + 色板），
            // 取到的颜色转成 c<RRGGBB> token
            if (action == "pick")
            {
                string picked = ColorPickerWindow.Pick(folder);
                if (string.IsNullOrEmpty(picked))
                {
                    return 0;
                }
                return Main(new string[] { "set", picked, folder, "quiet" });
            }

            return 5;
        }
        catch (Exception ex)
        {
            TryLog(ex);
            return 99;
        }
    }
}
