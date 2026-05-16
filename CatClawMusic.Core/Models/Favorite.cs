using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 收藏模型，对应数据库 Favorites 表
/// </summary>
[Table("Favorites")]
public class Favorite
{
    /// <summary>关联的歌曲 ID（主键）</summary>
    [PrimaryKey]
    public int SongId { get; set; }

    /// <summary>收藏时间（Unix 时间戳）</summary>
    public long AddedAt { get; set; }
}
