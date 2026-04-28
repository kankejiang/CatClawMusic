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
    Task<LrcLyrics?> GetLocalLyricsAsync(Song song);
    
    /// <summary>
    /// 解析 LRC 格式字符串
    /// </summary>
    LrcLyrics? ParseLrc(string lrcContent);
    
    /// <summary>
    /// 根据播放位置获取当前歌词行索引
    /// </summary>
    int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position);
}
