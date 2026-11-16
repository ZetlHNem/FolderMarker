using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 清单层：marked.txt（标记清单）与 roots.txt（共享盘根）的读写维护。
// 从 FolderMarker.cs 拆出来 —— 只处理「哪些文件夹被标记/被配置」这份持久状态。

internal static partial class Program
{

    /// <summary>标记清单：%LOCALAPPDATA%\FolderMarker\marked.txt，一行一个文件夹</summary>
    private static string MarkedListPath()
    {
        return Path.Combine(DataDir(), "marked.txt");
    }

    /// <summary>
    /// 配置过的共享盘根目录。另一台电脑 import 一次就记下来，
    /// 守护进程定期扫它，A 电脑上新标的文件夹同步过来后 B 会自动跟上。
    /// </summary>
    private static string RootsListPath()
    {
        return Path.Combine(DataDir(), "roots.txt");
    }

    private static List<string> ReadRoots()
    {
        List<string> list = new List<string>();
        try
        {
            string file = Io(RootsListPath());
            if (!File.Exists(file))
            {
                return list;
            }

            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.Trim().Length > 0)
                {
                    list.Add(line.Trim());
                }
            }
        }
        catch { }

        return list;
    }

    private static void AddRoot(string root)
    {
        try
        {
            List<string> list = ReadRoots();
            foreach (string r in list)
            {
                if (string.Equals(r, root, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add(root);
            File.WriteAllLines(Io(RootsListPath()), list.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>管理台里删掉一个共享盘根</summary>
    private static void RemoveRoot(string root)
    {
        try
        {
            List<string> kept = new List<string>();
            foreach (string r in ReadRoots())
            {
                if (!string.Equals(r, root, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(r);
                }
            }

            string file = Io(RootsListPath());
            if (kept.Count == 0)
            {
                if (File.Exists(file)) { File.Delete(file); }
            }
            else
            {
                File.WriteAllLines(file, kept.ToArray(), new UTF8Encoding(false));
            }
        }
        catch { }
    }

    private static List<string> ReadMarked()
    {
        List<string> list = new List<string>();

        try
        {
            string file = Io(MarkedListPath());
            if (!File.Exists(file))
            {
                return list;
            }

            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    list.Add(trimmed);
                }
            }
        }
        catch { }

        return list;
    }

    private static void WriteMarked(List<string> list)
    {
        try
        {
            string dir = DataDir();
            string file = Io(MarkedListPath());

            if (list == null || list.Count == 0)
            {
                if (File.Exists(file)) { File.Delete(file); }
                return;
            }

            File.WriteAllLines(file, list.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    private static void AddMarked(string folder)
    {
        // 读改写要串行：守护进程扫共享盘时 AddMarked、用户同时在 UI 里标另一个文件夹，
        // 两边各读一份旧清单再写回去，后写的那次会把前一条记录吞掉。
        using (PathLock(MarkedListPath()))
        {
            try
            {
                List<string> list = ReadMarked();

                for (int i = 0; i < list.Count; i++)
                {
                    if (SamePath(list[i], folder))
                    {
                        return;
                    }
                }

                list.Add(folder);
                WriteMarked(list);
            }
            catch { }
        }
    }

    private static void RemoveMarked(string folder)
    {
        using (PathLock(MarkedListPath()))
        {
            try
            {
                List<string> list = ReadMarked();
                List<string> keep = new List<string>();

                for (int i = 0; i < list.Count; i++)
                {
                    if (!SamePath(list[i], folder))
                    {
                        keep.Add(list[i]);
                    }
                }

                if (keep.Count != list.Count)
                {
                    WriteMarked(keep);
                }
            }
            catch { }
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            string x = Path.GetFullPath(Plain(a)).TrimEnd('\\');
            string y = Path.GetFullPath(Plain(b)).TrimEnd('\\');
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

}
