using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 歌词服务接口
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// 获取歌词（优先本地，失败后尝试网络提供者）
    /// </summary>
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    
    /// <summary>
    /// 从本地文件获取歌词
    /// </summary>
    Task<LrcLyrics?> GetLocalLyricsAsync(Song song, bool skipEmbedded = false, bool preferEmbedded = false);
    
    /// <summary>
    /// 解析 LRC 格式字符串
    /// </summary>
    LrcLyrics? ParseLrc(string lrcContent);

    /// <summary>
    /// 智能解析歌词内容：先检测格式（XML/JSON/LRC），再调用对应解析器
    /// </summary>
    LrcLyrics? TryParseLyrics(string content);

    /// <summary>
    /// 查找外部歌词文件并返回文本内容（含 SAF content:// 回退）
    /// </summary>
    Task<string?> FindExternalLyricsTextAsync(Song song);

    /// <summary>
    /// 解析 TTML (Timed Text Markup Language) 格式歌词
    /// 支持 W3C TTML 标准，常用于 Apple Music、Netflix 等平台
    /// </summary>
    LrcLyrics? ParseTtml(string ttmlContent);
    
    /// <summary>
    /// 从文件解析 TTML 格式
    /// </summary>
    LrcLyrics? ParseTtmlFromFile(string filePath);
    
    /// <summary>
    /// 异步从文件解析 TTML 格式
    /// </summary>
    Task<LrcLyrics?> ParseTtmlFromFileAsync(string filePath);
    
    /// <summary>
    /// 根据播放位置获取当前歌词行索引
    /// </summary>
    int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position);

    /// <summary>
    /// 根据播放位置获取当前行内的逐字歌词索引（-1 表示无逐字数据）
    /// </summary>
    int GetCurrentWordIndex(LrcLyricLine? line, TimeSpan position);
}
