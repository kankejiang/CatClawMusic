using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Models;

/// <summary>艺术家模型</summary>
public class Artist
{
    /// <summary>主键 ID</summary>
    public int Id { get; set; }

    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";

    /// <summary>封面 URL 或路径</summary>
    public string? Cover { get; set; }

    /// <summary>性别</summary>
    public string? Gender { get; set; }

    /// <summary>生日</summary>
    public string? Birthday { get; set; }

    /// <summary>国籍/地区</summary>
    public string? Region { get; set; }

    /// <summary>简介</summary>
    public string? Description { get; set; }
}
