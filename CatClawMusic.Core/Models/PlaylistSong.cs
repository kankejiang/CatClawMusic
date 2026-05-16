using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 播放列表歌曲关联模型，对应数据库 PlaylistSongs 表
/// </summary>
[Table("PlaylistSongs")]
public class PlaylistSong
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>播放列表 ID（外键）</summary>
    [Indexed]
    public int PlaylistId { get; set; }

    /// <summary>歌曲 ID（外键）</summary>
    [Indexed]
    public int SongId { get; set; }

    /// <summary>在播放列表中的排序位置</summary>
    public int Position { get; set; }
}
