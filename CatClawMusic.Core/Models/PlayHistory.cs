using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 播放历史模型，对应数据库 PlayHistory 表
/// </summary>
[Table("PlayHistory")]
public class PlayHistory
{
    /// <summary>自增主键</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>关联的歌曲 ID</summary>
    [Indexed]
    public int SongId { get; set; }

    /// <summary>最近播放时间（Unix 时间戳）</summary>
    public long PlayedAt { get; set; }

    /// <summary>播放次数</summary>
    public int PlayCount { get; set; } = 1;
}
