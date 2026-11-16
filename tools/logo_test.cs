using System;
using System.Runtime.InteropServices;

// 实验：验证 FCSM_LOGO (pszLogo) 对「文件夹缩略图」的作用
internal static class LogoTest
{
    private const uint FCSM_ICONFILE = 0x00000010;
    private const uint FCSM_LOGO = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFOLDERCUSTOMSETTINGS
    {
        public uint dwSize;
        public uint dwMask;
        public IntPtr pvid;
        public string pszWebViewTemplate;
        public uint cchWebViewTemplate;
        public string pszWebViewTemplateVersion;
        public string pszInfoTip;
        public uint cchInfoTip;
        public IntPtr pclsid;
        public uint dwFlags;
        public string pszIconFile;
        public uint cchIconFile;
        public int iIconIndex;
        public string pszLogo;
        public uint cchLogo;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS pfcs, string pszPath, uint dwReadWrite);

    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: LogoTest <folder> <read|setlogo|clearlogo> [logoPath]"); return 1; }

        string folder = args[0];
        string op = args[1];
        SHFOLDERCUSTOMSETTINGS fcs = new SHFOLDERCUSTOMSETTINGS();
        fcs.dwSize = (uint)Marshal.SizeOf(typeof(SHFOLDERCUSTOMSETTINGS));

        if (op == "read")
        {
            // 读回当前 IconFile 和 Logo
            fcs.dwMask = FCSM_ICONFILE | FCSM_LOGO;
            fcs.pszIconFile = new string('\0', 1024);
            fcs.cchIconFile = 1024;
            fcs.pszLogo = new string('\0', 1024);
            fcs.cchLogo = 1024;
            int hr = SHGetSetFolderCustomSettings(ref fcs, folder, 0x00000001); // FCS_READ
            Console.WriteLine("read hr=0x" + hr.ToString("X8"));
            Console.WriteLine("IconFile=" + (fcs.pszIconFile ?? "").TrimEnd('\0'));
            Console.WriteLine("Logo=" + (fcs.pszLogo ?? "").TrimEnd('\0'));
            return 0;
        }

        if (op == "setlogo")
        {
            string logo = args[2];
            fcs.dwMask = FCSM_LOGO;
            fcs.pszLogo = logo;
            fcs.cchLogo = 0;
            int hr = SHGetSetFolderCustomSettings(ref fcs, folder, 0x00000002); // FCS_FORCEWRITE
            Console.WriteLine("setlogo hr=0x" + hr.ToString("X8"));
            return 0;
        }

        if (op == "clearlogo")
        {
            fcs.dwMask = FCSM_LOGO;
            fcs.pszLogo = "";
            fcs.cchLogo = 0;
            int hr = SHGetSetFolderCustomSettings(ref fcs, folder, 0x00000002);
            Console.WriteLine("clearlogo hr=0x" + hr.ToString("X8"));
            return 0;
        }

        return 0;
    }
}
