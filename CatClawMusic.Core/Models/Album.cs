namespace CatClawMusic.Core.Models;

/// <summary>
/// 专辑模型
/// </summary>
public class Album
{
    public int Id { get; set; }
    
    /// <summary>
    /// 专辑名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 艺术家
    /// </summary>
    public string Artist { get; set; } = string.Empty;
    
    /// <summary>
    /// 专辑封面路径
    /// </summary>
    public string? CoverArtPath { get; set; }
    
    /// <summary>
    /// 歌曲数量
    /// </summary>
    public int SongCount { get; set; }
    
    /// <summary>
    /// 年份
    /// </summary>
    public int Year { get; set; }
}
