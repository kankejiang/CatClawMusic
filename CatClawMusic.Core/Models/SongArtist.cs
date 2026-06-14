using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 歌曲-艺术家多对多关联表，对应数据库 SongArtists 表。
/// 当一首歌曲有多个艺术家（如 "周杰伦/林俊杰"）时，每位艺术家都有一条记录，均可指向该歌曲。
/// </summary>
[Table("SongArtists")]
public class SongArtist
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>歌曲 ID（外键）</summary>
    [Indexed, NotNull]
    public int SongId { get; set; }

    /// <summary>艺术家 ID（外键）</summary>
    [Indexed, NotNull]
    public int ArtistId { get; set; }
}
