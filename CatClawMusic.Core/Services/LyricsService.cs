using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 歌词服务实现（LRC 格式解析 + 多源查找）
/// 从方糖音乐播放器移植并增强
/// </summary>
public class LyricsService : ILyricsService
{
    /// <summary>
    /// 获取歌词（优先级：嵌入歌词 > 用户目录 > 同名 .lrc > 网络）
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        var lyrics = await GetLocalLyricsAsync(song);
        if (lyrics != null) return lyrics;

        // TODO: 尝试网络歌词提供者（插件体系预留）

        return null;
    }

    /// <summary>
    /// 从本地获取歌词（多源查找）
    /// </summary>
    public async Task<LrcLyrics?> GetLocalLyricsAsync(Song song)
    {
        var songPath = song.FilePath;

        bool isContentUri = !string.IsNullOrEmpty(songPath) && songPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

        // 1. 读取嵌入歌词（需要真实路径文件访问）
        if (!isContentUri && !string.IsNullOrEmpty(songPath) && File.Exists(songPath))
        {
            var embeddedLyrics = TagReader.ReadEmbeddedLyrics(songPath);
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                var parsed = ParseLrc(embeddedLyrics);
                if (parsed != null) return parsed;
            }
        }

        // 2. 查找同名 .lrc
        if (isContentUri)
        {
            // SAF content URI → 构造同路径 .lrc URI
            var lrcUri = ConstructLrcUri(songPath);
            if (lrcUri != null)
            {
                var content = await ReadContentUriAsync(lrcUri);
                if (content != null) return ParseLrc(content);
            }
        }
        else
        {
            // 文件系统路径
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
            string newDocId = System.Text.RegularExpressions.Regex.Replace(docId, @"\.\w+$", ".lrc");
            if (newDocId == docId) return null;
            return prefix + newDocId;
        }
        catch { return null; }
    }

    /// <summary>读取 content:// URI 文本（由平台层注入）</summary>
    public static Func<string, Task<string?>>? ContentUriReader { get; set; }

    private static async Task<string?> ReadContentUriAsync(string uri)
    {
        if (ContentUriReader != null)
            return await ContentUriReader(uri);
        return null;
    }

    /// <summary>SAF content:// URI → 真实文件系统路径</summary>
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

        var timeRegex = new Regex(@"\[(\d+):(\d+)(?:\.(\d+))?\]");
        var tagRegex = new Regex(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.IgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            var tagMatch = tagRegex.Match(line);
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

            // 时间戳行校验
            if (line.Length > 10 && !DateTime.TryParse(line.Substring(1, 5), out _))
            {
                continue;
            }

            // 解析歌词行
            var timeMatches = timeRegex.Matches(line);
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

                lyrics.Lines.Add(new LrcLyricLine
                {
                    Timestamp = timestamp,
                    Text = text
                });
            }
        }

        // 按时间戳排序
        lyrics.Lines = lyrics.Lines.OrderBy(l => l.Timestamp).ToList();

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 根据播放位置获取当前歌词行索引
    /// </summary>
    public int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position)
    {
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0) return -1;

        var index = -1;
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            if (lyrics.Lines[i].Timestamp <= position)
                index = i;
            else
                break;
        }

        return index;
    }
}
