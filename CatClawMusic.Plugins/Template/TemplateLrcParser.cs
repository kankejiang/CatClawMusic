using System.Text.RegularExpressions;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Template;

/// <summary>
/// 示例：纯 .NET 的 LRC 歌词解析器（不依赖宿主 LyricsService，保持插件自治）。
/// <para>
/// 也可以直接复用宿主的公开解析器：从 DI 解析 <c>LyricsService</c> 后调用
/// <c>TryParseLyrics(text)</c>（支持 LRC/TTML/AMLL 自动识别），见 README「获取宿主服务」。
/// </para>
/// </summary>
public static class TemplateLrcParser
{
    private static readonly Regex TimeRegex = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    /// <summary>解析 LRC 文本；无有效歌词行时返回 null</summary>
    public static LrcLyrics? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var result = new LrcLyrics();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var matches = TimeRegex.Matches(line);
            if (matches.Count == 0) continue;

            var text = TimeRegex.Replace(line, string.Empty).Trim();
            foreach (Match m in matches)
            {
                var minutes = int.Parse(m.Groups[1].Value);
                var seconds = int.Parse(m.Groups[2].Value);
                var fraction = m.Groups[3].Success && m.Groups[3].Length > 0
                    ? int.Parse(m.Groups[3].Value.PadRight(3, '0'))
                    : 0;
                result.Lines.Add(new LrcLyricLine
                {
                    Timestamp = TimeSpan.FromMilliseconds(minutes * 60000 + seconds * 1000 + fraction),
                    Text = text
                });
            }
        }

        return result.Lines.Count > 0 ? result : null;
    }
}
