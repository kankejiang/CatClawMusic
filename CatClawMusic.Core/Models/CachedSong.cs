using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 缓存歌曲模型，对应数据库 CachedSongs 表
/// </summary>
[Table("CachedSongs")]
public class CachedSong
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>关联的歌曲 ID</summary>
    public int SongId { get; set; }

    /// <summary>缓存本地路径</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>缓存时间（Unix 时间戳）</summary>
    public long CachedAt { get; set; }

    /// <summary>缓存文件大小（字节）</summary>
    public long FileSize { get; set; }
}
