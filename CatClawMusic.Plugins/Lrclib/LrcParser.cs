using System.Text.RegularExpressions;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 轻量 LRC 解析器（纯 .NET，无宿主耦合）：把 LRCLIB 返回的 syncedLyrics 文本
/// 解析为 <see cref="LrcLyrics"/>。支持 [mm:ss.xx] / [mm:ss.xxx] / [mm:ss] 时间戳、
/// 一行多时间戳、[ti:]/[ar:]/[al:] 等元数据标签；无时间轴的纯文本按整行歌词处理。
/// </summary>
public static class LrcParser
{
    /// <summary>[mm:ss.xx] / [mm:ss.xxx] / [mm:ss] 时间戳</summary>
    private static readonly Regex TimeRegex = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    /// <summary>[ti:...] 等元数据标签</summary>
    private static readonly Regex TagRegex = new(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 解析 LRC 文本。
    /// </summary>
    /// <returns>解析成功且至少包含一行歌词时返回对象，否则返回 null</returns>
    public static LrcLyrics? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var lyrics = new LrcLyrics();
        var lines = new List<LrcLyricLine>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 元数据标签行（可单独一行，也可与时间戳共存）
            var tagMatch = TagRegex.Match(line);
            if (tagMatch.Success && !line.TrimStart().StartsWith("["))
            {
                ApplyMetadata(lyrics.Metadata, tagMatch.Groups[1].Value.ToLowerInvariant(), tagMatch.Groups[2].Value.Trim());
                continue;
            }

            // 提取该行所有时间戳
            var matches = TimeRegex.Matches(line);
            if (matches.Count == 0)
            {
                // 无时间戳：纯文本歌词，整行作为一条歌词（Timestamp=0，播放时始终显示）
                if (!string.IsNullOrWhiteSpace(line.Trim()))
                {
                    lines.Add(new LrcLyricLine { Timestamp = TimeSpan.Zero, Text = line.Trim() });
                }
                continue;
            }

            // 去掉所有时间戳后剩下的部分是歌词文本
            var text = TimeRegex.Replace(line, string.Empty).Trim();
            foreach (Match m in matches)
            {
                var ts = ParseTimestamp(m);
                lines.Add(new LrcLyricLine { Timestamp = ts, Text = text });
            }
        }

        if (lines.Count == 0) return null;

        // 纯文本行（Timestamp=0）置前显示，避免干扰按时间排序的同步行
        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        lyrics.Lines = lines;
        return lyrics;
    }

    private static TimeSpan ParseTimestamp(Match m)
    {
        var minutes = int.Parse(m.Groups[1].Value);
        var seconds = int.Parse(m.Groups[2].Value);
        var fraction = m.Groups[3].Success && !string.IsNullOrEmpty(m.Groups[3].Value)
            ? int.Parse(m.Groups[3].Value.PadRight(3, '0'))
            : 0;
        return TimeSpan.FromMilliseconds(minutes * 60000 + seconds * 1000 + fraction);
    }

    private static void ApplyMetadata(LrcMetadata metadata, string key, string value)
    {
        switch (key)
        {
            case "ti": metadata.Title = value; break;
            case "ar": metadata.Artist = value; break;
            case "al": metadata.Album = value; break;
            case "by": metadata.Author = value; break;
            case "re": metadata.Maker = value; break;
            case "ve": metadata.Version = value; break;
        }
    }
}
