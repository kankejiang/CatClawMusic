using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 歌曲模型，对应数据库 Songs 表
/// </summary>
[Table("Songs")]
public class Song
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>歌曲标题</summary>
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

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>比特率（kbps）</summary>
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

    /// <summary>歌曲来源类型（本地 / WebDAV / 缓存）</summary>
    public SongSource Source { get; set; } = SongSource.Local;

    /// <summary>远程协议类型，用于区分 WebDAV 和 Navidrome</summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.WebDAV;

    /// <summary>远程歌曲唯一标识（Subsonic songId），用于网络歌曲去重</summary>
    public string? RemoteId { get; set; }

    /// <summary>Android MediaStore 音频记录 ID，用于通过 ContentResolver.LoadThumbnail 快速加载封面</summary>
    public long MediaStoreId { get; set; }

    // ── 以下为兼容查询的运行时字段，不存储 ──

    /// <summary>艺术家名称（运行时赋值，不持久化）</summary>
    [Ignore]
    public string Artist { get; set; } = string.Empty;

    /// <summary>专辑名称（运行时赋值，不持久化）</summary>
    [Ignore]
    public string Album { get; set; } = string.Empty;

    /// <summary>播放次数（运行时赋值，不持久化，来自 PlayHistory）</summary>
    [Ignore]
    public int PlayCount { get; set; }

    /// <summary>标记本地歌曲是否同时存在于网络（运行时赋值，不持久化）</summary>
    [Ignore]
    public bool IsAlsoOnNetwork { get; set; }

    /// <summary>标记网络歌曲是否同时存在于本地（运行时赋值，不持久化）</summary>
    [Ignore]
    public bool IsAlsoLocal { get; set; }
}

/// <summary>
/// 歌曲来源类型
/// </summary>
public enum SongSource
{
    Local,
    WebDAV,
    SMB,
    Cache
}
