using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

// 颜色解析与 ico 写入层：颜色 token 解析、多尺寸 PNG ico 合成、路径哈希。
// 从 FolderMarker.cs 拆出来 —— 与 Color.cs（染色算法）、Icon.cs（图标装配）配套。

internal static partial class Program
{

    /// <summary>
    /// 颜色 token 解析：7 个预设走内置表；自定义 token 形如 c&lt;RRGGBB&gt;
    /// （颜色对话框 / 命令行给出），取色相与饱和度，明度沿用原像素 ——
    /// 与预设走完全相同的渲染路径。自定义时 ci 返回 -1。
    /// </summary>
    private static bool ResolveColor(string token, out int ci, out double hue, out double sat)
    {
        ci = Array.IndexOf(ValidColors, token);
        if (ci >= 0)
        {
            hue = TargetHue[ci];
            sat = TargetSat[ci];
            return true;
        }

        hue = 0; sat = 0;
        string t = token == null ? "" : token.ToLowerInvariant();
        if (!IsCustomToken(t)) { return false; }

        int rgb;
        if (!int.TryParse(t.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out rgb))
        {
            return false;
        }
        // 和取色器预览共用同一套映射（近灰 → 纯灰，其余取 HSV 的色相/饱和度）
        RgbToEffectiveHueSat(rgb, out hue, out sat);
        ci = -1;
        return true;
    }

    /// <summary>自定义颜色 token：c + 6 位小写十六进制 RGB</summary>
    private static bool IsCustomToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 7 || token[0] != 'c') { return false; }
        for (int i = 1; i < 7; i++)
        {
            char c = token[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex) { return false; }
        }
        return true;
    }
    private static byte ToByte(double v)
    {
        int n = (int)(v * 255.0 + 0.5);
        if (n < 0) { n = 0; }
        if (n > 255) { n = 255; }
        return (byte)n;
    }

    /// <summary>写多尺寸 ico（每帧都是 PNG 压缩，和 icons\*.ico 的做法一致）</summary>
    private static bool WriteIco(string path, List<Bitmap> frames)
    {
        if (frames == null || frames.Count == 0)
        {
            return false;
        }

        List<byte[]> pngs = new List<byte[]>();

        foreach (Bitmap f in frames)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                f.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }

        try
        {
            using (FileStream fs = new FileStream(Io(path), FileMode.Create, FileAccess.Write))
            {
                BinaryWriter bw = new BinaryWriter(fs);

                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)pngs.Count);

                int offset = 6 + 16 * pngs.Count;

                for (int i = 0; i < pngs.Count; i++)
                {
                    int s = frames[i].Width;
                    byte dim = s >= 256 ? (byte)0 : (byte)s;
                    bw.Write(dim);
                    bw.Write(dim);
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write(pngs[i].Length);
                    bw.Write(offset);
                    offset += pngs[i].Length;
                }

                for (int i = 0; i < pngs.Count; i++)
                {
                    bw.Write(pngs[i]);
                }

                bw.Flush();
            }

            return true;
        }
        catch (Exception ex)
        {
            TryLog(ex);
            return false;
        }
    }

    /// <summary>folder 路径的稳定短哈希，用来给合成图标命名（同一路径每次一样）</summary>
    private static string PathHash(string path)
    {
        uint h = 2166136261;
        string key = (path ?? "").ToLowerInvariant();

        for (int i = 0; i < key.Length; i++)
        {
            h = (h ^ key[i]) * 16777619;
        }

        return h.ToString("x8");
    }

}
