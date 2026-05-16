using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 专辑模型，对应数据库 Albums 表
/// </summary>
[Table("Albums")]
public class Album
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>专辑标题</summary>
    [NotNull]
    public string Title { get; set; } = string.Empty;

    /// <summary>专辑名称（旧字段兼容）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>艺术家名称</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>封面路径</summary>
    public string? CoverArtPath { get; set; }

    /// <summary>歌曲数量</summary>
    public int SongCount { get; set; }

    /// <summary>发行年份</summary>
    public int? Year { get; set; }

    /// <summary>艺术家 ID（外键）</summary>
    [Indexed]
    public int ArtistId { get; set; }

    /// <summary>封面 URL 或路径</summary>
    public string? Cover { get; set; }

    /// <summary>发行年份（新字段）</summary>
    public int? ReleaseYear { get; set; }
}
