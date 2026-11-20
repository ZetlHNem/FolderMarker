using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

// 自检 / 诊断：回归测试（selftest）和 diagnose.txt 的生成。
// 从 FolderMarker.cs 拆出来 —— 原文件 6500 行，改个自检要滚很久。

internal static partial class Program
{
    private static System.IO.StringWriter _diagFile;

    /// <summary>诊断输出：winexe 要先 AttachConsole 才能打到调用方的终端；同时落盘一份</summary>
    private static void DiagLine(string line)
    {
        try { Console.WriteLine(line); } catch { }
        if (_diagFile != null) { _diagFile.WriteLine(line); }
    }

    private static void DiagBegin()
    {
        try { AttachConsole(-1); } catch { }
        _diagFile = new System.IO.StringWriter();
    }

    private static void DiagEnd()
    {
        if (_diagFile == null) { return; }
        try
        {
            string path = Path.Combine(DataDir(), "diagnose.txt");
            File.WriteAllText(path, _diagFile.ToString(), new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("完整报告已保存到: " + path);
        }
        catch { }
        _diagFile = null;
    }

    /// <summary>手工解析 ico 的目录项，逐帧输出尺寸/格式/四角 alpha</summary>
    private static void DiagIcoFrames(string icoPath)
    {
        try
        {
            byte[] b = File.ReadAllBytes(Io(icoPath));
            if (b.Length < 6)
            {
                DiagLine("  ico 文件过小/损坏: " + icoPath);
                return;
            }
            int cnt = BitConverter.ToUInt16(b, 4);
            DiagLine("  ico: " + icoPath + "  帧数=" + cnt);
            for (int i = 0; i < cnt; i++)
            {
                int off = 6 + 16 * i;
                int dim = b[off] == 0 ? 256 : b[off];
                int size = BitConverter.ToInt32(b, off + 8);
                int pos = BitConverter.ToInt32(b, off + 12);
                string fmt = "BMP";
                if (pos >= 0 && pos + 8 <= b.Length &&
                    b[pos] == 0x89 && b[pos + 1] == 0x50)
                {
                    fmt = "PNG";
                }
                string corners = "?";
                try
                {
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream(b, pos, size, false))
                    using (Bitmap bm = new Bitmap(ms))
                    {
                        corners = bm.GetPixel(0, 0).A + "/" +
                                  bm.GetPixel(bm.Width - 1, 0).A + "/" +
                                  bm.GetPixel(0, bm.Height - 1).A + "/" +
                                  bm.GetPixel(bm.Width - 1, bm.Height - 1).A;
                        DiagLine("    帧" + i + ": " + dim + "px " + fmt +
                                 " 四角alpha=" + corners);
                    }
                }
                catch (Exception ex)
                {
                    DiagLine("    帧" + i + ": " + dim + "px " + fmt + " 解码失败 " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            DiagLine("  ico 读取失败: " + ex.Message);
        }
    }

    private static int RunDiagnose(string folder)
    {
        DiagBegin();
        try
        {
            DiagLine("=== FolderMarker 诊断  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            DiagLine("程序版本: " + AppVersion);
            DiagLine("数据目录: " + DataDirPath());

            // 纯壳索引状态
            try
            {
                string idx = File.Exists(Io(ShellIdxCachePath()))
                    ? File.ReadAllText(Io(ShellIdxCachePath()), Encoding.UTF8).Trim()
                    : "(无缓存)";
                DiagLine("纯壳索引缓存: " + idx + "  进程内生效: " +
                         _shellBackIdx + "/" + _shellFrontIdx);
            }
            catch { }

            DiagLine("标记清单条数: " + ReadMarked().Count);
            DiagLine("");

            if (string.IsNullOrEmpty(folder))
            {
                DiagLine("提示: 带上文件夹路径可以看单个文件夹的详细状态，例如");
                DiagLine("  FolderMarker.exe diagnose <文件夹路径>");

                return 0;
            }

            folder = Path.GetFullPath(Plain(folder));
            DiagLine("--- 文件夹: " + folder);
            bool dirExists = Directory.Exists(Io(folder));
            DiagLine("存在: " + dirExists);
            if (!dirExists) { return 1; }

            string ini = Path.Combine(Io(folder), "desktop.ini");
            DiagLine("desktop.ini 存在: " + File.Exists(ini));
            if (File.Exists(ini))
            {
                DiagLine(".fm_tmp 残留: " + File.Exists(ini + ".fm_tmp"));
                try
                {
                    byte[] head = File.ReadAllBytes(ini);
                    DiagLine("ini 编码: " + (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE
                        ? "UTF-16 LE (正常)" : "非 UTF-16 (异常!)"));
                }
                catch { }

                string iconRes = ReadIconResourceRaw(ini) ?? "";
                DiagLine("IconResource: " + (iconRes.Length > 0 ? iconRes : "(无)"));

                string iconName, color;
                bool isCache;
                if (TryReadMarkedIcon(ini, out iconName, out color, out isCache))
                {
                    DiagLine("解析: " + (isCache ? "本机缓存图标" : "安装目录纯色图标") +
                             " 颜色=" + color + " 文件=" + iconName);
                }

                FolderState st = LoadState(ini);
                DiagLine("FolderMarkerState: " + (st.Valid ? "有效 原图标=" + st.PrevIcon : "(无)"));

                if (isCache)
                {
                    DiagIcoFrames(Path.Combine(CacheIconDir(), iconName));
                }
            }

            // 内容签名 + 原生图缓存
            string sig = ComputeSignature(folder, null);
            DiagLine("内容签名: " + (sig.Length > 0 ? sig.Replace("\t", " | ") : "(空)"));
            string nativePath = NativeCachePath(folder);
            DiagLine("原生图缓存: " + (File.Exists(Io(nativePath)) ? "存在" : "无"));
            if (File.Exists(Io(nativePath)))
            {
                string cachedSig = ReadCacheSig(nativePath);
                DiagLine("  缓存签名" + (cachedSig == sig ? "一致" : "不一致(将重抓)"));
            }

            // 实时抓一遍，给硬分类统计
            try
            {
                Bitmap shell = LoadShell();
                if (shell != null)
                {
                    using (Bitmap probe = GrabNativeSandwich(folder))
                    {
                        if (probe == null)
                        {
                            DiagLine("实时抓取: 失败（shell 给不出原版缩略图）");
                        }
                        else
                        {
                            bool ok = IsPlausibleSandwichNative(probe, shell);
                            DiagLine("实时抓取: " + probe.Width + "x" + probe.Height +
                                     "  三明治判定=" + (ok ? "通过" : "不通过"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLine("实时抓取异常: " + ex.Message);
            }

            DiagLine("");
            DiagLine("最近日志: " + Path.Combine(DataDirPath(), "refresh.log"));
            return 0;
        }
        finally
        {
            DiagEnd();
        }
    }

    /// <summary>自检用：给临时文件夹里放一张会出三明治预览的测试图</summary>
    private static void WriteTestImage(string dir)
    {
        WriteTestImageNamed(dir, "a.png");
    }

    private static void WriteTestImageNamed(string dir, string name)
    {
        using (Bitmap test = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(test))
            {
                g.Clear(Color.FromArgb(70, 130, 200));
                using (SolidBrush br = new SolidBrush(Color.FromArgb(240, 180, 60)))
                {
                    g.FillEllipse(br, 8, 8, 48, 48);
                }
            }
            test.Save(Io(Path.Combine(dir, name)), ImageFormat.Png);
        }
    }

    private static int RunSelfTest()
    {
        Environment.SetEnvironmentVariable("FM_QUIET", "1");
        DiagBegin();
        int pass = 0, fail = 0;
        List<string> made = new List<string>();
        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        try
        {
            DiagLine("=== FolderMarker 自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

            Action<string, bool> check = delegate(string name, bool ok)
            {
                if (ok) { pass++; } else { fail++; }
                DiagLine((ok ? "  PASS  " : "  FAIL  ") + name);
            };

            // --- 场景1: 有内容文件夹 标色 → 三明治 ---
            string d1 = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Io(d1));
            made.Add(d1);
            WriteTestImage(d1);

            int rc = Main(new string[] { "set", "green", d1, "quiet" });
            check("标色 green 返回 0", rc == 0);
            string ini1 = Path.Combine(Io(d1), "desktop.ini");
            string iconRes1 = ReadIconResourceRaw(ini1) ?? "";
            check("ini 写入缓存图标", iconRes1.IndexOf("cache" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                     iconRes1.IndexOf("-green.ico", StringComparison.OrdinalIgnoreCase) >= 0);
            check("ini 是 UTF-16 LE", File.ReadAllBytes(ini1)[0] == 0xFF && File.ReadAllBytes(ini1)[1] == 0xFE);
            string ico1 = Path.Combine(CacheIconDir(), CacheIconName(d1, "green"));
            check("缓存 ico 存在", File.Exists(Io(ico1)));
            check("native 缓存建立", File.Exists(Io(NativeCachePath(d1))));

            // 帧检查
            bool framesOk = false;
            try
            {
                byte[] b = File.ReadAllBytes(Io(ico1));
                int cnt = BitConverter.ToUInt16(b, 4);
                framesOk = cnt >= 4;
                for (int i = 0; i < cnt && framesOk; i++)
                {
                    int off = 6 + 16 * i;
                    int pos = BitConverter.ToInt32(b, off + 12);
                    framesOk = b[pos] == 0x89 && b[pos + 1] == 0x50;   // PNG 帧
                }
            }
            catch { framesOk = false; }
            check("ico ≥4 帧且全为 PNG", framesOk);

            // --- 场景2: 换色 ---
            rc = Main(new string[] { "set", "red", d1, "quiet" });
            check("换色 red 返回 0", rc == 0);
            string iconRes2 = ReadIconResourceRaw(ini1) ?? "";
            check("ini 已指向 red", iconRes2.IndexOf("-red.ico", StringComparison.OrdinalIgnoreCase) >= 0);
            check("旧 green ico 已清理", !File.Exists(Io(ico1)));
            check("native 缓存复用", File.Exists(Io(NativeCachePath(d1))));

            // --- 场景3: 清除 ---
            rc = Main(new string[] { "clear", "none", d1, "quiet" });
            check("清除返回 0", rc == 0);
            string iconRes3 = ReadIconResourceRaw(ini1) ?? "";
            check("ini 已无 IconResource", iconRes3.Length == 0);

            // --- 场景4: 空文件夹 → 纯色 ---
            string d2 = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Io(d2));
            made.Add(d2);
            rc = Main(new string[] { "set", "blue", d2, "quiet" });
            check("空文件夹标色返回 0", rc == 0);
            string iconRes4 = ReadIconResourceRaw(Path.Combine(Io(d2), "desktop.ini")) ?? "";
            check("空文件夹用安装目录纯色 ico", iconRes4.IndexOf("icons\\blue.ico", StringComparison.OrdinalIgnoreCase) >= 0);
            Main(new string[] { "clear", "none", d2, "quiet" });

            // --- 场景5: .fm_tmp 崩溃恢复 ---
            rc = Main(new string[] { "set", "green", d1, "quiet" });
            check("重新标色返回 0", rc == 0);
            string parked = ini1 + ".fm_tmp";
            File.Move(ini1, parked);   // 模拟「移走后进程被杀」
            RecoverParkedIni();
            check("fm_tmp 自动移回", File.Exists(ini1) && !File.Exists(parked));

            // --- 场景6: native 缓存失效重抓 ---
            try { File.Delete(Io(NativeCachePath(d1))); } catch { }
            rc = Main(new string[] { "set", "purple", d1, "quiet" });
            check("缓存被删后重建返回 0", rc == 0);
            check("native 缓存已重建", File.Exists(Io(NativeCachePath(d1))));

            Main(new string[] { "clear", "none", d1, "quiet" });

            // --- 场景7: FileSystemWatcher 即时响应 ---
            string d3 = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Io(d3));
            made.Add(d3);
            WriteTestImage(d3);
            Main(new string[] { "set", "yellow", d3, "quiet" });
            string ico3 = Path.Combine(CacheIconDir(), CacheIconName(d3, "yellow"));
            check("watcher场景 标色成功", File.Exists(Io(ico3)));

            RebuildWatchers(ReadMarkedNormalized());   // 含 d3（标色时已 AddMarked）
            DateTime wBefore = File.Exists(Io(ico3))
                ? File.GetLastWriteTimeUtc(Io(ico3)) : DateTime.MinValue;
            WriteTestImageNamed(d3, "0_first.png");    // 新内容文件 → 签名变 → watcher 应在 ~1s 内重建

            bool watcherOk = false;
            for (int i = 0; i < 20; i++)               // 最多等 6 秒
            {
                System.Threading.Thread.Sleep(300);
                if (File.Exists(Io(ico3)) && File.GetLastWriteTimeUtc(Io(ico3)) > wBefore)
                {
                    watcherOk = true;
                    break;
                }
            }
            RebuildWatchers(new List<string>());       // 收走 watcher
            check("watcher 3 秒级即时重建", watcherOk);
            Main(new string[] { "clear", "none", d3, "quiet" });

            // --- 场景8: 自定义颜色 ---
            string d4 = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Io(d4));
            made.Add(d4);
            WriteTestImage(d4);
            rc = Main(new string[] { "set", "cff8800", d4, "quiet" });
            check("自定义色标色返回 0", rc == 0);
            string iniC = Path.Combine(Io(d4), "desktop.ini");
            string iconResC = ReadIconResourceRaw(iniC) ?? "";
            check("自定义色写入缓存 ico",
                iconResC.IndexOf("-cff8800.ico", StringComparison.OrdinalIgnoreCase) >= 0 &&
                iconResC.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0);
            check("自定义 ico 存在",
                File.Exists(Io(Path.Combine(CacheIconDir(), CacheIconName(d4, "cff8800")))));

            // --- 场景9: 换色 + undo 恢复上次颜色 ---
            rc = Main(new string[] { "set", "blue", d4, "quiet" });
            check("换回预设 blue 返回 0", rc == 0);
            string iconResB = ReadIconResourceRaw(iniC) ?? "";
            check("ini 已指向 blue", iconResB.IndexOf("-blue.ico", StringComparison.OrdinalIgnoreCase) >= 0);

            rc = Main(new string[] { "undo", "none", d4, "quiet" });
            check("undo 返回 0", rc == 0);
            string iconResU = ReadIconResourceRaw(iniC) ?? "";
            check("undo 恢复成自定义色", iconResU.IndexOf("-cff8800.ico", StringComparison.OrdinalIgnoreCase) >= 0);

            // --- 场景10: 自定义色 + 空文件夹（纯色自定义也走缓存） ---
            string d5 = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Io(d5));
            made.Add(d5);
            rc = Main(new string[] { "set", "cff8800", d5, "quiet" });
            check("空文件夹自定义色返回 0", rc == 0);
            string iconResD5 = ReadIconResourceRaw(Path.Combine(Io(d5), "desktop.ini")) ?? "";
            check("空文件夹自定义色也在缓存",
                iconResD5.IndexOf("-cff8800.ico", StringComparison.OrdinalIgnoreCase) >= 0 &&
                iconResD5.IndexOf("icons", StringComparison.OrdinalIgnoreCase) < 0);
            Main(new string[] { "clear", "none", d4, "quiet" });
            Main(new string[] { "clear", "none", d5, "quiet" });

            DiagLine("");
        // --- 场景: 恢复上次颜色（undo）---
        string dUndo = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dUndo));
        made.Add(dUndo);
        Main(new string[] { "set", "red", dUndo, "quiet" });
        Main(new string[] { "set", "green", dUndo, "quiet" });
        int rcUndo = Main(new string[] { "undo", "none", dUndo, "quiet" });
        check("undo 返回 0", rcUndo == 0);
        string icoUndo = ReadIconResourceRaw(Path.Combine(Io(dUndo), "desktop.ini")) ?? "";
        check("undo 恢复成上一次的颜色", icoUndo.IndexOf("red.ico", StringComparison.OrdinalIgnoreCase) >= 0);

        // --- 场景: 自定义色 c<RRGGBB> ---
        string dCustom = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dCustom));
        made.Add(dCustom);
        int rcCustom = Main(new string[] { "set", "cff0000", dCustom, "quiet" });
        check("自定义色标记返回 0", rcCustom == 0);
        string icoCustom = ReadIconResourceRaw(Path.Combine(Io(dCustom), "desktop.ini")) ?? "";
        check("自定义色图标落在 cache", icoCustom.IndexOf("-cff0000.ico", StringComparison.OrdinalIgnoreCase) >= 0);

        // --- 场景: 路径带尾斜杠 ---
        string dSlash = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dSlash));
        made.Add(dSlash);
        int rcSlash = Main(new string[] { "set", "gray", dSlash + "\\", "quiet" });
        check("带尾斜杠的路径也能标记", rcSlash == 0);

        // --- 场景: 清除后 ini 删除、目录保留 ---
        int rcClear2 = Main(new string[] { "clear", "none", dCustom, "quiet" });
        check("清除返回 0", rcClear2 == 0);
        check("清除后 desktop.ini 已删除", !File.Exists(Io(Path.Combine(dCustom, "desktop.ini"))));
        check("清除后目录仍在", Directory.Exists(Io(dCustom)));

        // --- 场景: 没有图片/视频的文件夹只做纯色，不该建 native 缓存 ---
        string dPlain = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dPlain));
        made.Add(dPlain);
        File.WriteAllText(Io(Path.Combine(dPlain, "note.txt")), "x");
        int rcPlain = Main(new string[] { "set", "yellow", dPlain, "quiet" });
        check("纯文本文件夹也能标记", rcPlain == 0);
        check("纯文本文件夹不建 native 缓存", !File.Exists(Io(NativeCachePath(dPlain))));

        // --- 场景: 共享盘标记不能被误判成副本（回归：标记过段时间自动消失）---
        // UNC 路径必须识别成网络盘；副本判定要直接放行，否则用 UNC 标的文件夹
        // 在另一台电脑上会被当成「从别处复制来的」而自动 clear 掉。
        string uncSample = @"\\some-server\share\已标记文件夹";
        check("UNC 路径识别为网络盘", IsNetworkPath(uncSample));
        string ownerOut;
        check("UNC 路径不判为副本", !IsForeignMark(uncSample, Path.Combine(uncSample, "desktop.ini"), out ownerOut));

        // 记录里的源路径在共享盘上（UNC）时，即使从别处访问也不判副本
        string dNetCopy = Path.Combine(Path.GetTempPath(), "FMTest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dNetCopy));
        made.Add(dNetCopy);
        int rcNet = Main(new string[] { "set", "blue", dNetCopy, "quiet" });
        check("网络源路径场景可正常标记", rcNet == 0);
        string netIni = Path.Combine(Io(dNetCopy), "desktop.ini");
        // 手工把 State 的 Path 改成一个 UNC 路径，模拟「共享盘同步来的标记」
        List<string> netLines = ReadIni(netIni);
        for (int i = 0; i < netLines.Count; i++)
        {
            if (netLines[i].Trim().StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            { netLines[i] = "Path=" + uncSample; break; }
        }
        WriteIni(netIni, netLines);
        check("源路径在共享盘时不判为副本", !IsForeignMark(dNetCopy, netIni, out ownerOut));

        // 本地文件夹从另一个本地路径复制来的，仍要能识别成副本（别改坏原功能）
        string dSrc = Path.Combine(Path.GetTempPath(), "FMTest-src-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string dCopy = Path.Combine(Path.GetTempPath(), "FMTest-cp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Io(dSrc));
        Directory.CreateDirectory(Io(dCopy));
        made.Add(dSrc); made.Add(dCopy);
        int rcSrc = Main(new string[] { "set", "red", dSrc, "quiet" });
        check("副本判定-源标记成功", rcSrc == 0);
        File.Copy(Io(Path.Combine(dSrc, "desktop.ini")), Io(Path.Combine(dCopy, "desktop.ini")), true);
        check("本地副本仍判为副本", IsForeignMark(dCopy, Path.Combine(dCopy, "desktop.ini"), out ownerOut));

        // --- 场景: 在线更新的版本比较与 JSON 解析（不联网）---
        check("版本比较-更新版更新", IsNewerVersion("v2.3.9", "2.3.8"));
        check("版本比较-当前版不更新", !IsNewerVersion("v2.3.8", "2.3.8"));
        check("版本比较-旧版不更新", !IsNewerVersion("v2.3.7", "2.3.8"));
        check("版本比较-跨大版本", IsNewerVersion("v3.0.0", "2.9.9"));
        // 用「带 uploader 嵌套对象」的真实结构，覆盖曾经的 bug：
        // asset 内嵌 uploader:{...} 时，旧正则 \{[^{}]*\} 匹配不到，导致检查失败
        string fakeJson = "{\"tag_name\":\"v2.4.0\",\"assets\":[{\"name\":\"setup.exe\",\"browser_download_url\":\"https://x/setup.exe\",\"uploader\":{\"login\":\"u\",\"id\":1}}]}";
        check("更新JSON-版本号解析", ExtractJsonString(fakeJson, "tag_name") == "v2.4.0");
        check("更新JSON-下载链接解析", ExtractExeAssetUrl(fakeJson) == "https://x/setup.exe");
        // 主通道：github.com 302 重定向的 Location 解析（免 API 额度，抗限流）
        check("重定向-tag解析", TagFromLocation("https://github.com/ZetlHNem/FolderMarker/releases/tag/v2.4.1") == "v2.4.1");
        check("重定向-下载链接拼接", SetupUrlFor("v2.4.1") ==
            "https://github.com/ZetlHNem/FolderMarker/releases/download/v2.4.1/FolderMarker-setup-v2.4.1.exe");

            DiagLine("=== 结果: " + pass + " 通过 / " + fail + " 失败 ===");
        }
        catch (Exception ex)
        {
            DiagLine("自检异常: " + ex.Message);
            fail++;
        }
        finally
        {
            foreach (string d in made)
            {
                try { Directory.Delete(Io(d), true); } catch { }
            }
        }

        DiagEnd();
        return fail == 0 ? 0 : 1;
    }
}
