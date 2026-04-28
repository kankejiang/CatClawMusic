namespace CatClawMusic.Core.Models;

/// <summary>
/// 歌曲模型
/// </summary>
public class Song
{
    public int Id { get; set; }
    
    /// <summary>
    /// 歌曲标题
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// 艺术家
    /// </summary>
    public string Artist { get; set; } = string.Empty;
    
    /// <summary>
    /// 专辑
    /// </summary>
    public string Album { get; set; } = string.Empty;
    
    /// <summary>
    /// 时长（秒）
    /// </summary>
    public int Duration { get; set; }
    
    /// <summary>
    /// 文件路径（本地路径或 WebDAV URL）
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// 比特率（kbps）
    /// </summary>
    public int Bitrate { get; set; }
    
    /// <summary>
    /// 文件最后修改时间（Unix 时间戳）
    /// </summary>
    public long LastModified { get; set; }
    
    /// <summary>
    /// 专辑封面路径（本地缓存路径）
    /// </summary>
    public string? CoverArtPath { get; set; }
    
    /// <summary>
    /// 歌曲来源类型
    /// </summary>
    public SongSource Source { get; set; } = SongSource.Local;
    
    /// <summary>
    /// 播放次数
    /// </summary>
    public int PlayCount { get; set; }
    
    /// <summary>
    /// 总播放时长（秒）
    /// </summary>
    public long TotalPlayTime { get; set; }
    
    /// <summary>
    /// 最后一次播放时间（Unix 时间戳）
    /// </summary>
    public long LastPlayedAt { get; set; }
    
    /// <summary>
    /// 跳过次数
    /// </summary>
    public int SkipCount { get; set; }
    
    /// <summary>
    /// 完整播放次数（播放 > 80% 时长）
    /// </summary>
    public int CompletePlayCount { get; set; }
}

/// <summary>
/// 歌曲来源类型
/// </summary>
public enum SongSource
{
    Local,      // 本地文件
    WebDAV,     // WebDAV 网络文件
    Cache       // 缓存文件
}
