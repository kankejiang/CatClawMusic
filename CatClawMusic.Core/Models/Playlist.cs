namespace CatClawMusic.Core.Models;

/// <summary>
/// 播放列表模型
/// </summary>
public class Playlist
{
    public int Id { get; set; }
    
    /// <summary>
    /// 播放列表名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 创建时间（Unix 时间戳）
    /// </summary>
    public long CreatedAt { get; set; }
    
    /// <summary>
    /// 更新时间（Unix 时间戳）
    /// </summary>
    public long UpdatedAt { get; set; }
    
    /// <summary>
    /// 歌曲数量
    /// </summary>
    public int SongCount { get; set; }
    
    /// <summary>
    /// 是否系统播放列表（如"最近播放"、"收藏"）
    /// </summary>
    public bool IsSystem { get; set; }
}
