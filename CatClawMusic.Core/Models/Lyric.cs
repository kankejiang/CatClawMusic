using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 歌词缓存模型，对应数据库 Lyrics 表
/// </summary>
[Table("Lyrics")]
public class Lyric
{
    /// <summary>关联的歌曲 ID（主键）</summary>
    [PrimaryKey]
    public int SongId { get; set; }

    /// <summary>外部 LRC 文件路径</summary>
    public string? LrcPath { get; set; }

    /// <summary>歌词内容文本</summary>
    public string? Content { get; set; }
}
