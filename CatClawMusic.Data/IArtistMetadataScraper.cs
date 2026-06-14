namespace CatClawMusic.Data;

/// <summary>艺术家元数据刮削器统一接口</summary>
public interface IArtistMetadataScraper
{
    /// <summary>来源名称（如"网易云"、"AI 搜索"）</summary>
    string SourceName { get; }

    /// <summary>搜索艺术家，返回多个匹配结果</summary>
    Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10);

    /// <summary>下载并保存艺术家封面到缓存，返回缓存路径</summary>
    Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName);
}

/// <summary>统一的艺术家搜索结果模型</summary>
public class ArtistSearchResult
{
    public string Source { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string? Alias { get; set; }

    // 基础信息
    public string? Gender { get; set; }
    public string? Birthday { get; set; }
    public string? Region { get; set; }        // 国籍/地区
    public string? Description { get; set; }

    // 扩展信息（百度百科等丰富来源）
    public string? RealName { get; set; }       // 本名
    public string? Nickname { get; set; }       // 昵称
    public string? Ethnicity { get; set; }      // 民族
    public string? BirthPlace { get; set; }     // 出生地
    public string? Education { get; set; }      // 毕业院校
    public string? Zodiac { get; set; }         // 星座
    public string? Height { get; set; }         // 身高
    public string? Agency { get; set; }         // 经纪公司
    public string? RepresentativeWorks { get; set; }  // 代表作品
    public string? Occupation { get; set; }     // 职业

    // 通用扩展字段（JSON 格式存储其他信息）
    public string? ExtraInfo { get; set; }
}
