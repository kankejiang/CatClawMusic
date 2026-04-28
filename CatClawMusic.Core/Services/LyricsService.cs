using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Text.RegularExpressions;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 歌词服务实现（LRC 格式解析）
/// </summary>
public class LyricsService : ILyricsService
{
    /// <summary>
    /// 获取歌词（优先本地，失败后尝试网络提供者）
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        // 1. 尝试本地歌词文件
        var localLyrics = await GetLocalLyricsAsync(song);
        if (localLyrics != null) return localLyrics;
        
        // 2. TODO: 尝试网络提供者（预留接口）
        
        return null;
    }
    
    /// <summary>
    /// 从本地文件获取歌词
    /// </summary>
    public async Task<LrcLyrics?> GetLocalLyricsAsync(Song song)
    {
        // 匹配规则：
        // 1. 同名 LRC 文件（如 song.mp3 → song.lrc）
        // 2. 同一目录下的 LRC 文件
        
        var songPath = song.FilePath;
        var directory = Path.GetDirectoryName(songPath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(songPath);
        
        // 尝试同名 LRC 文件
        var lrcPath = Path.Combine(directory ?? "", fileNameWithoutExt + ".lrc");
        if (File.Exists(lrcPath))
        {
            var content = await File.ReadAllTextAsync(lrcPath);
            return ParseLrc(content);
        }
        
        return null;
    }
    
    /// <summary>
    /// 解析 LRC 格式字符串
    /// </summary>
    public LrcLyrics? ParseLrc(string lrcContent)
    {
        var lyrics = new LrcLyrics();
        var lines = lrcContent.Split('\n');
        
        var timeRegex = new Regex(@"\[(\d+):(\d+)\.(\d+)\]");
        var tagRegex = new Regex(@"\[(ti|ar|al|by|re|ve):(.+)\]");
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // 解析元数据标签
            var tagMatch = tagRegex.Match(trimmedLine);
            if (tagMatch.Success)
            {
                var tag = tagMatch.Groups[1].Value;
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
            var timeMatches = timeRegex.Matches(trimmedLine);
            if (timeMatches.Count == 0) continue;
            
            // 提取歌词文本（最后一个 ] 之后的内容）
            var lastBracketIndex = trimmedLine.LastIndexOf(']');
            var text = lastBracketIndex >= 0 ? trimmedLine.Substring(lastBracketIndex + 1).Trim() : "";
            
            // 一个歌词行可能对应多个时间戳
            foreach (Match match in timeMatches)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var milliseconds = int.Parse(match.Groups[3].Value.PadRight(3, '0').Substring(0, 3));
                
                var timestamp = new TimeSpan(0, 0, minutes, seconds, int.Parse(milliseconds.ToString().Substring(0, 3)));
                
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
        
        // 找到最后一个时间戳 <= 当前位置的歌词行
        var index = -1;
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            if (lyrics.Lines[i].Timestamp <= position)
            {
                index = i;
            }
            else
            {
                break;
            }
        }
        
        return index;
    }
}
