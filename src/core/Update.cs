using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

// 在线更新：从 GitHub Releases 检查新版本、下载安装包、静默安装。
// 全部网络操作都包在 try 里，失败绝不抛到主程序（更新是增强功能，不能拖垮标记）。
internal static partial class Program
{
    private const string UpdateRepo = "ZetlHNem/FolderMarker";
    private const string LatestApi = "https://api.github.com/repos/" + UpdateRepo + "/releases/latest";

    /// <summary>GitHub 上最新版本的检查结果</summary>
    internal sealed class UpdateInfo
    {
        public string Version;       // "v2.3.9"
        public string DownloadUrl;   // 安装包直链
        public string Error;         // 失败原因（成功为 null）
        public bool Ok { get { return Error == null && Version != null; } }
    }

    /// <summary>
    /// 调 GitHub releases/latest，拿到最新 tag 和第一个 .exe 附件的下载链接。
    /// 用 WebClient + User-Agent（GitHub 对无 UA 的请求返回 403）。
    /// </summary>
    private static UpdateInfo CheckLatestVersion()
    {
        UpdateInfo info = new UpdateInfo();
        try
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "FolderMarker-Updater");
                wc.Headers.Add("Accept", "application/vnd.github+json");
                string json = wc.DownloadString(LatestApi);
                // GitHub 限流/被拒时返回的是 HTML 错误页而不是 JSON，提前识别给友好提示
                if (string.IsNullOrEmpty(json) || json.IndexOf("\"tag_name\"", StringComparison.Ordinal) < 0)
                {
                    info.Error = "GitHub 暂时拒绝了检查请求（请稍后再试）";
                    return info;
                }
                info.Version = ExtractJsonString(json, "tag_name");
                info.DownloadUrl = ExtractExeAssetUrl(json);
                if (string.IsNullOrEmpty(info.Version) || string.IsNullOrEmpty(info.DownloadUrl))
                {
                    info.Error = "返回内容里没找到版本号或安装包";
                }
            }
        }
        catch (Exception ex)
        {
            info.Error = ShortNetError(ex);
        }
        return info;
    }

    /// <summary>apk 版本号是否有更新：v2.3.9 比 2.3.8 新则为 true</summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        int[] a = ParseVersion(latest);
        int[] b = ParseVersion(current);
        for (int i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) { return true; }
            if (a[i] < b[i]) { return false; }
        }
        return false;
    }

    private static int[] ParseVersion(string s)
    {
        int[] v = new int[] { 0, 0, 0 };
        if (string.IsNullOrEmpty(s)) { return v; }
        Match m = Regex.Match(s, @"(\d+)\.(\d+)\.(\d+)");
        if (!m.Success) { return v; }
        int.TryParse(m.Groups[1].Value, out v[0]);
        int.TryParse(m.Groups[2].Value, out v[1]);
        int.TryParse(m.Groups[3].Value, out v[2]);
        return v;
    }

    /// <summary>
    /// 下载安装包到临时目录并静默安装。返回 null 表示成功，否则是错误说明。
    /// 下载完先校验是 PE 文件（MZ 头）再执行，避免把错误页当安装包跑。
    /// </summary>
    private static string DownloadAndInstall(UpdateInfo info)
    {
        string temp = null;
        try
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            temp = Path.Combine(Path.GetTempPath(),
                "FolderMarker-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");

            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "FolderMarker-Updater");
                wc.DownloadFile(info.DownloadUrl, temp);
            }

            // 基本校验：必须是可执行文件且带 MZ 头，大小合理（>1MB）
            FileInfo fi = new FileInfo(temp);
            if (!fi.Exists || fi.Length < 1024 * 1024) { return "下载文件过小，可能下载失败"; }
            using (FileStream fs = new FileStream(temp, FileMode.Open, FileAccess.Read))
            {
                byte[] head = new byte[2];
                if (fs.Read(head, 0, 2) < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z')
                { return "下载的不是有效的安装程序"; }
            }

            ProcessStartInfo psi = new ProcessStartInfo(temp);
            psi.Arguments = "/VERYSILENT /NORESTART /MERGETASKS=!desktopicon";
            psi.UseShellExecute = true;
            Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return ShortNetError(ex);
        }
        finally
        {
            // 安装包是自己启动的，Windows 会在进程退出后释放文件；
            // 这里不立即删（可能还在安装中），交给下次启动清理旧临时包。
            CleanOldUpdateTemp();
        }
    }

    /// <summary>清理上次残留的更新包（启动/更新时调用）</summary>
    private static void CleanOldUpdateTemp()
    {
        try
        {
            foreach (string f in Directory.GetFiles(Path.GetTempPath(), "FolderMarker-setup-*.exe"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    // ---- JSON 极简解析（不引 Newtonsoft，够用即可）----

    private static string ExtractJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) { return null; }
        string pat = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
        Match m = Regex.Match(json, pat);
        if (!m.Success) { return null; }
        return m.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"");
    }

    /// <summary>从 releases/latest 的 assets 数组里找 .exe 安装包的 browser_download_url</summary>
    private static string ExtractExeAssetUrl(string json)
    {
        if (string.IsNullOrEmpty(json)) { return null; }
        // 不能用 \{[^{}]*\} 匹配 asset 对象 —— GitHub 真实返回里每个 asset 内嵌了
        // uploader:{...}，含嵌套大括号，那个正则一个都匹配不到（曾导致「检查失败：
        // 返回内容里没找到版本号或安装包」）。改为直接抓所有 browser_download_url，
        // 挑第一个 .exe 结尾的；zip/tar.gz 等源码包的 url 不会以 .exe 结尾，自然被跳过。
        string pat = "\"browser_download_url\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
        foreach (Match m in Regex.Matches(json, pat))
        {
            string url = m.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"");
            if (url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { return url; }
        }
        return null;
    }

    private static string ShortNetError(Exception ex)
    {
        if (ex is WebException)
        {
            WebException we = (WebException)ex;
            if (we.Status == WebExceptionStatus.Timeout) { return "连接超时"; }
            if (we.Status == WebExceptionStatus.ProtocolError && we.Response != null)
            {
                HttpWebResponse resp = we.Response as HttpWebResponse;
                if (resp != null && (int)resp.StatusCode == 403)
                { return "GitHub 暂时拒绝了检查请求（请稍后再试）"; }
                if (resp != null && (int)resp.StatusCode == 404)
                { return "找不到更新信息"; }
            }
            return "网络错误：" + we.Status;
        }
        return ex.Message;
    }

    /// <summary>命令行：update 检查版本，update install 直接下载安装</summary>
    private static int CmdUpdate(string[] args)
    {
        UpdateInfo info = CheckLatestVersion();
        if (!info.Ok)
        {
            Diag("检查更新失败: " + info.Error);
            return 1;
        }
        bool newer = IsNewerVersion(info.Version, AppVersion);
        Diag("当前版本: " + AppVersion);
        Diag("最新版本: " + info.Version + (newer ? "（有更新）" : "（已是最新）"));
        Diag("下载: " + info.DownloadUrl);
        if (newer && args.Length > 1 && args[1].Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            string err = DownloadAndInstall(info);
            if (err != null) { Diag("安装失败: " + err); return 2; }
            Diag("已开始安装 " + info.Version);
        }
        return 0;
    }

    /// <summary>给命令行用的简单输出（写诊断文件 + 控制台）</summary>
    private static void Diag(string line)
    {
        try { Console.WriteLine(line); } catch { }
        try
        {
            string p = Path.Combine(DataDirPath(), "update.log");
            File.AppendAllText(Io(p), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }
}
