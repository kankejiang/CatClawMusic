using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>歌词服务 —— partial 分域文件。</summary>
public partial class LyricsService
{
    public static LrcLyrics? ParseAmll(string amllContent)
    {
        try
        {
            if (amllContent.Length > MaxLyricsParseSize)
            {
                Log.Debug("LyricsService", $"[LyricsService] AMLL 文件过大（{amllContent.Length / 1024}KB），跳过解析");
                return null;
            }

            using var doc = JsonDocument.Parse(amllContent);
            var root = doc.RootElement;

            // AMLL 常见结构：{ "lyrics": [...], "version": "..." }
            if (!root.TryGetProperty("lyrics", out var lyricsArray) && 
                !root.TryGetProperty("data", out lyricsArray))
                return null;

            var result = new LrcLyrics();
            var lines = new List<LrcLyricLine>();

            foreach (var item in lyricsArray.EnumerateArray())
            {
                // 每行结构：{ "startTime": 7224, "endTime": 10500, "content": "...", "words": [...] }
                if (!item.TryGetProperty("startTime", out var startProp)) continue;
                var startMs = startProp.GetInt64();
                var start = TimeSpan.FromMilliseconds(startMs);

                var text = "";
                if (item.TryGetProperty("content", out var contentProp))
                    text = contentProp.GetString() ?? "";
                else if (item.TryGetProperty("lyric", out var lyricProp))
                    text = lyricProp.GetString() ?? "";

                var (amllAlignment, amllBacking) = ParseAmllRole(item);
                var singer = item.TryGetProperty("singer", out var singerProp)
                    ? singerProp.GetString()
                    : null;
                var line = new LrcLyricLine
                {
                    Timestamp = start,
                    Text = text,
                    // 解析 AMLL 的 role 字段（用于对唱布局）
                    Alignment = amllAlignment,
                    IsBackingVocal = amllBacking,
                    Role = singer,
                    SingerName = singer
                };

                // 解析逐字时间戳（AMLL 特有的 words 数组）
                if (item.TryGetProperty("words", out var wordsArray))
                {
                    var wordTimestamps = new List<WordTimestamp>();
                    foreach (var word in wordsArray.EnumerateArray())
                    {
                        if (!word.TryGetProperty("word", out var wordProp)) continue;
                        var wordText = wordProp.GetString() ?? "";
                        
                        if (!word.TryGetProperty("startTime", out var wsProp)) continue;
                        var ws = TimeSpan.FromMilliseconds(wsProp.GetInt64());

                        TimeSpan dur;
                        if (word.TryGetProperty("endTime", out var weProp))
                            dur = TimeSpan.FromMilliseconds(weProp.GetInt64()) - ws;
                        else if (word.TryGetProperty("duration", out var wdProp))
                            dur = TimeSpan.FromMilliseconds(wdProp.GetInt64());
                        else
                            dur = TimeSpan.FromMilliseconds(200);

                        if (dur <= TimeSpan.Zero) dur = TimeSpan.FromMilliseconds(200);

                        wordTimestamps.Add(new WordTimestamp
                        {
                            Word = wordText,
                            Start = ws,
                            Duration = dur
                        });
                    }
                    line.WordTimestamps = wordTimestamps.Count > 0 ? wordTimestamps : null;
                }

                lines.Add(line);
            }

            if (lines.Count == 0) return null;
            // 按时间戳排序（与 TTML/LRC 保持一致）
            result.Lines = lines.OrderBy(l => l.Timestamp).ToList();
            // 合并同拍对唱行（v1 左 + v2 右 → 单行左右分栏）
            MergeDuetLines(result);
            // 如果任何一行有非默认对齐方式或双文本，标记为逐行对齐
            result.HasPerLineAlignment = result.Lines.Any(l => l.Alignment != 1 || l.SecondaryText != null);
            // 合并翻译行（同时间戳的原文+翻译行合并）
            MergeTranslationLines(result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 AMLL 每行的 role 字段，返回对齐方式与是否和声
    /// role 常见值：male / female / duet / chorus / v1 / v2 / v1000 等
    /// </summary>
    private static (int alignment, bool isBackingVocal) ParseAmllRole(JsonElement item)
    {
        if (!item.TryGetProperty("role", out var roleProp))
            return (1, false); // 默认居中

        var role = roleProp.GetString()?.ToLowerInvariant() ?? "";
        return InferRoleAlignment(role);
    }

    /// <summary>从文本中提取带结束标记的有限长度子串，避免把二进制尾部当作歌词</summary>
    private static string? ExtractBoundedSubstring(string text, int startIndex, string endMarker, int maxLength)
    {
        var maxEnd = Math.Min(text.Length, startIndex + maxLength);
        var endIndex = text.IndexOf(endMarker, startIndex, maxEnd - startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0) return null;
        return text.Substring(startIndex, endIndex + endMarker.Length - startIndex);
    }

    /// <summary>从文本中提取一个完整的 JSON 对象（带字符串转义处理）</summary>
    private static string? ExtractBoundedJson(string text, int startIndex, int maxLength)
    {
        if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '{') return null;
        var maxEnd = Math.Min(text.Length, startIndex + maxLength);
        int braceCount = 0;
        bool inString = false;
        bool escape = false;
        for (int i = startIndex; i < maxEnd; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') braceCount++;
            else if (c == '}') braceCount--;
            if (braceCount == 0)
                return text.Substring(startIndex, i - startIndex + 1);
        }
        return null;
    }

    /// <summary>
    /// 兜底：对音频文件做二进制扫描，搜索内嵌的 TTML/AMLL 歌词标记
    /// 适用于 M4A 自定义 atom 等 TagLibSharp 读不到的场景
    /// </summary>
}
