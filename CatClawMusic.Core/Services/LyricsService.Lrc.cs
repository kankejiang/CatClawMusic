using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>歌词服务 —— partial 分域文件。</summary>
public partial class LyricsService
{
    public LrcLyrics? ParseLrc(string lrcContent)
    {
        // lx-music 扩展歌词格式：[awlrc:lrc:base64,tlrc:base64,rlrc:base64]
        // 一个标签块内逗号分隔多段，每段 流名:base64；正文为原文 LRC。
        // 译文(tlrc)/罗马音(rlrc)/逐字(awlrc) 是独立歌词流，按时间戳并入主行。
        // 兼容部分源单独发 [tlrc:...] / [rlrc:...] 标签的情况。
        if (lrcContent != null)
        {
            var body = lrcContent;
            string? tlrcRaw = null, rlrcRaw = null, lrcRaw = null;

            var extTag = System.Text.RegularExpressions.Regex.Match(body,
                @"(?:^|\n\s*)\[awlrc:([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (extTag.Success)
            {
                body = body.Remove(extTag.Index, extTag.Length);
                foreach (var seg in extTag.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var colon = seg.IndexOf(':');
                    if (colon <= 0) continue;
                    var name = seg[..colon].Trim().ToLowerInvariant();
                    var data = seg[(colon + 1)..].Trim();
                    if (data.Length == 0) continue;
                    switch (name)
                    {
                        case "tlrc": tlrcRaw = DecodeBase64Utf8(data); break;
                        case "rlrc": rlrcRaw = DecodeBase64Utf8(data); break;
                        case "lrc": lrcRaw = DecodeBase64Utf8(data); break;
                    }
                }
            }
            else
            {
                var tlrcTag = System.Text.RegularExpressions.Regex.Match(body,
                    @"(?:^|\n\s*)\[tlrc:([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tlrcTag.Success) { tlrcRaw = DecodeBase64Utf8(tlrcTag.Groups[1].Value); body = body.Remove(tlrcTag.Index, tlrcTag.Length); }
                var rlrcTag = System.Text.RegularExpressions.Regex.Match(body,
                    @"(?:^|\n\s*)\[rlrc:([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rlrcTag.Success) { rlrcRaw = DecodeBase64Utf8(rlrcTag.Groups[1].Value); body = body.Remove(rlrcTag.Index, rlrcTag.Length); }
            }

            var lyrics = ParseLrcCore(body);
            if (lyrics == null && !string.IsNullOrEmpty(lrcRaw))
                lyrics = ParseLrcCore(lrcRaw);
            if (lyrics != null)
            {
                lyrics.TranslationLines = string.IsNullOrEmpty(tlrcRaw) ? null : ParseLrcCore(tlrcRaw)?.Lines;
                lyrics.RomaLines = string.IsNullOrEmpty(rlrcRaw) ? null : ParseLrcCore(rlrcRaw)?.Lines;
                MergeExtendedLines(lyrics);
            }
            return lyrics;
        }
        return null;
    }

    private static string? DecodeBase64Utf8(string data)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data)).Trim(); }
        catch { return null; }
    }

    /// <summary>把译文流/罗马音流按时间戳并入主行（容差 300ms），并保持到原始流字段。</summary>
    private static void MergeExtendedLines(LrcLyrics lyrics)
    {
        if (lyrics.TranslationLines != null)
        {
            foreach (var t in lyrics.TranslationLines)
            {
                var target = FindLineAt(lyrics, t.Timestamp);
                if (target != null && string.IsNullOrEmpty(target.Translation))
                    target.Translation = t.Text;
            }
        }
        if (lyrics.RomaLines != null)
        {
            foreach (var r in lyrics.RomaLines)
            {
                var target = FindLineAt(lyrics, r.Timestamp);
                if (target != null && string.IsNullOrEmpty(target.Roma))
                    target.Roma = r.Text;
            }
        }
    }

    private static LrcLyricLine? FindLineAt(LrcLyrics lyrics, TimeSpan ts)
    {
        foreach (var l in lyrics.Lines)
        {
            if (Math.Abs((l.Timestamp - ts).TotalMilliseconds) < 300)
                return l;
        }
        return null;
    }

    private LrcLyrics? ParseLrcCore(string lrcContent)
    {
        var lyrics = new LrcLyrics();

        lrcContent = lrcContent.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = lrcContent.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            var tagMatch = TagRegex.Match(line);
            if (tagMatch.Success)
            {
                var tag = tagMatch.Groups[1].Value.ToLower();
                var value = tagMatch.Groups[2].Value.Trim();
                switch (tag)
                {
                    case "ti": lyrics.Metadata.Title = value; break;
                    case "ar": lyrics.Metadata.Artist = value; break;
                    case "al": lyrics.Metadata.Album = value; break;
                    case "by": lyrics.Metadata.Author = value; break;
                    case "re": lyrics.Metadata.Maker = value; break;
                    case "ve": lyrics.Metadata.Version = value; break;
                }
                continue;
            }

            // 解析歌词行
            var timeMatches = TimeRegex.Matches(line);
            if (timeMatches.Count == 0) continue;

            // ── 逐字方括号格式适配：[00:00.000]起[00:00.211]风[00:00.422]了[00:00.633] ...
            // 特征：一行内多个时间戳、最后一个 ] 之后无文本、且时间戳之间夹着非空文本。
            // 早期实现把每个时间戳拆成独立行，且文本提取（取最后一个 ] 之后）恒为空，
            // 导致整首歌被解析成 N 个空文本行（如《起风了》903 行全空）→ 歌词区域空白。
            // 正确处理：整行合并为一行，字词时间戳写入 WordTimestamps（逐字卡拉OK填充）。
            var lastBracketIndex = line.LastIndexOf(']');
            var trailingText = lastBracketIndex >= 0 ? line.Substring(lastBracketIndex + 1).Trim() : "";
            if (timeMatches.Count >= 2 && trailingText.Length == 0)
            {
                bool hasInterstitial = false;
                for (int m = 0; m < timeMatches.Count - 1; m++)
                {
                    var segStart = timeMatches[m].Index + timeMatches[m].Length;
                    var segEnd = timeMatches[m + 1].Index;
                    if (segEnd > segStart && !string.IsNullOrWhiteSpace(line.Substring(segStart, segEnd - segStart)))
                    {
                        hasInterstitial = true;
                        break;
                    }
                }

                if (hasInterstitial)
                {
                    var wordTimestamps = new List<WordTimestamp>();
                    var sb = new StringBuilder();
                    for (int m = 0; m < timeMatches.Count; m++)
                    {
                        var mt = timeMatches[m];
                        var minutes = int.Parse(mt.Groups[1].Value);
                        var seconds = int.Parse(mt.Groups[2].Value);
                        var millis = mt.Groups[3].Success
                            ? int.Parse(mt.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                            : 0;
                        var start = new TimeSpan(0, 0, minutes, seconds, millis);

                        var wordStart = mt.Index + mt.Length;
                        var wordEnd = m + 1 < timeMatches.Count ? timeMatches[m + 1].Index : line.Length;
                        var word = line.Substring(wordStart, wordEnd - wordStart);
                        if (word.Length == 0) continue; // 行尾锚点时间戳（如 [00:03.600]）无文本，跳过

                        TimeSpan duration;
                        if (m + 1 < timeMatches.Count)
                        {
                            var nextM = timeMatches[m + 1];
                            var nMin = int.Parse(nextM.Groups[1].Value);
                            var nSec = int.Parse(nextM.Groups[2].Value);
                            var nMs = nextM.Groups[3].Success
                                ? int.Parse(nextM.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                                : 0;
                            duration = new TimeSpan(0, 0, nMin, nSec, nMs) - start;
                        }
                        else
                        {
                            duration = TimeSpan.FromMilliseconds(500);
                        }

                        wordTimestamps.Add(new WordTimestamp { Word = word, Start = start, Duration = duration });
                        sb.Append(word);
                    }

                    if (wordTimestamps.Count > 0)
                    {
                        lyrics.Lines.Add(new LrcLyricLine
                        {
                            Timestamp = wordTimestamps[0].Start,
                            Text = sb.ToString(),
                            WordTimestamps = wordTimestamps
                        });
                        continue;
                    }
                }
            }

            // 提取歌词文本（最后一个 ] 之后的内容）
            var text = lastBracketIndex >= 0
                ? line.Substring(lastBracketIndex + 1).Trim()
                : "";

            // 跳过纯音乐标记
            if (text.Contains("纯音乐") || text.Contains("暂无歌词"))
            {
                text = "";
            }

            // 一个歌词行可能对应多个时间戳 [01:23.45][02:34.56]歌词
            foreach (Match match in timeMatches)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var millis = match.Groups[3].Success
                    ? int.Parse(match.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                    : 0;

                var timestamp = new TimeSpan(0, 0, minutes, seconds, millis);

                var wordTimestamps = ParseWordTimestamps(text, timestamp);
                var lineText = wordTimestamps != null ? string.Join("", wordTimestamps.Select(w => w.Word)) : text;
                // 有逐字时间戳时不做 SplitBilingual（翻译行会通过 MergeTranslationLines 合并）
                var (orig, trans) = wordTimestamps != null ? (lineText, (string?)null) : SplitBilingual(lineText);

                lyrics.Lines.Add(new LrcLyricLine
                {
                    Timestamp = timestamp,
                    Text = orig,
                    Translation = trans,
                    WordTimestamps = wordTimestamps
                });
            }
        }

        // 按时间戳排序（LRC 文件通常已有序，仅兜底排序）
        lyrics.Lines = lyrics.Lines.OrderBy(l => l.Timestamp).ToList();

        // 合并同时间戳的翻译行：
        // 格式如 [00:07.24]<00:07.24>미안해 ... 和 [00:07.24]对不起 ...
        // 两个行时间戳相同，翻译行没有逐字时间戳且文本较短（纯翻译），
        // 应合并为原文行的 Translation 字段
        MergeTranslationLines(lyrics);

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 合并同时间戳的翻译行到原文行
    /// <para>判断条件：两行时间戳相同，且其中一行没有逐字时间戳（或文本明显是翻译）</para>
    /// </summary>
    private static void MergeTranslationLines(LrcLyrics lyrics)
    {
        if (lyrics.Lines.Count < 2) return;

        var merged = new List<LrcLyricLine>();
        var i = 0;
        while (i < lyrics.Lines.Count)
        {
            var current = lyrics.Lines[i];

            // 查找后续同时间戳的行
            var j = i + 1;
            while (j < lyrics.Lines.Count && lyrics.Lines[j].Timestamp == current.Timestamp)
            {
                var next = lyrics.Lines[j];

                // 判断哪行是原文、哪行是翻译
                // 规则：有逐字时间戳的是原文；都没有时，第一行是原文
                if (current.WordTimestamps != null && current.WordTimestamps.Count > 0
                    && (next.WordTimestamps == null || next.WordTimestamps.Count == 0))
                {
                    // 当前行有逐字时间戳（原文），next 是翻译
                    if (string.IsNullOrEmpty(current.Translation))
                        current.Translation = next.Text;
                    i = j + 1;
                    goto AddCurrent;
                }

                if ((next.WordTimestamps != null && next.WordTimestamps.Count > 0)
                    && (current.WordTimestamps == null || current.WordTimestamps.Count == 0))
                {
                    // next 有逐字时间戳（原文），当前行是翻译
                    if (string.IsNullOrEmpty(next.Translation))
                        next.Translation = current.Text;
                    current = next;
                    i = j + 1;
                    goto AddCurrent;
                }

                // 两行都没有逐字时间戳，用 SplitBilingual 的结果判断
                // 如果当前行已有翻译，或者 next 的文本看起来是翻译（更短、不同语言）
                if (string.IsNullOrEmpty(current.Translation) && !string.IsNullOrEmpty(next.Text))
                {
                    // 检查是否是不同语言（如韩文+中文）
                    if (IsDifferentScript(current.Text, next.Text))
                    {
                        current.Translation = next.Text;
                        i = j + 1;
                        goto AddCurrent;
                    }
                }

                // 无法判断，保留两行
                j++;
            }

            i = j;

        AddCurrent:
            merged.Add(current);
        }

        lyrics.Lines = merged;
    }

    /// <summary>
    /// 合并同拍对唱行：把同一时刻的主唱 v1（左）和 v2（右）合并为单行双文本。
    /// <para>合并后：Primary 存左侧文本，SecondaryText 存右侧文本，和声行不合并。</para>
    /// </summary>
    private static void MergeDuetLines(LrcLyrics lyrics)
    {
        if (lyrics.Lines.Count < 2) return;

        const int mergeToleranceMs = 150; // 时间差容差 150ms
        var merged = new List<LrcLyricLine>();
        var used = new bool[lyrics.Lines.Count];

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            if (used[i]) continue;
            var current = lyrics.Lines[i];

            // 和声行不合并，保持独立
            if (current.IsBackingVocal)
            {
                merged.Add(current);
                used[i] = true;
                continue;
            }

            // 已带 SecondaryText 的行不再合并
            if (!string.IsNullOrEmpty(current.SecondaryText))
            {
                merged.Add(current);
                used[i] = true;
                continue;
            }

            // 寻找同拍且对齐方式互补的另一主唱行
            LrcLyricLine? partner = null;
            int partnerIdx = -1;
            for (int j = i + 1; j < lyrics.Lines.Count; j++)
            {
                if (used[j]) continue;
                var next = lyrics.Lines[j];
                if (next.IsBackingVocal) continue;
                if (!string.IsNullOrEmpty(next.SecondaryText)) continue;

                var diffMs = Math.Abs((next.Timestamp - current.Timestamp).TotalMilliseconds);
                if (diffMs > mergeToleranceMs) break; // 已排序，后面时间差更大

                // 仅合并对齐互补的 v1/v2 行：0+2
                if ((current.Alignment == 0 && next.Alignment == 2) ||
                    (current.Alignment == 2 && next.Alignment == 0))
                {
                    partner = next;
                    partnerIdx = j;
                    break;
                }
            }

            if (partner != null)
            {
                // current 为左，partner 为右；若 current 是右则交换
                if (current.Alignment == 0)
                {
                    current.SecondaryText = partner.Text;
                    current.SecondaryAlignment = partner.Alignment;
                }
                else
                {
                    current.SecondaryText = current.Text;
                    current.SecondaryAlignment = current.Alignment;
                    current.Text = partner.Text;
                    current.Alignment = partner.Alignment;
                }
                used[partnerIdx] = true;
            }

            merged.Add(current);
            used[i] = true;
        }

        lyrics.Lines = merged;
    }

    /// <summary>
    /// 判断两段文本是否使用了不同的文字系统（如韩文 vs 中文）
    /// </summary>
    private static bool IsDifferentScript(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return false;

        var script1 = GetDominantScript(text1);
        var script2 = GetDominantScript(text2);

        return script1 != script2 && script1 != ScriptType.Unknown && script2 != ScriptType.Unknown;
    }

    /// <summary>文字系统类型，用于区分原文与翻译</summary>
    private enum ScriptType { Unknown, Cjk, Japanese, Hangul, Latin }

    /// <summary>统计文本中各类字符的数量，返回占主导地位的文字系统</summary>
    private static ScriptType GetDominantScript(string text)
    {
        int cjk = 0, japanese = 0, hangul = 0, latin = 0;
        foreach (var ch in text)
        {
            if (IsJapanese(ch)) japanese++;
            else if (IsHangul(ch)) hangul++;
            else if (IsCjk(ch)) cjk++;
            else if (char.IsLetter(ch) && ch <= 0x007F) latin++;
        }
        // 日文判定：含假名字符则视为日文（即使也有汉字）
        if (japanese > 0) return ScriptType.Japanese;
        var max = Math.Max(cjk, Math.Max(hangul, latin));
        if (max == 0) return ScriptType.Unknown;
        if (max == cjk) return ScriptType.Cjk;
        if (max == hangul) return ScriptType.Hangul;
        return ScriptType.Latin;
    }

    /// <summary>判断字符是否为韩文字母</summary>
    private static bool IsHangul(char ch)
    {
        return (ch >= 0xAC00 && ch <= 0xD7AF) ||   // 韩文音节
               (ch >= 0x1100 && ch <= 0x11FF) ||   // 韩文字母 Jamo
               (ch >= 0x3130 && ch <= 0x318F);     // 韩文兼容字母
    }

    /// <summary>
    /// 解析纯文本歌词（无时间戳），为每行生成等间隔时间戳
    /// </summary>
    public LrcLyrics? ParsePlainTextLyrics(string text)
    {
        var lyrics = new LrcLyrics();

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        int lineIndex = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            var tagMatch = TagRegex.Match(line);
            if (tagMatch.Success)
            {
                lineIndex++;
                continue;
            }

            if (line.Contains("纯音乐") || line.Contains("暂无歌词"))
                continue;

            var timestamp = TimeSpan.FromSeconds(lineIndex * 5);
            lyrics.Lines.Add(new LrcLyricLine
            {
                Timestamp = timestamp,
                Text = line
            });
            lineIndex++;
        }

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 智能解析歌词内容：先检测格式（XML/JSON/LRC），再调用对应解析器。
    /// <para>关键：XML/JSON 内容绝不回退到 ParsePlainTextLyrics，避免显示原始代码</para>
    /// </summary>
    public LrcLyrics? TryParseLyrics(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 编码兜底：若误读导致含 0x00，尝试按 UTF-16 重新解释
        if (content.Contains('\0'))
            content = TryReinterpretAsUtf16(content);

        content = SanitizeForXml(content);
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 检测内容类型
        bool isXml = content.Contains("<tt") || content.Contains("<?xml")
            || content.Contains("xmlns=\"http://www.w3.org/ns/ttml")
            || (content.TrimStart().StartsWith("<") && content.Contains(">") && !content.TrimStart().StartsWith("["));
        bool isJson = content.TrimStart().StartsWith("{")
            && (content.Contains("\"lyrics\"") || content.Contains("\"lines\"") || content.Contains("\"role\"")
                || content.Contains("\"code\"") || content.Contains("\"message\"") || content.Contains("\"data\""));

        if (isXml)
        {
            // TTML 专用路径：绝不回退到 PlainText
            return ParseTtml(content);
        }

        if (isJson)
        {
            // AMLL JSON 专用路径：绝不回退到 PlainText
            return ParseAmll(content);
        }

        // LRC 或纯文本：先试 LRC，再试纯文本
        var lrc = ParseLrc(content);
        if (lrc != null) return lrc;

        // 防御：非歌词内容（JSON/XML/HTML/错误响应）不应作为纯文本歌词显示
        var trimmed = content.Trim();
        if ((trimmed.StartsWith("<") && trimmed.Contains(">"))
            || (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            || trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("LyricsService", "[LyricsService] 检测到非歌词内容，已过滤");
            return null;
        }

        return ParsePlainTextLyrics(content);
    }

    /// <summary>
    /// 解析 TTML (Timed Text Markup Language) 格式歌词
    /// 支持 W3C TTML 标准，常用于 Apple Music、Netflix 等平台
    /// </summary>
    /// <summary>
    /// 异步解析 TTML 格式（包装在 Task.Run 中避免阻塞 UI 线程）
    /// </summary>

    private List<WordTimestamp>? ParseWordTimestamps(string text, TimeSpan lineTimestamp)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var matches = WordTimeRegex.Matches(text);
        if (matches.Count == 0) return null;

        var result = new List<WordTimestamp>();
        var lastIndex = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var wordStart = m.Index + m.Length;
            var nextTagIndex = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var word = text.Substring(wordStart, nextTagIndex - wordStart).Trim();

            if (string.IsNullOrEmpty(word)) continue;

            var minutes = int.Parse(m.Groups[1].Value);
            var seconds = int.Parse(m.Groups[2].Value);
            var millis = m.Groups[3].Success
                ? int.Parse(m.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                : 0;
            var start = new TimeSpan(0, 0, minutes, seconds, millis);

            TimeSpan duration;
            if (i + 1 < matches.Count)
            {
                var nextM = matches[i + 1];
                var nm = int.Parse(nextM.Groups[1].Value);
                var ns = int.Parse(nextM.Groups[2].Value);
                var nms = nextM.Groups[3].Success
                    ? int.Parse(nextM.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                    : 0;
                var nextStart = new TimeSpan(0, 0, nm, ns, nms);
                duration = nextStart - start;
            }
            else
            {
                duration = TimeSpan.FromMilliseconds(500);
            }

            result.Add(new WordTimestamp
            {
                Word = word,
                Start = start,
                Duration = duration
            });
            lastIndex = nextTagIndex;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 解析 AMLL (Anni Music Lyrics Library) JSON 格式
    /// AMLL 是 JSON 格式的逐字歌词，常见于网易云/QQ音乐下载的歌词文件
    /// </summary>
}
