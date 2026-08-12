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

        // 策略0：显式分隔符路径 — 原文 / 译文、原文｜译文、原文 - 译文 等。
        // 某些歌词源用 空格/斜杠/竖线/破折号 等分隔原文与翻译，两侧文字系统不同。
        var (sepStart, sepEnd) = FindExplicitSeparatorSplit(text);
        if (sepStart >= 0)
        {
            var orig = text.Substring(0, sepStart).TrimEnd();
            var trans = text.Substring(sepEnd).TrimStart();
            if (!string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(trans))
                return (orig, trans);
        }

        // 策略1：日文+中文分割 — 日文含假名，中文纯汉字
        // 找到"含假名的区域"结束后、"纯汉字区域"开始前的空白分隔点
        var jpCnSplit = FindJapaneseChineseSplit(text);
        if (jpCnSplit > 0)
        {
            var orig = text.Substring(0, jpCnSplit).TrimEnd();
            var trans = text.Substring(jpCnSplit).TrimStart();
            if (!string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(trans))
                return (orig, trans);
        }

        // 策略2：通用 CJK + 非 CJK 分割（韩文+中文等）
        bool hasCjk = false;
        bool hasNonCjk = false;
        foreach (var ch in text)
        {
            if (IsCjk(ch) || IsJapanese(ch) || IsHangul(ch)) hasCjk = true;
            else if (char.IsLetter(ch)) hasNonCjk = true;
        }
        if (!hasCjk || !hasNonCjk) return (text, null);

        int splitPos = -1;
        bool inCjkRun = false;
        int cjkRunStart = -1;
        bool seenJapanese = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (IsJapanese(ch))
            {
                seenJapanese = true;
                inCjkRun = false;
                cjkRunStart = -1;
            }
            else if (IsCjk(ch))
            {
                if (!inCjkRun)
                {
                    inCjkRun = true;
                    cjkRunStart = i;
                }
            }
            else
            {
                if (inCjkRun && seenJapanese && cjkRunStart > 0)
                {
                    if (char.IsWhiteSpace(text[cjkRunStart - 1]))
                        splitPos = cjkRunStart;
                }
                inCjkRun = false;
                cjkRunStart = -1;
            }
        }

        if (splitPos < 0)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (IsCjk(text[i]))
                {
                    if (i > 0 && char.IsWhiteSpace(text[i - 1]))
                    {
                        bool hasNonCjkBefore = false;
                        for (int j = 0; j < i - 1; j++)
                        {
                            if (char.IsLetter(text[j]) && !IsCjk(text[j]))
                            {
                                hasNonCjkBefore = true;
                                break;
                            }
                        }
                        if (hasNonCjkBefore)
                        {
                            splitPos = i;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        // 策略2b：对称方向 — CJK 原文 + 空白 + 非CJK 译文（如 "你好 Hello"、"爱你 I love you"）。
        // 原策略2 只支持 非CJK→CJK，中文在前英文在后时拆不开。
        if (splitPos < 0)
        {
            for (int i = 1; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsLetter(ch) && !IsCjk(ch) && !IsJapanese(ch) && !IsHangul(ch))
                {
                    if (char.IsWhiteSpace(text[i - 1]))
                    {
                        bool hasCjkBefore = false;
                        for (int j = 0; j < i; j++)
                        {
                            if (IsCjk(text[j]) || IsJapanese(text[j]) || IsHangul(text[j]))
                            {
                                hasCjkBefore = true;
                                break;
                            }
                        }
                        if (hasCjkBefore)
                        {
                            splitPos = i;
                            break;
                        }
                    }
                }
            }
        }

        if (splitPos > 0)
        {
            var orig = text.Substring(0, splitPos).TrimEnd();
            var trans = text.Substring(splitPos).TrimStart();
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

    /// <summary>
    /// 查找日文+中文的分隔点
    /// <para>日文特征：含假名（ひらがな/カタカナ）；中文特征：纯汉字（无假名）</para>
    /// <para>策略：找到最后一个假名位置，在其后找空白分隔点，确保分隔后无假名</para>
    /// </summary>
    /// <returns>分割位置（空白字符位置），0 表示未找到</returns>
    private static int FindJapaneseChineseSplit(string text)
    {
        // 找到最后一个假名字符的位置
        int lastKanaPos = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsJapanese(text[i]))
                lastKanaPos = i;
        }

        // 没有假名，无法判断为日文
        if (lastKanaPos < 0) return 0;

        // 检查最后一个假名之后是否还有汉字（中文翻译）
        bool hasCjkAfterLastKana = false;
        for (int i = lastKanaPos + 1; i < text.Length; i++)
        {
            if (IsCjk(text[i])) { hasCjkAfterLastKana = true; break; }
        }
        if (!hasCjkAfterLastKana) return 0;

        // 从最后一个假名之后，找空白分隔点
        // 空白后面必须只有纯汉字（无假名），才认为是中文翻译
        for (int i = lastKanaPos + 1; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i])) continue;

            // 跳过连续空白
            int nextStart = i + 1;
            while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart]))
                nextStart++;

            if (nextStart >= text.Length) break;

            // 空白后第一个字符必须是 CJK
            if (!IsCjk(text[nextStart])) continue;

            // 确认空白后面到文本末尾没有假名（纯中文翻译）
            bool hasKanaAfter = false;
            for (int k = nextStart; k < text.Length; k++)
            {
                if (IsJapanese(text[k])) { hasKanaAfter = true; break; }
            }
            if (!hasKanaAfter) return i;
        }

        // 如果没有空白分隔，但最后一个假名后紧跟汉字（无空格情况）
        // 尝试在假名后直接分割（不太常见但作为兜底）
        return 0;
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
