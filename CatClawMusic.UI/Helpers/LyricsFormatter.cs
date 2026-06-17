using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Helpers;

/// <summary>歌词显示格式化工具</summary>
public static class LyricsFormatter
{
    /// <summary>将 LrcLyrics 格式化为可读文本（含翻译和对唱角色标记）</summary>
    public static string FormatLrcLyrics(LrcLyrics lyrics)
    {
        if (lyrics.Lines == null || lyrics.Lines.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        bool hasAlignment = lyrics.HasPerLineAlignment;

        foreach (var line in lyrics.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');

            // 对唱歌词：加角色前缀
            string prefix = "";
            if (hasAlignment)
            {
                prefix = line.Alignment switch
                {
                    0 => "【男】",
                    2 => "【女】",
                    _ => ""
                };
            }

            // 原文
            sb.Append(prefix);
            sb.Append(line.Text);

            // 翻译（如果有）
            if (!string.IsNullOrEmpty(line.Translation))
            {
                sb.Append('\n');
                if (hasAlignment) sb.Append("      "); // 对齐缩进
                sb.Append(line.Translation);
            }
        }

        return sb.ToString();
    }
}
