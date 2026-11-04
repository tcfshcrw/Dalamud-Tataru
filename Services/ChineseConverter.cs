using System;
using System.Runtime.InteropServices;

namespace DalamudTataru.Services;

/// <summary>
/// 提供基於 Windows 原生 API (LCMapStringEx) 的超高效簡體轉繁體與繁體轉簡體功能，
/// 零第三方相依性，專門解決本地 LLM (如 LM Studio / Qwen / Sakura) 回傳簡體字的問題。
/// </summary>
public static class ChineseConverter
{
    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;
    private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;

    [DllImport("kernel32.dll", EntryPoint = "LCMapStringEx", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string lpLocaleName,
        uint dwMapFlags,
        string lpSrcStr,
        int cchSrc,
        [Out] char[]? lpDestStr,
        int cchDest,
        IntPtr lpVersionInformation,
        IntPtr lpReserved,
        IntPtr sortHandle);

    /// <summary>
    /// 將字串中的簡體中文字元轉換為繁體中文
    /// </summary>
    public static string ToTraditional(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        try
        {
            int len = LCMapStringEx("zh-CN", LCMAP_TRADITIONAL_CHINESE, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (len <= 0) return text;

            char[] buffer = new char[len];
            int result = LCMapStringEx("zh-CN", LCMAP_TRADITIONAL_CHINESE, text, text.Length, buffer, len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return result > 0 ? new string(buffer, 0, result) : text;
        }
        catch
        {
            return text;
        }
    }

    /// <summary>
    /// 將字串中的繁體中文字元轉換為簡體中文
    /// </summary>
    public static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        try
        {
            int len = LCMapStringEx("zh-TW", LCMAP_SIMPLIFIED_CHINESE, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (len <= 0) return text;

            char[] buffer = new char[len];
            int result = LCMapStringEx("zh-TW", LCMAP_SIMPLIFIED_CHINESE, text, text.Length, buffer, len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return result > 0 ? new string(buffer, 0, result) : text;
        }
        catch
        {
            return text;
        }
    }
}
