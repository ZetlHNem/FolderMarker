using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 规则引擎：rules.txt 的读取/解析/套用（按路径、名称、时间等条件批量改色）。
// 从 FolderMarker.cs 拆出来 —— 规则语法和匹配逻辑独立于其他部分。

internal static partial class Program
{
    /// <summary>同一个共享盘一轮只探一次</summary>
    private static bool PathReachable(string folder, Dictionary<string, bool> cache)
    {
        if (!IsUncPath(folder))
        {
            return true;
        }

        string share = ShareRoot(folder);
        if (string.IsNullOrEmpty(share))
        {
            return true;
        }

        bool ok;
        if (cache.TryGetValue(share, out ok))
        {
            return ok;
        }

        ok = ShareReachable(share);
        cache[share] = ok;

        if (!ok)
        {
            DebugLog("skip offline share: " + share + "（本轮跳过它下面所有文件夹）");
        }

        return ok;
    }

    // ================= 自动规则 =================
    //
    // rules.txt 每行一条，自上而下第一条命中生效：
    //   contains:归档   => gray
    //   contains:最终版 => green star
    //   older:30        => orange alert
    // 左侧匹配：contains:<子串>（路径包含，忽略大小写）/ older:<天数>（文件夹 30 天没改过）
    // 右侧动作：<颜色> [符号]，颜色可以是预设名或 c<RRGGBB>，符号可选
    //
    // 只作用于「已经在标记清单里」的文件夹，不主动给整个盘上色 —— 误伤成本太高。

    internal sealed class AutoRule
    {
        public string Kind;      // contains / older
        public string Arg;
        public string Color;
        public string Raw;
    }

    internal static string RulesPath()
    {
        return Path.Combine(DataDir(), "rules.txt");
    }

    internal static List<AutoRule> ReadRules()
    {
        List<AutoRule> list = new List<AutoRule>();
        try
        {
            if (!File.Exists(Io(RulesPath()))) { return list; }

            foreach (string raw in File.ReadAllLines(Io(RulesPath()), Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) { continue; }

                int arrow = line.IndexOf("=>", StringComparison.Ordinal);
                if (arrow <= 0) { continue; }

                string left = line.Substring(0, arrow).Trim();
                string right = line.Substring(arrow + 2).Trim();
                if (right.Length == 0) { continue; }

                AutoRule r = new AutoRule();
                r.Raw = line;

                int colon = left.IndexOf(':');
                if (colon <= 0) { continue; }
                r.Kind = left.Substring(0, colon).Trim().ToLowerInvariant();
                r.Arg = left.Substring(colon + 1).Trim();

                string[] parts = right.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                r.Color = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

                int ci;
                double hue, sat;
                if (!ResolveColor(r.Color, out ci, out hue, out sat)) { continue; }

                list.Add(r);
            }
        }
        catch { }
        return list;
    }

    /// <summary>按规则算目标颜色；没有命中返回 null</summary>
    private static AutoRule MatchRule(string folder, List<AutoRule> rules)
    {
        if (rules == null || rules.Count == 0) { return null; }

        foreach (AutoRule r in rules)
        {
            if (r.Kind == "contains")
            {
                if (r.Arg.Length > 0 && folder.IndexOf(r.Arg, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return r;
                }
            }
            else if (r.Kind == "older")
            {
                int days;
                if (!int.TryParse(r.Arg, out days) || days <= 0) { continue; }
                try
                {
                    DirectoryInfo di = new DirectoryInfo(Plain(folder));
                    if (di.Exists && (DateTime.UtcNow - di.LastWriteTimeUtc).TotalDays >= days)
                    {
                        return r;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// 对清单里的一批文件夹应用规则。返回实际改动的数量。
    /// 只改不还原：规则删掉后，已改过的颜色保持原样。
    /// </summary>
    internal static int ApplyRules(List<string> folders, out List<string> changed)
    {
        changed = new List<string>();
        List<AutoRule> rules = ReadRules();
        if (rules.Count == 0) { return 0; }

        foreach (string f in folders)
        {
            try
            {
                if (!Directory.Exists(Io(f))) { continue; }

                AutoRule hit = MatchRule(f, rules);
                if (hit == null) { continue; }

                string ini = Path.Combine(Io(f), "desktop.ini");
                if (!File.Exists(ini)) { continue; }

                string curColor = CurrentFolderColor(f);

                bool sameColor = string.Equals(curColor, hit.Color, StringComparison.OrdinalIgnoreCase);
                if (sameColor) { continue; }

                Main(new string[] { "set", hit.Color, f, "quiet" });
                changed.Add(f);
            }
            catch { }
        }
        return changed.Count;
    }
}
