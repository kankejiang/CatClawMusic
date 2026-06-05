using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 艺术家模型，对应数据库 Artists 表
/// </summary>
[Table("Artists")]
public class Artist
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>艺术家名称（唯一）</summary>
    [Unique, NotNull]
    public string Name { get; set; } = string.Empty;

    /// <summary>封面 URL 或路径</summary>
    public string? Cover { get; set; }

    /// <summary>性别</summary>
    public string? Gender { get; set; }

    /// <summary>国籍/地区</summary>
    public string? Region { get; set; }

    /// <summary>简介</summary>
    public string? Description { get; set; }
}
