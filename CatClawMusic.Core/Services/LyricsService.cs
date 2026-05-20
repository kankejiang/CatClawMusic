using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 歌词服务实现（LRC 格式解析 + 多源查找）
/// </summary>
public class LyricsService : ILyricsService
{
    /// <summary>插件管理器（可选，由 UI 层设置）</summary>
    public IPluginManager? PluginManager { get; set; }

    /// <summary>时间戳正则 [mm:ss.xx]</summary>
    private static readonly Regex TimeRegex = new(@"\[(\d+):(\d+)(?:\.(\d+))?\]", RegexOptions.Compiled);
    /// <summary>逐字时间戳正则 &lt;mm:ss.xx&gt;</summary>
    private static readonly Regex WordTimeRegex = new(@"<(\d+):(\d+)(?:\.(\d+))?>", RegexOptions.Compiled);
    /// <summary>元数据标签正则 [ti:...] 等</summary>
    private static readonly Regex TagRegex = new(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>文件扩展名正则</summary>
    private static readonly Regex ExtensionRegex = new(@"\.\w+$", RegexOptions.Compiled);

    /// <summary>
    /// 获取歌词（优先级：同名 .lrc > 嵌入歌词 > 插件）
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        var lyrics = await GetLocalLyricsAsync(song);
        if (lyrics != null) return lyrics;

        if (PluginManager != null)
        {
            var providers = PluginManager.GetEnabledPlugins<ILyricsProviderPlugin>();
            foreach (var provider in providers)
            {
                try
                {
                    if (!provider.IsAvailable) continue;
                    lyrics = await provider.GetLyricsAsync(song);
                    if (lyrics != null) return lyrics;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    public async Task<LrcLyrics?> GetLocalLyricsAsync(Song song, bool skipEmbedded = false)
    {
        var songPath = song.FilePath;

        bool isContentUri = !string.IsNullOrEmpty(songPath) && songPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

        if (isContentUri)
        {
            var lrcUri = ConstructLrcUri(songPath);
            if (lrcUri != null)
            {
                var content = await ReadContentUriAsync(lrcUri);
                if (content != null) return ParseLrc(content);
            }
        }
        else
        {
            if (File.Exists(songPath))
            {
                var dir = Path.GetDirectoryName(songPath) ?? "";
                var nameNoExt = Path.GetFileNameWithoutExtension(songPath);
                var lrcPath = Path.Combine(dir, nameNoExt + ".lrc");
                if (File.Exists(lrcPath))
                {
                    try { return ParseLrc(await File.ReadAllTextAsync(lrcPath)); }
                    catch { }
                }
            }
        }

        if (!skipEmbedded && !isContentUri && !string.IsNullOrEmpty(songPath) && File.Exists(songPath))
        {
            var embeddedLyrics = TagReader.ReadEmbeddedLyrics(songPath);
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                var parsed = ParseLrc(embeddedLyrics);
                if (parsed != null) return parsed;
            }
        }

        return null;
    }

    /// <summary>从 SAF content URI 构造同名 .lrc 的 content URI</summary>
    internal static string? ConstructLrcUri(string songUri)
    {
        try
        {
            int docIdx = songUri.LastIndexOf("/document/", StringComparison.Ordinal);
            if (docIdx < 0) return null;
            string prefix = songUri.Substring(0, docIdx + "/document/".Length);
            string docId = songUri.Substring(docIdx + "/document/".Length);
            string newDocId = ExtensionRegex.Replace(docId, ".lrc");
            if (newDocId == docId) return null;
            return prefix + newDocId;
        }
        catch { return null; }
    }

    /// <summary>读取 content:// URI 文本（由平台层注入）</summary>
    public static Func<string, Task<string?>>? ContentUriReader { get; set; }

    /// <summary>通过注入的 ContentUriReader 读取 content URI 内容</summary>
    private static async Task<string?> ReadContentUriAsync(string uri)
    {
        if (ContentUriReader != null)
            return await ContentUriReader(uri);
        return null;
    }

    /// <summary>尝试将 SAF content:// URI 转换为真实文件系统路径</summary>
    private static string? TryConvertContentUriToPath(string uri)
    {
        try
        {
            // content://com.android.externalstorage.documents/tree/primary%3AMusic/document/primary%3AMusic%2F...
            var decoded = Uri.UnescapeDataString(uri);
            // 提取 document ID 部分（最后一个 /document/ 之后）
            int docIdx = decoded.LastIndexOf("/document/", StringComparison.Ordinal);
            if (docIdx < 0) return null;
            string docId = decoded.Substring(docIdx + "/document/".Length);

            // primary:Foo/bar → /storage/emulated/0/Foo/bar
            if (docId.StartsWith("primary:", StringComparison.Ordinal))
            {
                string subPath = docId.Substring("primary:".Length);
                string fullPath = "/storage/emulated/0/" + subPath;
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 解析 LRC 格式字符串（增强版，兼容多种时间戳格式）
    /// 支持 [mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
    /// </summary>
    public LrcLyrics? ParseLrc(string lrcContent)
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

            // 提取歌词文本（最后一个 ] 之后的内容）
            var lastBracketIndex = line.LastIndexOf(']');
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

                // 解析逐字时间戳（如果有 <mm:ss.xx>word 格式）
                var wordTimestamps = ParseWordTimestamps(text, timestamp);

                lyrics.Lines.Add(new LrcLyricLine
                {
                    Timestamp = timestamp,
                    Text = wordTimestamps != null ? string.Join("", wordTimestamps.Select(w => w.Word)) : text,
                    WordTimestamps = wordTimestamps
                });
            }
        }

        // 按时间戳排序（LRC 文件通常已有序，仅兜底排序）
        lyrics.Lines = lyrics.Lines.OrderBy(l => l.Timestamp).ToList();

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 根据播放位置获取当前歌词行索引（二分查找，O(log n)）
    /// </summary>
    public int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position)
    {
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0) return -1;

        var lines = lyrics.Lines;
        int lo = 0, hi = lines.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (lines[mid].Timestamp <= position)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return hi;
    }

    /// <summary>
    /// 根据播放位置获取当前行内的逐字歌词索引（遍历查找，O(n)）
    /// </summary>
    /// <returns>当前高亮字的索引，-1 表示无逐字数据</returns>
    public int GetCurrentWordIndex(LrcLyricLine? line, TimeSpan position)
    {
        if (line?.WordTimestamps == null || line.WordTimestamps.Count == 0) return -1;
        for (int i = line.WordTimestamps.Count - 1; i >= 0; i--)
        {
            if (line.WordTimestamps[i].Start <= position)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 解析行内逐字时间戳（格式：&lt;mm:ss.xx&gt;word &lt;mm:ss.xx&gt;word ...）
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
}
