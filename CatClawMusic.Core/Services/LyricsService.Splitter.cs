using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>歌词服务 —— partial 分域文件。</summary>
public partial class LyricsService
{
    private static (string original, string? translation) SplitBilingual(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, null);

        // 只按「显式分隔符」拆分：原文 / 译文、原文｜译文、原文 - 译文 等。
        // 不做任何文字系统猜测（CJK/日文假名/拉丁 的启发式判断）——
        // 参考 lx-music 等主流播放器：译文走独立歌词流，行内不做猜测拆分。
        // 猜测式拆分会误伤无分隔符的行，如 "作词 aimer"（作词/作曲署名行）、
        // "Lyrics by aimer"（英文歌词署名）等，被拆成 原文+译文 两行。
        var (sepStart, sepEnd) = FindExplicitSeparatorSplit(text);
        if (sepStart >= 0)
        {
            var orig = text.Substring(0, sepStart).TrimEnd();
            var trans = text.Substring(sepEnd).TrimStart();
            if (!string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(trans))
                return (orig, trans);
        }

        return (text, null);
    }

    /// <summary>
    /// 在显式分隔符（斜杠/竖线/反斜杠/破折号等）处定位双语分割点。
    /// <para>要求：分隔符两侧均为文字段（含字母或 CJK），且两侧主导文字系统不同
    /// （如 日文/中文、中文/英文），避免误伤 "1/2"、"和/或"、"a-ha" 等。
    /// 支持带空白包裹的分隔符（"原文 / 译文"）与紧贴形式（"原文/译文"）。</para>
    /// </summary>
    /// <param name="text">待拆分文本</param>
    /// <returns>(分隔符块起点, 分隔符块末尾)；未找到返回 (-1, -1)。译文从末尾索引开始取。</returns>
    private static (int start, int end) FindExplicitSeparatorSplit(string text)
    {
        const string separators = "/／|｜\\﹨—–-﹣－";
        for (int i = 0; i < text.Length; i++)
        {
            if (separators.IndexOf(text[i]) < 0) continue;

            // 吞掉连续分隔符（如 "//"、"｜｜"）
            int end = i + 1;
            while (end < text.Length && separators.IndexOf(text[end]) >= 0)
                end++;

            var left = text.Substring(0, i).TrimEnd();
            var right = text.Substring(end).TrimStart();
            if (left.Length == 0 || right.Length == 0) continue;
            if (!ContainsLetterOrCjk(left) || !ContainsLetterOrCjk(right)) continue;
            if (!IsDifferentScript(left, right)) continue;

            return (i, end);
        }
        return (-1, -1);
    }

    /// <summary>判断文本是否包含至少一个字母或中日韩字符（排除纯数字/纯标点段）</summary>
    private static bool ContainsLetterOrCjk(string s)
    {
        foreach (var ch in s)
        {
            if (IsCjk(ch) || IsJapanese(ch) || IsHangul(ch)) return true;
            if (char.IsLetter(ch)) return true;
        }
        return false;
    }

    /// <summary>判断字符是否为 CJK 中日韩统一表意文字（含兼容区与全角符号）</summary>
    private static bool IsCjk(char ch)
    {
        return (ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3400 && ch <= 0x4DBF) ||
               (ch >= 0x2E80 && ch <= 0x2EFF) || (ch >= 0x3000 && ch <= 0x303F) ||
               (ch >= 0xFF00 && ch <= 0xFFEF);
    }

    /// <summary>判断字符是否为日文假名（平假名/片假名/半角片假名）</summary>
    private static bool IsJapanese(char ch)
    {
        return (ch >= 0x3040 && ch <= 0x309F) || (ch >= 0x30A0 && ch <= 0x30FF) ||
               (ch >= 0x31F0 && ch <= 0x31FF) || (ch >= 0xFF65 && ch <= 0xFF9F);
    }

    /// <summary>
    /// 解析行内逐字时间戳（格式：&lt;mm:ss.xx&gt;word &lt;mm:ss.xx&gt;word ...）
    /// </summary>
}
