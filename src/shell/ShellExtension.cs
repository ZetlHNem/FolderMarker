// Windows 11 新版右键菜单扩展：实现 IExplorerCommand，让「文件夹标记」直接出现在
// 主菜单（而不是「显示更多选项」里）。编译成 AnyCPU 的 .NET Framework DLL，
// 由 Explorer 通过 mscoree 的 COM shim 加载。
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

[assembly: ComVisible(true)]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace FolderMarker.Shell
{
    // ---------------- shell 接口 ----------------

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int GetCount(out uint pdwNumItems);
        [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem ppsi);
        [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetIcon(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
        [PreserveSig] int GetToolTip(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out int pCmdState);
        [PreserveSig] int Invoke(IShellItemArray psiItemArray, IntPtr pbc);
        [PreserveSig] int GetFlags(out int pFlags);
        [PreserveSig] int EnumSubCommands(out IEnumExplorerCommand ppEnum);
    }

    [ComImport, Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumExplorerCommand
    {
        [PreserveSig] int Next(uint celt, IntPtr rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumExplorerCommand ppenum);
    }

    // ---------------- 实现 ----------------

    internal static class Const
    {
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const uint SIGDN_FILESYSPATH = 0x80058000;
        public const int ECS_ENABLED = 0;
        public const int ECF_HASSUBCOMMANDS = 1;

        public static string Dir
        {
            get
            {
                try
                {
                    string loc = typeof(MarkerCommand).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc)) { return Path.GetDirectoryName(loc); }
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static string Icon(string name)
        {
            return Path.Combine(Path.Combine(Dir, "icons"), name);
        }

        public static void Run(string folder, string action, string color)
        {
            try
            {
                string exe = Path.Combine(Dir, "FolderMarker.exe");
                if (!File.Exists(exe)) { return; }

                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.Arguments = action + " " + color + " \"" + folder + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Dir;
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>打开管理台窗口（不需要选中的文件夹路径）</summary>
        public static void RunUI()
        {
            try
            {
                string exe = Path.Combine(Dir, "FolderMarker.exe");
                if (!File.Exists(exe)) { return; }

                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.Arguments = "ui";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Dir;
                Process.Start(psi);
            }
            catch { }
        }

        public static string[] PathsOf(IShellItemArray array)
        {
            if (array == null) { return new string[0]; }

            uint count;
            if (array.GetCount(out count) != S_OK || count == 0) { return new string[0]; }

            string[] result = new string[count];
            for (uint i = 0; i < count; i++)
            {
                IShellItem item;
                if (array.GetItemAt(i, out item) != S_OK || item == null) { continue; }

                IntPtr buffer;
                if (item.GetDisplayName(SIGDN_FILESYSPATH, out buffer) == S_OK)
                {
                    result[i] = Marshal.PtrToStringUni(buffer);
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
            return result;
        }
    }

    internal sealed class ColorSpec
    {
        public readonly string Color;
        public readonly string Title;
        public readonly string IconName;

        public ColorSpec(string color, string title, string iconName)
        {
            Color = color;
            Title = title;
            IconName = iconName;
        }
    }

    internal static class Palette
    {
        public static readonly ColorSpec[] All = new ColorSpec[]
        {
            new ColorSpec("red",    "红色", "red.ico"),
            new ColorSpec("orange", "橙色", "orange.ico"),
            new ColorSpec("yellow", "黄色", "yellow.ico"),
            new ColorSpec("green",  "绿色", "green.ico"),
            new ColorSpec("blue",   "蓝色", "blue.ico"),
            new ColorSpec("purple", "紫色", "purple.ico"),
            new ColorSpec("gray",   "灰色", "gray.ico"),
            new ColorSpec("none",   "清除标记", "menu.ico"),
            new ColorSpec("manage", "管理…", "manage.ico"),
        };
    }

    /// <summary>父命令：只负责挂子菜单</summary>
    [ComVisible(true)]
    [Guid("6F1D3A24-8C7E-4B1A-9E25-3D5C8A0F7B41")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class MarkerCommand : IExplorerCommand
    {
        public int GetTitle(IShellItemArray items, out string title)
        {
            title = "文件夹标记";
            return Const.S_OK;
        }

        public int GetIcon(IShellItemArray items, out string icon)
        {
            icon = Const.Icon("menu.ico");
            return Const.S_OK;
        }

        public int GetToolTip(IShellItemArray items, out string tip)
        {
            tip = "给文件夹加一点颜色，图标立即变色";
            return Const.S_OK;
        }

        public int GetCanonicalName(out Guid guid)
        {
            guid = new Guid("6F1D3A24-8C7E-4B1A-9E25-3D5C8A0F7B41");
            return Const.S_OK;
        }

        public int GetState(IShellItemArray items, bool slow, out int state)
        {
            state = Const.ECS_ENABLED;
            return Const.S_OK;
        }

        public int Invoke(IShellItemArray items, IntPtr pbc)
        {
            // 父项本身不带动作（有子菜单时 Explorer 不会调它）
            return Const.S_OK;
        }

        public int GetFlags(out int flags)
        {
            flags = Const.ECF_HASSUBCOMMANDS;
            return Const.S_OK;
        }

        public int EnumSubCommands(out IEnumExplorerCommand commands)
        {
            commands = new ColorEnumerator();
            return Const.S_OK;
        }
    }

    /// <summary>子命令：一次具体动作</summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ColorCommand : IExplorerCommand
    {
        private readonly string color;
        private readonly string title;
        private readonly string iconName;

        public ColorCommand(string color, string title, string iconName)
        {
            this.color = color;
            this.title = title;
            this.iconName = iconName;
        }

        public int GetTitle(IShellItemArray items, out string name)
        {
            name = title;
            return Const.S_OK;
        }

        public int GetIcon(IShellItemArray items, out string icon)
        {
            icon = Const.Icon(iconName);
            return Const.S_OK;
        }

        public int GetToolTip(IShellItemArray items, out string tip)
        {
            tip = null;
            return Const.S_OK;
        }

        public int GetCanonicalName(out Guid guid)
        {
            guid = Guid.Empty;
            return Const.S_OK;
        }

        public int GetState(IShellItemArray items, bool slow, out int state)
        {
            state = Const.ECS_ENABLED;
            return Const.S_OK;
        }

        public int Invoke(IShellItemArray items, IntPtr pbc)
        {
            // 管理台是全局窗口，跟选中了哪个文件夹无关
            if (color == "manage")
            {
                Const.RunUI();
                return Const.S_OK;
            }

            string[] paths = Const.PathsOf(items);
            for (int i = 0; i < paths.Length; i++)
            {
                string folder = paths[i];
                if (string.IsNullOrEmpty(folder)) { continue; }

                if (color == "none") { Const.Run(folder, "clear", "none"); }
                else { Const.Run(folder, "set", color); }
            }
            return Const.S_OK;
        }

        public int GetFlags(out int flags)
        {
            flags = 0;
            return Const.S_OK;
        }

        public int EnumSubCommands(out IEnumExplorerCommand commands)
        {
            commands = null;
            return Const.E_NOTIMPL;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ColorEnumerator : IEnumExplorerCommand
    {
        private readonly IExplorerCommand[] items;
        private int index;

        public ColorEnumerator()
        {
            ColorSpec[] all = Palette.All;
            items = new IExplorerCommand[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                items[i] = new ColorCommand(all[i].Color, all[i].Title, all[i].IconName);
            }
        }

        public int Next(uint celt, IntPtr rgelt, out uint fetched)
        {
            uint n = 0;
            while (n < celt && index < items.Length)
            {
                IntPtr p = Marshal.GetComInterfaceForObject(items[index], typeof(IExplorerCommand));
                Marshal.WriteIntPtr(rgelt, (int)(n * (uint)IntPtr.Size), p);
                index++;
                n++;
            }
            fetched = n;
            return n == celt ? Const.S_OK : Const.S_FALSE;
        }

        public int Skip(uint celt)
        {
            index += (int)celt;
            if (index > items.Length) { index = items.Length; }
            return Const.S_OK;
        }

        public int Reset()
        {
            index = 0;
            return Const.S_OK;
        }

        public int Clone(out IEnumExplorerCommand copy)
        {
            copy = new ColorEnumerator();
            return Const.S_OK;
        }
    }
}
