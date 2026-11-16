using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ini 读写底层：命名互斥体串行化、UTF-16 LE 读写、读失败重试与自愈记录。
// 从 FolderMarker.cs 拆出来 —— 供标记/清理/巡检各层共用的文件 IO 原语。

internal static partial class Program
{

    private sealed class MutexScope : IDisposable
    {
        private readonly System.Threading.Mutex _m;

        public MutexScope(System.Threading.Mutex m)
        {
            _m = m;
        }

        public void Dispose()
        {
            try { _m.ReleaseMutex(); }
            catch { }
            try { _m.Dispose(); }
            catch { }
        }
    }

    /// <summary>
    /// 用命名互斥体把「改 desktop.ini」串行化。
    /// 不加锁的话，用户快速连点两次会有两个进程同时读改写，把文件写成乱码。
    /// </summary>
    private static MutexScope PathLock(string path)
    {
        uint h = 2166136261;
        string key = path.ToLowerInvariant();
        for (int i = 0; i < key.Length; i++)
        {
            h = (h ^ key[i]) * 16777619;
        }

        System.Threading.Mutex mtx = new System.Threading.Mutex(false, "FolderMarker_" + h.ToString("x8"));
        try
        {
            mtx.WaitOne(3000);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // 上一个进程异常退出，锁已归我们所有，继续即可
        }
        catch { }

        return new MutexScope(mtx);
    }

    /// <summary>
    /// 读 ini 并区分「读到了」和「读不到」。
    /// 清理幽灵条目时必须区分：共享盘抖动导致读失败，不能当成「用户已经清掉标记」。
    /// </summary>
    private static bool TryReadIni(string path, out List<string> lines)
    {
        lines = new List<string>();

        byte[] raw = ReadAllBytesShared(path);
        if (raw == null)
        {
            return false;
        }

        if (raw.Length == 0)
        {
            return true;   // 空文件：读到了，只是没内容
        }

        Encoding enc = Encoding.Default;
        int skip = 0;

        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            enc = Encoding.Unicode;
            skip = 2;
        }
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            enc = Encoding.BigEndianUnicode;
            skip = 2;
        }
        else if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            enc = Encoding.UTF8;
            skip = 3;
        }

        string text = enc.GetString(raw, skip, raw.Length - skip);

        // 防呆：文件若是被写坏的（夹着 0 字节），当作没有内容重新生成，
        // 免得把乱码继续传播下去。
        if (text.IndexOf('\0') >= 0)
        {
            return true;
        }

        string[] parts = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++)
        {
            // 自愈：跨机器同步的 desktop.ini 可能被旧版本写坏 ——
            // "[FolderMarkerState]" 直接粘在 "IconResource=...,0" 后面（没有换行）。
            // Windows 解析时会把 "0[FolderMarkerState]" 当进图标索引，整条
            // IconResource 作废，文件夹掉回默认缩略图（本机缓存明明存在）。
            // 读到时拆开；是否回写由 RefreshOne 里的 _iniHealedPaths 决定。
            int glue = parts[i].IndexOf(StateSection, StringComparison.OrdinalIgnoreCase);
            if (glue > 0 && !parts[i].TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                RecordIniHealed(path);
                lines.Add(parts[i].Substring(0, glue).TrimEnd());
                lines.Add(parts[i].Substring(glue));
            }
            else
            {
                lines.Add(parts[i]);
            }
        }

        return true;
    }

    /// <summary>TryReadIni 检测到 desktop.ini 粘行待修复的路径（Io 归一化后的路径）</summary>
    private static Dictionary<string, bool> _iniHealedPaths =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    private static void RecordIniHealed(string path)
    {
        try
        {
            lock (_iniHealedPaths)
            {
                _iniHealedPaths[Io(path)] = true;
            }
        }
        catch { }
    }

    private static bool TakeIniHealed(string path)
    {
        try
        {
            lock (_iniHealedPaths)
            {
                return _iniHealedPaths.Remove(Io(path));
            }
        }
        catch { }
        return false;
    }

    private static List<string> ReadIni(string path)
    {
        List<string> lines;
        if (TryReadIni(path, out lines))
        {
            return lines;
        }
        return new List<string>();
    }

    /// <summary>
    /// 允许共享读写地读取文件，并带重试。
    /// 用户快速连点两次时，另一个进程可能正在写 desktop.ini，
    /// 直接 File.ReadAllBytes 会抛"文件正由另一进程使用"。
    /// </summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        path = Io(path);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (MemoryStream ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(20 + attempt * 15);
            }
            catch (UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(20 + attempt * 15);
            }
        }

        return null;
    }

    private static void WriteIni(string path, List<string> lines)
    {
        path = Io(path);

        // desktop.ini 必须是 UTF-16 LE 且带 BOM，资源管理器才会解析
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            sb.Append("\r\n");
        }

        UnicodeEncoding enc = new UnicodeEncoding(false, true);
        byte[] body = enc.GetBytes(sb.ToString());
        byte[] preamble = enc.GetPreamble();
        byte[] bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                try { File.SetAttributes(path, FileAttributes.Normal); }
                catch { }

                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                           FileShare.Read))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush();
                }
                return;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(20 + attempt * 15);
            }
            catch (UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(20 + attempt * 15);
            }
        }
    }

    private static List<string> StripSection(List<string> lines, string sectionName)
    {
        List<string> result = new List<string>();
        bool skipping = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (!skipping && trimmed.Equals("[" + sectionName + "]", StringComparison.OrdinalIgnoreCase))
            {
                skipping = true;
                continue;
            }

            if (skipping)
            {
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    skipping = false;
                }
                else
                {
                    continue;
                }
            }

            result.Add(lines[i]);
        }

        return result;
    }

    private static void TryLog(Exception ex)
    {
        try
        {
            File.AppendAllText(
                Io(Path.Combine(LogDir(), "error.log")),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex.ToString() + "\r\n\r\n",
                Encoding.UTF8);
        }
        catch { }
    }


}
