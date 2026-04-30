using SQLite;

namespace CatClawMusic.Core.Models;

[Table("Songs")]
public class Song
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Title { get; set; } = string.Empty;

    /// <summary>艺术家 ID（外键）</summary>
    [Indexed]
    public int ArtistId { get; set; }

    /// <summary>专辑 ID（外键）</summary>
    [Indexed]
    public int AlbumId { get; set; }

    /// <summary>时长（毫秒）</summary>
    public int Duration { get; set; }

    /// <summary>文件路径</summary>
    [Unique, NotNull]
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }
    public int Bitrate { get; set; }

    /// <summary>专辑内曲目序号</summary>
    public int TrackNumber { get; set; }

    /// <summary>发行年份</summary>
    public int Year { get; set; }

    /// <summary>流派</summary>
    public string? Genre { get; set; }

    /// <summary>入库时间（Unix 时间戳）</summary>
    public long DateAdded { get; set; }

    /// <summary>文件最后修改时间（Unix 时间戳）</summary>
    public long DateModified { get; set; }

    /// <summary>专辑封面路径（本地缓存）</summary>
    public string? CoverArtPath { get; set; }

    /// <summary>歌词文件路径</summary>
    public string? LyricsPath { get; set; }

    public SongSource Source { get; set; } = SongSource.Local;

    // ── 以下为兼容查询的运行时字段，不存储 ──

    [Ignore]
    public string Artist { get; set; } = string.Empty;

    [Ignore]
    public string Album { get; set; } = string.Empty;
}

public enum SongSource
{
    Local,
    WebDAV,
    Cache
}
