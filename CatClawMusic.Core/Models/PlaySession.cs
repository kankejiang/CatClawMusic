using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 播放会话日志模型，对应数据库 PlaySession 表。
/// 与 PlayHistory（按 SongId 聚合的累计次数 + 最近时间）不同，
/// PlaySession 每次播放都写入一条记录，用于支持听歌趋势、时段分布、连续听歌等逐次日志型统计。
/// </summary>
[Table("PlaySession")]
public class PlaySession
{
    /// <summary>自增主键</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>关联的歌曲 ID</summary>
    [Indexed]
    public int SongId { get; set; }

    /// <summary>播放发生时间（Unix 时间戳，秒）</summary>
    [Indexed]
    public long PlayedAt { get; set; }

    /// <summary>本次播放实际聆听时长（毫秒）。取自播放进度，可能小于歌曲全长。</summary>
    public long DurationMs { get; set; }
}
