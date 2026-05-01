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
        System.Diagnostics.Debug.WriteLine($"[Lyrics] song.FilePath={songPath}");
        System.Diagnostics.Debug.WriteLine($"[Lyrics] File.Exists={File.Exists(songPath ?? "")}");
        if (string.IsNullOrEmpty(songPath) || !File.Exists(songPath))
            return null;

        // 1. 读取音频文件内嵌歌词
        var embeddedLyrics = TagReader.ReadEmbeddedLyrics(songPath);
        System.Diagnostics.Debug.WriteLine($"[Lyrics] embeddedLyrics length={embeddedLyrics?.Length ?? -1}");
        if (!string.IsNullOrWhiteSpace(embeddedLyrics))
        {
            var parsed = ParseLrc(embeddedLyrics);
            if (parsed != null) return parsed;
        }

        // 2. 查找同名 .lrc 文件
        var directory = Path.GetDirectoryName(songPath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(songPath);

        var lrcPath = Path.Combine(directory, fileNameWithoutExt + ".lrc");
        System.Diagnostics.Debug.WriteLine($"[Lyrics] looking for: {lrcPath}, exists={File.Exists(lrcPath)}");
        if (File.Exists(lrcPath))
        {
            var content = await File.ReadAllTextAsync(lrcPath);
            var parsed = ParseLrc(content);
            if (parsed != null) return parsed;
        }

        // 3. 查找同目录下其他 .lrc 文件
        var lrcFiles = Directory.GetFiles(directory, "*.lrc");
        System.Diagnostics.Debug.WriteLine($"[Lyrics] dir={directory}, .lrc files count={lrcFiles.Length}");
        foreach (var lrcFile in lrcFiles)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics]   found: {lrcFile}");
            var lrcName = Path.GetFileNameWithoutExtension(lrcFile);
            if (fileNameWithoutExt.Contains(lrcName) || lrcName.Contains(fileNameWithoutExt))
            {
                var content = await File.ReadAllTextAsync(lrcFile);
                var parsed = ParseLrc(content);
                if (parsed != null) return parsed;
            }
        }

        System.Diagnostics.Debug.WriteLine("[Lyrics] no lyrics found");
        return null;
    }

    /// <summary>
    /// 解析 LRC 格式字符串（增强版，兼容多种时间戳格式）
    /// 支持 [mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
    /// </summary>
    public LrcLyrics? ParseLrc(string lrcContent)
    {
        var lyrics = new LrcLyrics();

        // 标准化换行符
        lrcContent = lrcContent.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = lrcContent.Split('\n');

        // 时间戳正则：兼容 [mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
        var timeRegex = new Regex(@"\[(\d+):(\d+)(?:\.(\d+))?\]");
        var tagRegex = new Regex(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.IgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 跳过注释行
            if (line.StartsWith("//")) continue;

            // 解析元数据标签
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

            // 简单的时间戳有效性校验（从方糖音乐移植的巧技）
            // 如果行长度 > 10 且 Substring(1,5) 是合法 DateTime 格式，则为有效歌词行
            if (line.Length > 10 && !DateTime.TryParse(line.Substring(1, 5), out _))
                continue;

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
